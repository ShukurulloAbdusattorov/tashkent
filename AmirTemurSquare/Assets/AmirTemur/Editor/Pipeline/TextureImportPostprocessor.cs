using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace AmirTemur.Editor
{
    /// <summary>
    /// Import rules for everything under Assets/AmirTemur/Art/ (Character/ is owned by another module and is skipped).
    /// Textures: naming produced by data/process_assets.py
    ///   ambientCG  : &lt;Set&gt;_BaseColor.jpg|png, &lt;Set&gt;_Normal.jpg (GL), &lt;Set&gt;_Mask.png (R metal, G AO, B 0, A smooth), &lt;Set&gt;_Height.jpg
    ///   Poly Haven : &lt;prefix&gt;_diff_2k.(jpg|png), &lt;prefix&gt;_nor_gl_2k.(png|exr), raw &lt;prefix&gt;_(rough|metal|ao|arm|alpha|mask|disp|opacity|spec)_2k.*,
    ///                generated &lt;prefix&gt;_Mask.png and &lt;prefix&gt;_BaseColorA.png (base colour + alpha)
    ///   HDRI       : *.hdr -> cubemap
    /// Models: Assets/AmirTemur/Art/Models/**.fbx (Poly Haven, FBX UnitScaleFactor 1 with node scale 100 -> Unity file scale 0.01 == metres).
    /// The same Apply* methods are used by AssetImportSetup to re-check already imported assets (deterministic regardless of postprocessor versioning).
    /// </summary>
    public class TextureImportPostprocessor : AssetPostprocessor
    {
        public const string ArtRoot = "Assets/AmirTemur/Art/";
        public const string CharacterRoot = "Assets/AmirTemur/Art/Character/";
        public const string ModelsRoot = "Assets/AmirTemur/Art/Models/";
        public const string PlatformName = "Standalone";
        public const int MaxSize = 2048;
        /// <summary>Unity 6.2+ automatic Mesh LOD generation on import (ModelImporter.generateMeshLods).</summary>
        public const bool EnableMeshLod = true;

        public override uint GetVersion() => 4;

        public static bool Owns(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            path = path.Replace('\\', '/');
            return path.StartsWith(ArtRoot, StringComparison.OrdinalIgnoreCase)
                && !path.StartsWith(CharacterRoot, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsModelPath(string path)
        {
            if (!Owns(path)) return false;
            path = path.Replace('\\', '/');
            return path.StartsWith(ModelsRoot, StringComparison.OrdinalIgnoreCase)
                && path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase);
        }

        void OnPreprocessTexture()
        {
            if (!Owns(assetPath)) return;
            if (assetImporter is TextureImporter ti) ApplyTexture(ti, assetPath);
        }

        void OnPreprocessModel()
        {
            if (!IsModelPath(assetPath)) return;
            if (assetImporter is ModelImporter mi) ApplyModel(mi, assetPath);
        }

        // ------------------------------------------------------------------ textures

        public enum TexKind { BaseColor, BaseColorAlpha, Normal, Linear, Hdr }

        public static TexKind Classify(string path)
        {
            string file = Path.GetFileName(path).ToLowerInvariant();
            if (file.EndsWith(".hdr")) return TexKind.Hdr;
            if (file.Contains("_normal.") || file.Contains("_nor_gl_") || file.Contains("_nor_dx_")) return TexKind.Normal;
            if (file.EndsWith("_basecolora.png")) return TexKind.BaseColorAlpha;
            if (file.EndsWith("_mask.png") || file.Contains("_height.") || file.Contains("_arm_") || file.Contains("_rough_")
                || file.Contains("_alpha_") || file.Contains("_mask_") || file.Contains("_metal_") || file.Contains("_ao_")
                || file.Contains("_disp_") || file.Contains("_opacity_") || file.Contains("_spec_"))
                return TexKind.Linear;
            return TexKind.BaseColor;
        }

        static string Signature(TextureImporter i)
        {
            var p = i.GetPlatformTextureSettings(PlatformName);
            return $"{i.textureType}|{i.sRGBTexture}|{i.alphaIsTransparency}|{i.maxTextureSize}|{i.textureCompression}|{i.streamingMipmaps}|{i.mipmapEnabled}|{i.anisoLevel}|{i.textureShape}|{i.generateCubemap}|{p.overridden}|{p.format}|{p.maxTextureSize}|{p.textureCompression}";
        }

        /// <summary>Applies the import rules; returns true when any setting changed (caller may SaveAndReimport).</summary>
        public static bool ApplyTexture(TextureImporter i, string path)
        {
            string before = Signature(i);
            TexKind kind = Classify(path);

            i.textureType = kind == TexKind.Normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            i.sRGBTexture = kind == TexKind.BaseColor || kind == TexKind.BaseColorAlpha;
            i.alphaIsTransparency = kind == TexKind.BaseColorAlpha;
            i.maxTextureSize = MaxSize;
            i.mipmapEnabled = true;
            i.streamingMipmaps = kind != TexKind.Hdr;
            i.anisoLevel = 8;
            i.textureCompression = kind == TexKind.Hdr ? TextureImporterCompression.Uncompressed : TextureImporterCompression.CompressedHQ;
            if (kind == TexKind.Hdr)
            {
                i.textureShape = TextureImporterShape.TextureCube;
                i.generateCubemap = TextureImporterGenerateCubemap.AutoCubemap;
            }
            else
            {
                i.textureShape = TextureImporterShape.Texture2D;
            }

            var ps = i.GetPlatformTextureSettings(PlatformName);
            ps.name = PlatformName;
            ps.overridden = true;
            ps.maxTextureSize = MaxSize;
            ps.textureCompression = TextureImporterCompression.CompressedHQ;
            ps.format = kind == TexKind.Normal ? TextureImporterFormat.BC5
                      : kind == TexKind.Hdr ? TextureImporterFormat.BC6H
                      : TextureImporterFormat.BC7;
            i.SetPlatformTextureSettings(ps);

            return Signature(i) != before;
        }

        // ------------------------------------------------------------------ models

        static string Signature(ModelImporter m)
        {
            string s = $"{m.materialImportMode}|{m.materialLocation}|{m.generateSecondaryUV}|{m.meshCompression}|{m.isReadable}|{m.importBlendShapes}|{m.importCameras}|{m.importLights}|{m.importAnimation}|{m.animationType}|{m.useFileScale}|{m.globalScale}|{m.importNormals}|{m.importTangents}";
#if UNITY_6000_2_OR_NEWER
            s += $"|{m.generateMeshLods}";
#endif
            return s;
        }

        /// <summary>Applies the model import rules; returns true when any setting changed.</summary>
        public static bool ApplyModel(ModelImporter m, string path)
        {
            string before = Signature(m);

            // Materials are built by AssetImportSetup and applied through importer remaps (AddRemap). Remaps are only honoured
            // when material import is on (with materialImportMode = None Unity assigns the default material and ignores remaps).
            m.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            m.materialLocation = ModelImporterMaterialLocation.InPrefab;
            m.generateSecondaryUV = false;
            m.meshCompression = ModelImporterMeshCompression.Off;
            m.isReadable = false;
            m.importBlendShapes = false;
            m.importCameras = false;
            m.importLights = false;
            m.importAnimation = false;
            m.animationType = ModelImporterAnimationType.None;
            // Poly Haven FBX: UnitScaleFactor = 1 (cm) and every node carries scale 100 -> Unity file scale 0.01 yields metres.
            m.useFileScale = true;
            m.globalScale = 1f;
            m.importNormals = ModelImporterNormals.Import;
            m.importTangents = ModelImporterTangents.CalculateMikk;
#if UNITY_6000_2_OR_NEWER
            m.generateMeshLods = EnableMeshLod;
#endif
            return Signature(m) != before;
        }
    }
}
