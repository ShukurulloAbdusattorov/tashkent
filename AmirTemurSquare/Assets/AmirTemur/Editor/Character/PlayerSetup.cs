using Unity.Cinemachine.TargetTracking;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.HighDefinition;

namespace AmirTemur.Editor
{
    /// <summary>
    /// Builds the player: animator controller (root-motion locomotion blend tree + jump / fall / land),
    /// the DefaultMale instance with CharacterController + ThirdPersonController + HUD, the HDRP main camera with a
    /// CinemachineBrain, and an orbital Cinemachine camera (three-ring orbit, rotation composer, input axis
    /// controller, deoccluder). Entry point: <see cref="Create"/>.
    /// </summary>
    public static class PlayerSetup
    {
        public const string ControllerPath = "Assets/AmirTemur/Art/Character/PlayerAnimator.controller";
        public const string MaterialPath = "Assets/AmirTemur/Art/Character/Models/Male/Materials/DefaultMale_HDRP.mat";
        public const string InputActionsPath = "Assets/InputSystem_Actions.inputactions";

        // Nominal speeds used when a clip has no measurable root motion.
        const float NominalWalk = 1.5f, NominalJog = 3.6f, NominalSprint = 6.0f;

        struct Speeds { public float walk, jog, sprint; }

        public static GameObject Create(Vector3 spawnPos)
        {
            // 1. Make sure the character assets are imported as humanoid + root motion.
            if (CharacterImportSetup.LoadAvatar(CharacterImportSetup.ModelPath) == null || CharacterImportSetup.FindClip("WalkForwards") == null)
                CharacterImportSetup.Run();

            Avatar avatar = CharacterImportSetup.LoadAvatar(CharacterImportSetup.ModelPath);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterImportSetup.ModelPath);
            if (model == null || avatar == null)
            {
                Debug.LogError("[PlayerSetup] DefaultMale model / avatar missing at " + CharacterImportSetup.ModelPath);
                return null;
            }

            // 2. Animator controller with thresholds measured from the clips' root motion.
            AnimatorController controller = BuildAnimatorController(out Speeds speeds);

            // 3. Player object.
            GameObject player = InstantiateModel(model, spawnPos);
            player.name = "Player";
            player.tag = "Player";

            AssignHdrpMaterial(player);

            Animator animator = player.GetComponent<Animator>();
            if (animator == null) animator = player.AddComponent<Animator>();
            animator.avatar = avatar;
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = true;
            animator.updateMode = AnimatorUpdateMode.Normal;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            CharacterController cc = player.GetComponent<CharacterController>();
            if (cc == null) cc = player.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.32f;
            cc.center = new Vector3(0f, 0.9f, 0f);
            cc.stepOffset = 0.35f;
            cc.slopeLimit = 45f;
            cc.skinWidth = 0.04f;
            cc.minMoveDistance = 0f;

            ThirdPersonController tpc = player.GetComponent<ThirdPersonController>();
            if (tpc == null) tpc = player.AddComponent<ThirdPersonController>();
            tpc.SetSpeeds(speeds.walk, speeds.jog, speeds.sprint);
            var actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);
            if (actions == null) Debug.LogWarning("[PlayerSetup] Input actions asset not found at " + InputActionsPath + "; the controller will fall back to InputSystem.actions.");
            tpc.SetInputActions(actions);

            if (player.GetComponent<PlayerHUD>() == null) player.AddComponent<PlayerHUD>();

            // Camera look-at / follow target at head height.
            Transform camTarget = player.transform.Find("CameraTarget");
            if (camTarget == null)
            {
                camTarget = new GameObject("CameraTarget").transform;
                camTarget.SetParent(player.transform, false);
            }
            camTarget.localPosition = new Vector3(0f, 1.6f, 0f);
            camTarget.localRotation = Quaternion.identity;

            // 4. Cameras.
            Camera mainCam = EnsureMainCamera(spawnPos);
            CreateCinemachineRig(player, camTarget, actions);

            // 5. Day / night cycle (frozen by default; keys 1-4 change the time).
            EnsureDayNightCycle();

