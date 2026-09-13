using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AmirTemur.Editor
{
    /// <summary>
    /// Stage 2 of BuildAll: new empty scene with the late-afternoon sun, the global HDRP volume (physically based sky, volumetric clouds,
    /// volumetric fog, exposure, post, SSAO/SSR/SSGI, contact/micro shadows, cascades), wind zone, reflection probe, lighting settings
    /// (no bake) and the project quality / HDRP asset configuration (High Fidelity as default).
    /// </summary>
    public static class SceneSetup
    {
        public const string SettingsFolder = "Assets/AmirTemur/Settings";
        public const string VolumeProfilePath = SettingsFolder + "/GlobalVolume.asset";
        public const string LightingSettingsPath = SettingsFolder + "/LightingSettings.lighting";
        public const string HighFidelityAssetPath = "Assets/Settings/HDRP High Fidelity.asset";
        public const string HighFidelityQualityName = "High Fidelity";

        /// <summary>Elevation 28 deg, azimuth 240 (west-south-west): the monument is front-lit from the square side.</summary>
        public static readonly Vector3 SunEuler = new Vector3(48f, 34f, 0f); // kloofendal_48d HDRI sun (elev 48°, azimuth 34°) rotated 180° -> sun in the SSW (azimuth 214°), light forward yaw 34°

        [MenuItem("AmirTemur/Create Scene")]
        public static void CreateSceneMenu() => CreateScene();

        public static Scene CreateScene()
        {
            try { ConfigureQualityAndPipeline(); }
            catch (Exception e) { Debug.LogError($"[SceneSetup] quality/pipeline configuration failed: {e}"); }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            Light sun = CreateSun();
            CreateGlobalVolume();
            CreateWindZone();
            CreateReflectionProbe();
            ConfigureLighting(sun);

            Debug.Log("[SceneSetup] scene created: Sun, Global Volume, WindZone, MonumentReflectionProbe");
            return scene;
        }

        // ------------------------------------------------------------------ sun

        static Light CreateSun()
        {
            var go = new GameObject("Sun");
            go.transform.rotation = Quaternion.Euler(SunEuler);
            HDAdditionalLightData hd = go.AddHDLight(LightType.Directional);
            var light = go.GetComponent<Light>();
            light.type = LightType.Directional;
            light.lightUnit = LightUnit.Lux;
            light.intensity = 90000f;
            light.useColorTemperature = true;
            light.colorTemperature = 5200f;
            light.color = Color.white;
            light.shadows = LightShadows.Soft;

            hd.angularDiameter = 0.53f;
            hd.interactsWithSky = true;
            hd.EnableShadows(true);
            hd.SetShadowResolutionOverride(true);
            hd.SetShadowResolution(4096);
            hd.shadowUpdateMode = ShadowUpdateMode.EveryFrame;
            hd.affectsVolumetric = true;
            hd.volumetricDimmer = 1f;
            hd.shadowDimmer = 1f;
            return light;
        }

        // ------------------------------------------------------------------ volume

        static T AddOverride<T>(VolumeProfile profile) where T : VolumeComponent
        {
            var c = profile.Add<T>(true);
            c.name = typeof(T).Name;
            c.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(c, profile);
            return c;
        }

        static void CreateGlobalVolume()
        {
            MeshUtil.EnsureFolder(SettingsFolder);
            if (AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath) != null) AssetDatabase.DeleteAsset(VolumeProfilePath);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, VolumeProfilePath);

            // --- sky / environment
            var env = AddOverride<VisualEnvironment>(profile);
            env.skyType.value = (int)SkyType.PhysicallyBased;
            env.cloudType.value = 0;                              // no CloudLayer: volumetric clouds are used
            env.skyAmbientMode.value = SkyAmbientMode.Dynamic;

            // photographed sky (Poly Haven CC0) — realistic clouds and horizon in one shot; sun direction matched in SunEuler
            var hdriTex = AssetDatabase.LoadAssetAtPath<Cubemap>("Assets/AmirTemur/Art/HDRI/kloofendal_48d_partly_cloudy_puresky_4k.hdr");
            if (hdriTex != null)
            {
                env.skyType.value = (int)SkyType.HDRI;
                var hdri = AddOverride<HDRISky>(profile);
                hdri.hdriSky.value = hdriTex;
                hdri.skyIntensityMode.value = SkyIntensityMode.Lux;
                hdri.desiredLuxValue.value = 20000f;
                hdri.rotation.value = 180f; // put the sun in the south-west so the square-facing facades are lit
                hdri.updateMode.value = EnvironmentUpdateMode.OnChanged;
            }
            else
            {
                Debug.LogWarning("[SceneSetup] HDRI cubemap not found, falling back to physically based sky");
                var sky = AddOverride<PhysicallyBasedSky>(profile);
                sky.type.value = PhysicallyBasedSkyModel.EarthSimple;
                sky.groundTint.value = new Color(0.16f, 0.13f, 0.10f);
                sky.exposure.value = 0f;
                sky.updateMode.value = EnvironmentUpdateMode.OnChanged;
            }

            var clouds = AddOverride<VolumetricClouds>(profile);
            clouds.enable.value = false; // the HDRI already contains clouds

            var fog = AddOverride<Fog>(profile);
            fog.enabled.value = true;
            fog.colorMode.value = FogColorMode.SkyColor;
            fog.meanFreePath.value = 2500f;
            fog.baseHeight.value = 0f;
            fog.maximumHeight.value = 150f;
            fog.maxFogDistance.value = 5000f;
            fog.enableVolumetricFog.value = true;
            fog.albedo.value = new Color(1.0f, 0.96f, 0.9f);
            fog.anisotropy.value = 0.4f;
            fog.globalLightProbeDimmer.value = 0.8f;
            fog.depthExtent.value = 128f;
            fog.quality.value = (int)ScalableSettingLevelParameter.Level.High;

            // --- exposure / tonemapping / grading
            var exposure = AddOverride<Exposure>(profile);
            exposure.mode.value = ExposureMode.Automatic;
            exposure.meteringMode.value = MeteringMode.CenterWeighted;
            exposure.limitMin.value = 7f;
            exposure.limitMax.value = 15f;
            exposure.compensation.value = 0f;
            exposure.adaptationMode.value = AdaptationMode.Progressive;
            exposure.adaptationSpeedDarkToLight.value = 3f;
            exposure.adaptationSpeedLightToDark.value = 1f;

            var tonemap = AddOverride<Tonemapping>(profile);
            tonemap.mode.value = TonemappingMode.ACES;

            var bloom = AddOverride<Bloom>(profile);
            bloom.intensity.value = 0.15f;
            bloom.scatter.value = 0.65f;

            var grade = AddOverride<ColorAdjustments>(profile);
            grade.postExposure.value = 0f;
            grade.contrast.value = 8f;
            grade.saturation.value = 5f;

            var wb = AddOverride<WhiteBalance>(profile);
            wb.temperature.value = 0f; // HDRP: positive = warmer (ColorUtils.ColorBalanceToLMSCoeffs); slight warmth
            wb.tint.value = 0f;

            var vignette = AddOverride<Vignette>(profile);
            vignette.intensity.value = 0.18f;
            vignette.smoothness.value = 0.3f;

            var smh = AddOverride<ShadowsMidtonesHighlights>(profile);
            smh.shadows.value = new Vector4(0.98f, 0.985f, 1.0f, -0.01f);
            smh.midtones.value = new Vector4(1f, 1f, 1f, 0f);
            smh.highlights.value = new Vector4(1.02f, 1.0f, 0.97f, 0.02f);

            var motionBlur = AddOverride<MotionBlur>(profile);
            motionBlur.intensity.value = 0.25f;

            var grain = AddOverride<FilmGrain>(profile);
            grain.intensity.value = 0f;

            // --- lighting
            var ssao = AddOverride<ScreenSpaceAmbientOcclusion>(profile);
            ssao.intensity.value = 1.2f;
            ssao.radius.value = 1.5f;
            ssao.quality.value = (int)ScalableSettingLevelParameter.Level.High;
            ssao.temporalAccumulation.value = true;
            ssao.rayTracing.value = false;

            var ssr = AddOverride<ScreenSpaceReflection>(profile);
            ssr.enabled.value = true;
            ssr.enabledTransparent.value = true;
            ssr.tracing.value = RayCastingMode.RayMarching; // ray tracing off
            ssr.usedAlgorithm.value = ScreenSpaceReflectionAlgorithm.PBRAccumulation;
            ssr.quality.value = (int)ScalableSettingLevelParameter.Level.High;

            var gi = AddOverride<GlobalIllumination>(profile);
            gi.enable.value = true;
            gi.tracing.value = RayCastingMode.RayMarching;
            gi.quality.value = (int)ScalableSettingLevelParameter.Level.High;

            var contact = AddOverride<ContactShadows>(profile);
            contact.enable.value = true;
            contact.length.value = 0.2f;
            contact.opacity.value = 1f;
            contact.quality.value = (int)ScalableSettingLevelParameter.Level.High;

            var micro = AddOverride<MicroShadowing>(profile);
            micro.enable.value = true;
            micro.opacity.value = 0.7f;

            var shadows = AddOverride<HDShadowSettings>(profile);
            shadows.maxShadowDistance.value = 350f;
            shadows.cascadeShadowSplitCount.value = 4;
            shadows.cascadeShadowSplit0.value = 0.05f;
            shadows.cascadeShadowSplit1.value = 0.15f;
            shadows.cascadeShadowSplit2.value = 0.35f;

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();

            var go = new GameObject("Global Volume");
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 0f;
            volume.sharedProfile = profile;
        }

        /// <summary>Sets value + overrideState on a private serialized VolumeParameter (e.g. VolumetricClouds.m_CloudPreset).</summary>
        static void SetPrivateOverride(VolumeComponent component, string fieldName, int enumValue)
        {
            var so = new SerializedObject(component);
            var p = so.FindProperty(fieldName);
            if (p == null) { Debug.LogWarning($"[SceneSetup] {component.GetType().Name}.{fieldName} not found"); return; }
            var v = p.FindPropertyRelative("m_Value");
            var o = p.FindPropertyRelative("m_OverrideState");
            if (v != null) { if (v.propertyType == SerializedPropertyType.Enum) v.enumValueIndex = enumValue; else v.intValue = enumValue; }
            if (o != null) o.boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ------------------------------------------------------------------ wind / reflections / lighting

        static void CreateWindZone()
        {
            var go = new GameObject("WindZone");
            go.transform.rotation = Quaternion.Euler(0f, 250f, 0f);
            var wind = go.AddComponent<WindZone>();
            wind.mode = WindZoneMode.Directional;
            wind.windMain = 0.35f;
            wind.windTurbulence = 0.2f;
            wind.windPulseMagnitude = 0.3f;
            wind.windPulseFrequency = 0.15f;
        }

        static void CreateReflectionProbe()
        {
            var go = new GameObject("MonumentReflectionProbe");
            go.transform.position = new Vector3(0f, 12f, 0f);
            var probe = go.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Baked;
            probe.size = Vector3.one * 600f;
            probe.boxProjection = false;
            var hd = go.AddComponent<HDAdditionalReflectionData>();
            hd.mode = ProbeSettings.Mode.Baked;
            hd.influenceVolume.shape = InfluenceShape.Box;
            hd.influenceVolume.boxSize = Vector3.one * 600f;
        }

        static void ConfigureLighting(Light sun)
        {
            RenderSettings.fog = false;          // HDRP ignores legacy fog; volume Fog is used
            RenderSettings.sun = sun;

            MeshUtil.EnsureFolder(SettingsFolder);
            var ls = AssetDatabase.LoadAssetAtPath<LightingSettings>(LightingSettingsPath);
            if (ls == null)
            {
                ls = new LightingSettings { name = "AmirTemurLighting" };
                AssetDatabase.CreateAsset(ls, LightingSettingsPath);
            }
            ls.bakedGI = false;     // no lightmap bake: SSGI + dynamic sky ambient
            ls.realtimeGI = false;
            EditorUtility.SetDirty(ls);
            Lightmapping.lightingSettings = ls;
        }

        // ------------------------------------------------------------------ quality / HDRP asset

        public static void ConfigureQualityAndPipeline()
        {
            var hf = AssetDatabase.LoadAssetAtPath<HDRenderPipelineAsset>(HighFidelityAssetPath);
            if (hf == null) { Debug.LogWarning($"[SceneSetup] {HighFidelityAssetPath} not found; quality left unchanged"); return; }

            int idx = Array.IndexOf(QualitySettings.names, HighFidelityQualityName);
            if (idx >= 0) QualitySettings.SetQualityLevel(idx, true);
            QualitySettings.renderPipeline = hf;
            GraphicsSettings.defaultRenderPipeline = hf;

            RenderPipelineSettings s = hf.currentPlatformRenderPipelineSettings;
            s.supportSSGI = true;
            s.supportSSR = true;
            s.supportSSRTransparent = true;
            s.supportSSAO = true;
            s.supportVolumetrics = true;
            s.supportVolumetricClouds = true;
            s.supportMotionVectors = true;
            s.supportRayTracing = false;
            s.msaaSampleCount = MSAASamples.None;                                    // TAA is used
            s.lightProbeSystem = RenderPipelineSettings.LightProbeSystem.LegacyLightProbes; // supportProbeVolume off

            HDShadowInitParameters sh = s.hdShadowInitParams;
            sh.supportScreenSpaceShadows = true;
            sh.maxScreenSpaceShadowSlots = 4;
            sh.maxShadowRequests = 256;
            sh.punctualLightShadowAtlas.shadowAtlasResolution = 8192;
            sh.areaLightShadowAtlas.shadowAtlasResolution = 4096;
            sh.cachedPunctualLightShadowAtlas = 4096;
            sh.maxDirectionalShadowMapResolution = 4096;
            sh.shadowResolutionDirectional = new IntScalableSetting(new[] { 512, 1024, 2048, 4096 }, ScalableSettingSchemaId.With4Levels);
            sh.directionalShadowFilteringQuality = HDShadowFilteringQuality.High;
            sh.punctualShadowFilteringQuality = HDShadowFilteringQuality.High;
            s.hdShadowInitParams = sh;

            RenderPipelineSettings.LightSettings ls = s.lightSettings;
            ls.useContactShadow = new BoolScalableSetting(new[] { true, true, true }, ScalableSettingSchemaId.With3Levels);
            s.lightSettings = ls;

            s.lodBias = new FloatScalableSetting(new[] { 2f, 2f, 2f }, ScalableSettingSchemaId.With3Levels);

            hf.currentPlatformRenderPipelineSettings = s;
            EditorUtility.SetDirty(hf);
            AssetDatabase.SaveAssets();
            Debug.Log("[SceneSetup] HDRP High Fidelity asset configured (SSGI, SSR, volumetrics, screen-space shadows, contact shadows, atlas 8192, LOD bias 2, MSAA off, no ray tracing, no APV); set as default pipeline");
        }
    }
}
