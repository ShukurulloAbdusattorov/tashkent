using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace AmirTemur.Editor
{
    /// <summary>
    /// Configures the ModelImporter of the Unity Standard-Assets-Characters "DefaultMale" model and of every
    /// mocap clip under Animation/Male/Animations for Humanoid + root-motion use.
    /// Entry point: <see cref="Run"/> (idempotent; safe to call on every build).
    /// </summary>
    public static class CharacterImportSetup
    {
        public const string CharacterRoot = "Assets/AmirTemur/Art/Character";
        public const string ModelPath = CharacterRoot + "/Models/Male/Models/DefaultMale.fbx";
        public const string AnimationRoot = CharacterRoot + "/Animation/Male/Animations";
        public const string TextureRoot = CharacterRoot + "/Models/Male/Textures";

        /// <summary>Per-clip import settings derived from the clip name (file stem without the "m@" prefix).</summary>
        struct ClipRule
        {
            public bool loop;          // loop time + loop pose
            public bool rootRotation;  // keep root rotation as root motion (lockRootRotation = false)
            public bool rootXZ;        // keep XZ root motion (lockRootPositionXZ = false)
            public bool lockHeight;    // bake vertical root motion into the pose
            public bool mirrorCopy;    // also create a mirrored "...Right..." clip from a "...Left..." clip
        }

        static ClipRule Rule(string clipName)
        {
            bool aerial = clipName.Contains("Jump") || clipName == "Falling" || clipName.StartsWith("FallLand");
            bool turning = clipName.Contains("Turn");
            bool idle = clipName == "Idle" || clipName == "StrafeIdle";
            bool oneShot = aerial || clipName.Contains("180");

            return new ClipRule
            {
                loop = !oneShot,
                rootRotation = turning,
                rootXZ = !idle,
                lockHeight = !aerial, // aerial: vertical root motion is discarded by the controller anyway (physics drives Y)
                mirrorCopy = turning && clipName.Contains("Left") && !clipName.Contains("Strafe")
            };
        }

        public static void Run()
        {
            if (AssetDatabase.LoadMainAssetAtPath(ModelPath) == null)
            {
                Debug.LogError("[CharacterImportSetup] Model not found: " + ModelPath);
                return;
            }

            SetupTextures();
            SetupModel();

            Avatar avatar = LoadAvatar(ModelPath);
            if (avatar == null)
            {
                Debug.LogError("[CharacterImportSetup] DefaultMale avatar was not created; aborting animation setup.");
                return;
            }

            int count = 0;
            foreach (string path in FindAnimationFbx())
            {
                if (SetupAnimation(path, avatar)) count++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[CharacterImportSetup] Configured DefaultMale + " + count + " animation files.");
        }

        // ------------------------------------------------------------------ model + textures

        static void SetupModel()
        {
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null) return;

            bool changed = false;
            if (importer.animationType != ModelImporterAnimationType.Human) { importer.animationType = ModelImporterAnimationType.Human; changed = true; }
            if (importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel) { importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel; changed = true; }
            if (importer.importAnimation) { importer.importAnimation = false; changed = true; }
            if (importer.materialImportMode != ModelImporterMaterialImportMode.None) { importer.materialImportMode = ModelImporterMaterialImportMode.None; changed = true; }
            if (importer.importCameras) { importer.importCameras = false; changed = true; }
            if (importer.importLights) { importer.importLights = false; changed = true; }
            if (importer.optimizeGameObjects) { importer.optimizeGameObjects = false; changed = true; }
            if (!importer.useFileScale) { importer.useFileScale = true; changed = true; }
            if (Math.Abs(importer.globalScale - 1f) > 1e-4f) { importer.globalScale = 1f; changed = true; }

            if (changed) importer.SaveAndReimport();
        }

        static void SetupTextures()
        {
            if (!AssetDatabase.IsValidFolder(TextureRoot)) return;
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { TextureRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var ti = AssetImporter.GetAtPath(path) as TextureImporter;
                if (ti == null) continue;
                string stem = Path.GetFileNameWithoutExtension(path);
                bool changed = false;
                if (stem.EndsWith("_Normal", StringComparison.OrdinalIgnoreCase))
                {
                    if (ti.textureType != TextureImporterType.NormalMap) { ti.textureType = TextureImporterType.NormalMap; changed = true; }
                }
                else if (stem.EndsWith("_Albedo", StringComparison.OrdinalIgnoreCase))
                {
                    if (!ti.sRGBTexture) { ti.sRGBTexture = true; changed = true; }
                }
                else
                {
                    if (ti.sRGBTexture) { ti.sRGBTexture = false; changed = true; }
                }
                if (ti.maxTextureSize < 2048) { ti.maxTextureSize = 2048; changed = true; }
                if (changed) ti.SaveAndReimport();
            }
        }

        public static Avatar LoadAvatar(string modelPath)
        {
            foreach (UnityEngine.Object o in AssetDatabase.LoadAllAssetsAtPath(modelPath))
            {
                if (o is Avatar a && a.isValid) return a;
            }
            return null;
        }

        // ------------------------------------------------------------------ animations

        public static List<string> FindAnimationFbx()
        {
            var result = new List<string>();
            if (!AssetDatabase.IsValidFolder(AnimationRoot)) return result;
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { AnimationRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase) && !result.Contains(path)) result.Add(path);
            }
            result.Sort(StringComparer.Ordinal);
            return result;
        }

        /// <summary>"m@WalkForwards.fbx" -> "WalkForwards".</summary>
        public static string ClipNameFromPath(string assetPath)
        {
            string stem = Path.GetFileNameWithoutExtension(assetPath);
            int at = stem.IndexOf('@');
            return at >= 0 ? stem.Substring(at + 1) : stem;
        }

        static bool SetupAnimation(string path, Avatar sourceAvatar)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) return false;

            string clipName = ClipNameFromPath(path);
            ClipRule rule = Rule(clipName);

            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel; // own avatar per clip: humanoid retargeting handles rig differences (CopyFromOther logged rig mismatches)
            importer.sourceAvatar = null;
            importer.importAnimation = true;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importBlendShapes = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.motionNodeName = string.Empty; // root motion from the humanoid root; nothing overridden
            importer.animationCompression = ModelImporterAnimationCompression.Optimal;

            // Take the file's default take(s), rename to the file stem and apply the root-motion settings.
            ModelImporterClipAnimation[] defaults = importer.defaultClipAnimations;
            if (defaults == null || defaults.Length == 0)
            {
                Debug.LogWarning("[CharacterImportSetup] No animation takes in " + path);
                importer.SaveAndReimport();
                return false;
            }

            var clips = new List<ModelImporterClipAnimation> { Configure(defaults[0], clipName, rule, false) };
            if (rule.mirrorCopy)
            {
                // defaultClipAnimations returns fresh instances on each call, so this is an independent copy of the take.
                ModelImporterClipAnimation mirrorTake = importer.defaultClipAnimations[0];
                clips.Add(Configure(mirrorTake, clipName.Replace("Left", "Right"), rule, true));
            }
            importer.clipAnimations = clips.ToArray();
            importer.SaveAndReimport();
            return true;
        }

        static ModelImporterClipAnimation Configure(ModelImporterClipAnimation clip, string name, ClipRule rule, bool mirror)
        {
            clip.name = name;
            clip.loopTime = rule.loop;
            clip.loopPose = rule.loop;
            clip.cycleOffset = 0f;
            clip.mirror = mirror;

            // Root transform rotation: baked into the pose unless the clip is meant to turn the character.
            clip.lockRootRotation = !rule.rootRotation;
            clip.keepOriginalOrientation = false;   // based upon: body orientation

            // Root transform position Y: locked (physics drives the vertical axis) except for aerial clips.
            clip.lockRootHeightY = rule.lockHeight;
            clip.keepOriginalPositionY = false;
            clip.heightFromFeet = rule.lockHeight;  // based upon: feet, so locomotion stays planted on y = 0

            // Root transform position XZ: keep the mocap root motion (idles are baked so they don't drift).
            clip.lockRootPositionXZ = !rule.rootXZ;
            clip.keepOriginalPositionXZ = false;    // based upon: centre of mass

            clip.maskType = ClipAnimationMaskType.None;
            return clip;
        }

        // ------------------------------------------------------------------ lookup helpers used by PlayerSetup

        /// <summary>Finds the imported clip called <paramref name="clipName"/> among all male animation FBX files
        /// (mirrored "...Right..." clips live inside the corresponding "...Left..." file).</summary>
        public static AnimationClip FindClip(string clipName)
        {
            foreach (string path in FindAnimationFbx())
            {
                string stem = ClipNameFromPath(path);
                if (stem != clipName && stem.Replace("Left", "Right") != clipName) continue;
                foreach (UnityEngine.Object o in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (o is AnimationClip c && c.name == clipName) return c;
                }
            }
            return null;
        }
    }
}