            EditorUtility.SetDirty(player);
            Debug.Log("[PlayerSetup] Player created at " + spawnPos + " (walk " + speeds.walk.ToString("0.00") + ", jog " + speeds.jog.ToString("0.00") + ", sprint " + speeds.sprint.ToString("0.00") + " m/s). Camera: " + mainCam.name);
            return player;
        }

        // ------------------------------------------------------------------ model

        static GameObject InstantiateModel(GameObject model, Vector3 spawnPos)
        {
            var instance = PrefabUtility.InstantiatePrefab(model) as GameObject;
            if (instance == null) instance = Object.Instantiate(model);
            if (PrefabUtility.IsPartOfPrefabInstance(instance))
                PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            instance.transform.position = spawnPos;
            instance.transform.rotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
            return instance;
        }

        static void AssignHdrpMaterial(GameObject player)
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (mat == null)
            {
                Shader lit = Shader.Find("HDRP/Lit");
                if (lit == null)
                {
                    Debug.LogWarning("[PlayerSetup] HDRP/Lit shader not found; keeping imported materials.");
                    return;
                }
                mat = new Material(lit) { name = "DefaultMale_HDRP" };
                string texRoot = CharacterImportSetup.TextureRoot;
                var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(texRoot + "/DefaultMale_Albedo.tif");
                var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(texRoot + "/DefaultMale_Normal.tif");
                if (albedo != null) mat.SetTexture("_BaseColorMap", albedo);
                if (normal != null) { mat.SetTexture("_NormalMap", normal); mat.SetFloat("_NormalScale", 1f); }
                mat.SetColor("_BaseColor", Color.white);
                mat.SetFloat("_Smoothness", 0.35f);
                mat.SetFloat("_Metallic", 0f);
                HDMaterial.ValidateMaterial(mat);
                AssetDatabase.CreateAsset(mat, MaterialPath);
            }

            foreach (SkinnedMeshRenderer smr in player.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mats = new Material[Mathf.Max(1, smr.sharedMaterials.Length)];
                for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                smr.sharedMaterials = mats;
                smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                smr.updateWhenOffscreen = true;
            }
        }

        // ------------------------------------------------------------------ animator controller

        static float ClipSpeed(AnimationClip clip)
        {
            if (clip == null) return 0f;
            Vector3 v = clip.averageSpeed;
            return new Vector2(v.x, v.z).magnitude;
        }

        static float Pick(float measured, float nominal, string label)
        {
            if (measured > 0.3f) return measured;
            Debug.LogWarning("[PlayerSetup] Clip '" + label + "' has no measurable root motion (" + measured.ToString("0.00") + " m/s); using nominal " + nominal + " m/s.");
            return nominal;
        }

        static AnimatorController BuildAnimatorController(out Speeds speeds)
        {
            AnimationClip idle = CharacterImportSetup.FindClip("Idle");
            AnimationClip walk = CharacterImportSetup.FindClip("WalkForwards");
            AnimationClip run = CharacterImportSetup.FindClip("RunForwards");
            AnimationClip sprint = CharacterImportSetup.FindClip("SprintForwards");
            AnimationClip turnL = CharacterImportSetup.FindClip("TurningLeft");
            AnimationClip turnR = CharacterImportSetup.FindClip("TurningRight");
            AnimationClip walkTurnL = CharacterImportSetup.FindClip("TurningLeftWalkForward");
            AnimationClip walkTurnR = CharacterImportSetup.FindClip("TurningRightWalkForward");
            AnimationClip runTurnL = CharacterImportSetup.FindClip("TurningLeftRunForward");
            AnimationClip runTurnR = CharacterImportSetup.FindClip("TurningRightRunForward");
            AnimationClip sprintTurnL = CharacterImportSetup.FindClip("TurningLeftSprintForwards");
            AnimationClip sprintTurnR = CharacterImportSetup.FindClip("TurningRightSprintForwards");
            AnimationClip falling = CharacterImportSetup.FindClip("Falling");
            AnimationClip land = CharacterImportSetup.FindClip("FallLand");
            AnimationClip jumpIdle = CharacterImportSetup.FindClip("StandingJump_Frame01");
            AnimationClip jumpRun = CharacterImportSetup.FindClip("RunForwardsJump_Frame01");
            AnimationClip jumpSprint = CharacterImportSetup.FindClip("SprintForwardsJump_Frame01");

            if (idle == null || walk == null || run == null || sprint == null)
                Debug.LogError("[PlayerSetup] Core locomotion clips missing (Idle/WalkForwards/RunForwards/SprintForwards). Check CharacterImportSetup.");

            speeds = new Speeds
            {
                walk = Pick(ClipSpeed(walk), NominalWalk, "WalkForwards"),
                jog = Pick(ClipSpeed(run), NominalJog, "RunForwards"),
                sprint = Pick(ClipSpeed(sprint), NominalSprint, "SprintForwards")
            };
            // Keep the tiers strictly increasing so the 1D/2D trees stay well-formed.
            if (speeds.jog <= speeds.walk + 0.2f) speeds.jog = speeds.walk + 1.5f;
            if (speeds.sprint <= speeds.jog + 0.2f) speeds.sprint = speeds.jog + 2f;

            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null)
                AssetDatabase.DeleteAsset(ControllerPath);
            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Turn", AnimatorControllerParameterType.Float);
            controller.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Jump", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Falling", AnimatorControllerParameterType.Bool);
            // Grounded defaults to true so the default state is locomotion.
            AnimatorControllerParameter[] pars = controller.parameters;
            for (int i = 0; i < pars.Length; i++) if (pars[i].name == "Grounded") pars[i].defaultBool = true;
            controller.parameters = pars;

            AnimatorStateMachine sm = controller.layers[0].stateMachine;

            // --- Locomotion: 2D freeform cartesian tree on (Turn, Speed). Cartesian is the right flavour here
            // because Turn is a lean amount, not a direction, and the two axes have different scales.
            AnimatorState locomotion = controller.CreateBlendTreeInController("Locomotion", out BlendTree loco, 0);
            loco.blendType = BlendTreeType.FreeformCartesian2D;
            loco.blendParameter = "Turn";
            loco.blendParameterY = "Speed";
            loco.useAutomaticThresholds = false;
            AddChild2D(loco, idle, 0f, 0f);
            AddChild2D(loco, turnL ?? idle, -1f, 0f);
            AddChild2D(loco, turnR ?? idle, 1f, 0f);
            AddChild2D(loco, walk, 0f, speeds.walk);
            AddChild2D(loco, walkTurnL ?? walk, -1f, speeds.walk);
            AddChild2D(loco, walkTurnR ?? walk, 1f, speeds.walk);
            AddChild2D(loco, run, 0f, speeds.jog);
            AddChild2D(loco, runTurnL ?? run, -1f, speeds.jog);
            AddChild2D(loco, runTurnR ?? run, 1f, speeds.jog);
            AddChild2D(loco, sprint, 0f, speeds.sprint);
            AddChild2D(loco, sprintTurnL ?? sprint, -1f, speeds.sprint);
            AddChild2D(loco, sprintTurnR ?? sprint, 1f, speeds.sprint);
            sm.defaultState = locomotion;

            // --- Jump: 1D on Speed (standing / running / sprinting take-off).
            AnimatorState jump = controller.CreateBlendTreeInController("Jump", out BlendTree jumpTree, 0);
            jumpTree.blendType = BlendTreeType.Simple1D;
            jumpTree.blendParameter = "Speed";
            jumpTree.useAutomaticThresholds = false;
            AnimationClip jumpBase = jumpIdle ?? jumpRun ?? jumpSprint ?? falling ?? idle;
            AddChild1D(jumpTree, jumpBase, 0f);
            AddChild1D(jumpTree, jumpRun ?? jumpBase, speeds.jog);
            AddChild1D(jumpTree, jumpSprint ?? jumpRun ?? jumpBase, speeds.sprint);

            AnimatorState fall = sm.AddState("Falling");
            fall.motion = falling ?? jumpBase;

            AnimatorState landState = sm.AddState("Land");
            landState.motion = land ?? idle;

            // --- Transitions.
            AnimatorStateTransition anyToJump = sm.AddAnyStateTransition(jump);
            anyToJump.hasExitTime = false;
            anyToJump.hasFixedDuration = true;
            anyToJump.duration = 0.08f;
            anyToJump.canTransitionToSelf = false;
            anyToJump.AddCondition(AnimatorConditionMode.If, 0f, "Jump");

            AnimatorStateTransition locoToFall = locomotion.AddTransition(fall);
            locoToFall.hasExitTime = false;
            locoToFall.hasFixedDuration = true;
            locoToFall.duration = 0.25f;
            locoToFall.AddCondition(AnimatorConditionMode.If, 0f, "Falling");

            AnimatorStateTransition jumpToFallExit = jump.AddTransition(fall);
            jumpToFallExit.hasExitTime = true;
            jumpToFallExit.exitTime = 0.9f;
            jumpToFallExit.hasFixedDuration = true;
            jumpToFallExit.duration = 0.25f;

            AnimatorStateTransition jumpToFall = jump.AddTransition(fall);
            jumpToFall.hasExitTime = false;
            jumpToFall.hasFixedDuration = true;
            jumpToFall.duration = 0.25f;
            jumpToFall.AddCondition(AnimatorConditionMode.If, 0f, "Falling");

            AnimatorStateTransition jumpToLand = jump.AddTransition(landState);
            jumpToLand.hasExitTime = true;
            jumpToLand.exitTime = 0.35f;
            jumpToLand.hasFixedDuration = true;
            jumpToLand.duration = 0.1f;
            jumpToLand.AddCondition(AnimatorConditionMode.If, 0f, "Grounded");

            AnimatorStateTransition fallToLand = fall.AddTransition(landState);
            fallToLand.hasExitTime = false;
            fallToLand.hasFixedDuration = true;
            fallToLand.duration = 0.1f;
            fallToLand.AddCondition(AnimatorConditionMode.If, 0f, "Grounded");

            AnimatorStateTransition landToLoco = landState.AddTransition(locomotion);
            landToLoco.hasExitTime = true;
            landToLoco.exitTime = 0.65f;
            landToLoco.hasFixedDuration = true;
            landToLoco.duration = 0.25f;

            AnimatorStateTransition landInterrupt = landState.AddTransition(locomotion);
            landInterrupt.hasExitTime = false;
            landInterrupt.hasFixedDuration = true;
            landInterrupt.duration = 0.2f;
            landInterrupt.AddCondition(AnimatorConditionMode.Greater, speeds.walk * 0.8f, "Speed");
            landInterrupt.AddCondition(AnimatorConditionMode.If, 0f, "Grounded");

            AnimatorStateTransition landToFall = landState.AddTransition(fall);
            landToFall.hasExitTime = false;
            landToFall.hasFixedDuration = true;
            landToFall.duration = 0.2f;
            landToFall.AddCondition(AnimatorConditionMode.If, 0f, "Falling");

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            return controller;
        }

        static void AddChild2D(BlendTree tree, AnimationClip clip, float x, float y)
        {
            if (clip == null) return;
            tree.AddChild(clip, new Vector2(x, y));
        }

        static void AddChild1D(BlendTree tree, AnimationClip clip, float threshold)
        {
            if (clip == null) return;
            tree.AddChild(clip, threshold);
        }

        // ------------------------------------------------------------------ cameras

        static Camera EnsureMainCamera(Vector3 spawnPos)
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera");
                cam = go.AddComponent<Camera>();
                go.transform.position = spawnPos + new Vector3(0f, 1.8f, -4.5f);
                go.transform.LookAt(spawnPos + Vector3.up * 1.6f);
            }
            cam.gameObject.tag = "MainCamera";
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 3000f;
            cam.fieldOfView = 50f;
            cam.allowHDR = true;

            HDAdditionalCameraData hd = cam.GetComponent<HDAdditionalCameraData>();
            if (hd == null) hd = cam.gameObject.AddComponent<HDAdditionalCameraData>();
            hd.antialiasing = HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing;
            hd.dithering = true;
            hd.stopNaNs = true;
            hd.volumeLayerMask = ~0;

            if (cam.GetComponent<AudioListener>() == null) cam.gameObject.AddComponent<AudioListener>();

            CinemachineBrain brain = cam.GetComponent<CinemachineBrain>();
            if (brain == null) brain = cam.gameObject.AddComponent<CinemachineBrain>();
            brain.UpdateMethod = CinemachineBrain.UpdateMethods.LateUpdate;
            brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.EaseInOut, 1f);
            return cam;
        }

        static void CreateCinemachineRig(GameObject player, Transform camTarget, InputActionAsset projectActions)
        {
            GameObject existing = GameObject.Find("PlayerCamera");
            if (existing != null) Object.DestroyImmediate(existing);

            var go = new GameObject("PlayerCamera");
            go.transform.position = camTarget.position + new Vector3(0f, 0.3f, -4.5f);
            go.transform.LookAt(camTarget.position);

            CinemachineCamera vcam = go.AddComponent<CinemachineCamera>();
            vcam.Priority = 10;
            vcam.Target.TrackingTarget = camTarget;
            vcam.Target.LookAtTarget = camTarget;
            vcam.Target.CustomLookAtTarget = false;
            vcam.Lens.FieldOfView = 50f;
            vcam.Lens.NearClipPlane = 0.1f;
            vcam.Lens.FarClipPlane = 3000f;

            // Orbital follow: three-ring rig. Heights are relative to the CameraTarget (1.6 m above the feet),
            // so world heights are 4.0 / 1.8 / 0.6 m as designed.
            CinemachineOrbitalFollow orbit = go.AddComponent<CinemachineOrbitalFollow>();
            orbit.OrbitStyle = CinemachineOrbitalFollow.OrbitStyles.ThreeRing;
            orbit.Orbits = new Cinemachine3OrbitRig.Settings
            {
                Top = new Cinemachine3OrbitRig.Orbit { Height = 4.0f - 1.6f, Radius = 2.5f },
                Center = new Cinemachine3OrbitRig.Orbit { Height = 1.8f - 1.6f, Radius = 4.5f },
                Bottom = new Cinemachine3OrbitRig.Orbit { Height = 0.6f - 1.6f, Radius = 3.5f },
                SplineCurvature = 0.5f
            };
            orbit.TargetOffset = Vector3.zero;
            orbit.TrackerSettings = TrackerSettings.Default;
            orbit.TrackerSettings.BindingMode = BindingMode.WorldSpace;
            orbit.TrackerSettings.PositionDamping = new Vector3(0.35f, 0.6f, 0.35f);
            orbit.HorizontalAxis = new InputAxis
            {
                Value = 0f, Center = 0f, Range = new Vector2(-180f, 180f), Wrap = true,
                Recentering = new InputAxis.RecenteringSettings { Enabled = false, Wait = 1f, Time = 2f }
            };
            orbit.VerticalAxis = new InputAxis
            {
                Value = 0.15f, Center = 0.15f, Range = new Vector2(-1f, 1f), Wrap = false,
                Recentering = new InputAxis.RecenteringSettings { Enabled = false, Wait = 1f, Time = 2f }
            };
            orbit.RadialAxis = new InputAxis
            {
                Value = 1f, Center = 1f, Range = new Vector2(1f, 1f), Wrap = false,
                Recentering = new InputAxis.RecenteringSettings { Enabled = false, Wait = 1f, Time = 2f }
            };

            CinemachineRotationComposer composer = go.AddComponent<CinemachineRotationComposer>();
            composer.Composition = ScreenComposerSettings.Default;
            composer.Composition.ScreenPosition = new Vector2(-0.1f, 0.02f); // character slightly left of centre
            composer.Damping = new Vector2(0.3f, 0.3f);
            composer.TargetOffset = Vector3.zero;
            composer.CenterOnActivate = true;

            // Input: mouse delta + right stick through the Input System.
            CinemachineInputAxisController input = go.AddComponent<CinemachineInputAxisController>();
            input.ScanRecursively = true;
            input.SuppressInputWhileBlending = true;
            input.SynchronizeControllers();
            InputActionReference look = LoadLookReference();
            foreach (CinemachineInputAxisController.Controller c in input.Controllers)
            {
                if (c.Name.Contains("Look"))
                {
                    c.Enabled = true;
                    if (look != null) c.Input.InputAction = look;
                    c.Input.Gain = c.Name.Contains("Y") ? -1f : 1f;
                    c.Input.CancelDeltaTime = false;
                    c.Driver = DefaultInputAxisDriver.Default;
                    c.Driver.AccelTime = 0.12f;
                    c.Driver.DecelTime = 0.12f;
                }
                else
                {
                    c.Enabled = false; // no scroll-wheel orbit scaling
                }
            }

            // Camera collision.
            CinemachineDeoccluder deoccluder = go.AddComponent<CinemachineDeoccluder>();
            deoccluder.CollideAgainst = ~0 & ~(1 << 2); // everything except "Ignore Raycast"
            deoccluder.IgnoreTag = "Player";
            deoccluder.MinimumDistanceFromTarget = 0.3f;
            deoccluder.AvoidObstacles.Enabled = true;
            deoccluder.AvoidObstacles.DistanceLimit = 0f;
            deoccluder.AvoidObstacles.MinimumOcclusionTime = 0f;
            deoccluder.AvoidObstacles.CameraRadius = 0.25f;
            deoccluder.AvoidObstacles.Strategy = CinemachineDeoccluder.ObstacleAvoidance.ResolutionStrategy.PullCameraForward;
            deoccluder.AvoidObstacles.SmoothingTime = 0.25f;
            deoccluder.AvoidObstacles.Damping = 0.3f;
            deoccluder.AvoidObstacles.DampingWhenOccluded = 0.05f;

            EditorUtility.SetDirty(go);
        }

        /// <summary>Cinemachine's bundled "CM Default/Look" action (mouse delta with DeltaTimeScale + right stick, both scaled
        /// for the axis driver). Falls back to the project's Player/Look reference if the package asset is unavailable.</summary>
        static InputActionReference LoadLookReference()
        {
            string cmPath = CinemachineCore.kPackageRoot + "/Runtime/Input/CinemachineDefaultInputActions.inputactions";
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(cmPath))
            {
                if (o is InputActionReference r && r.name == "CM Default/Look" && (r.hideFlags & HideFlags.HideInHierarchy) == 0) return r;
            }
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(InputActionsPath))
            {
                if (o is InputActionReference r && r.name == "Player/Look" && (r.hideFlags & HideFlags.HideInHierarchy) == 0) return r;
            }
            Debug.LogWarning("[PlayerSetup] No Look InputActionReference found; Cinemachine input controller keeps its defaults.");
            return null;
        }

        // ------------------------------------------------------------------ day / night

        static void EnsureDayNightCycle()
        {
            if (Object.FindFirstObjectByType<DayNightCycle>() != null) return;
            Light sun = null;
            foreach (Light l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (l.type == LightType.Directional) { sun = l; break; }
            }
            var go = new GameObject("DayNightCycle");
            DayNightCycle cycle = go.AddComponent<DayNightCycle>();
            var so = new SerializedObject(cycle);
            SerializedProperty sunProp = so.FindProperty("sun");
            if (sunProp != null) sunProp.objectReferenceValue = sun;
            SerializedProperty dayLength = so.FindProperty("dayLengthMinutes");
            if (dayLength != null) dayLength.floatValue = 0f; // frozen; keys 1-4 / inspector change the time
            SerializedProperty apply = so.FindProperty("applyOnStart");
            if (apply != null) apply.boolValue = false; // keep SceneSetup's authored sun until a key is pressed
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(go);
        }
    }
}
