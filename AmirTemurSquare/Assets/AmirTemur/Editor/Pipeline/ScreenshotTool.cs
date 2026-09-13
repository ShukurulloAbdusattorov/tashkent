using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using Object = UnityEngine.Object;

namespace AmirTemur.Editor
{
    /// <summary>
    /// Renders the open scene with a temporary HDRP camera into a RenderTexture and writes PNGs.
    /// Works in -batchmode as long as -nographics is NOT used. Temporal effects (TAA, SSR/SSGI accumulation, clouds) need several
    /// frames, so each shot is rendered WarmupFrames times before read-back; exposure adaptation is forced to instant meanwhile.
    /// </summary>
    public static class ScreenshotTool
    {
        public const string DefaultOutDir = "D:/Tashkent city/screenshots";
        public const int WarmupFrames = 8;

        public static readonly (Vector3 pos, Vector3 lookAt, string name)[] DefaultShots =
        {
            (new Vector3(0f, 120f, -220f), new Vector3(0f, 0f, 0f), "aerial"),
            (new Vector3(-9f, 3f, -17f), new Vector3(0f, 7.5f, 0f), "monument_close"),
            (new Vector3(-24f, 2.2f, -50f), new Vector3(-20f, 1.2f, -45f), "player_view"),
            (new Vector3(120f, 25f, -140f), new Vector3(0f, 8f, 0f), "square_overview"),
            (new Vector3(-40f, 3f, -60f), new Vector3(249f, 30f, 23f), "hotel_view"),
            (new Vector3(0f, 3f, 120f), new Vector3(-63f, 15f, 277f), "museum"),
            (new Vector3(60f, 3f, -60f), new Vector3(246f, 25f, -181f), "forum"),
            (new Vector3(-120f, 1.8f, -40f), new Vector3(0f, 5f, 0f), "street_level"),
        };

        [MenuItem("AmirTemur/Screenshots")]
        public static void CaptureDefault() => Capture(DefaultOutDir, DefaultShots);

        public static void Capture(string outDir, IList<(Vector3 pos, Vector3 lookAt, string name)> shots, int w = 1920, int h = 1080)
        {
            Directory.CreateDirectory(outDir);

            var camGo = new GameObject("ScreenshotCamera") { hideFlags = HideFlags.DontSave };
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 55f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 4000f;
            cam.allowHDR = true;
            cam.allowMSAA = false;
            var hd = camGo.AddComponent<HDAdditionalCameraData>();
            hd.antialiasing = HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing;
            hd.clearColorMode = HDAdditionalCameraData.ClearColorMode.Sky;
            hd.dithering = true;

            // instant exposure adaptation while capturing (the scene profile uses progressive adaptation)
            var volGo = new GameObject("ScreenshotExposureVolume") { hideFlags = HideFlags.DontSave };
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 1000f;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var exposure = profile.Add<Exposure>(false);
            exposure.adaptationMode.overrideState = true;
            exposure.adaptationMode.value = AdaptationMode.Fixed;
            vol.sharedProfile = profile;

            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 1 };
            rt.Create();
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            int written = 0;
            try
            {
                cam.targetTexture = rt;
                foreach (var shot in shots)
                {
                    try
                    {
                        camGo.transform.position = shot.pos;
                        camGo.transform.LookAt(shot.lookAt);
                        for (int i = 0; i < WarmupFrames; i++) cam.Render();

                        RenderTexture.active = rt;
                        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                        tex.Apply(false);
                        RenderTexture.active = null;

                        string file = Path.Combine(outDir, shot.name + ".png");
                        File.WriteAllBytes(file, tex.EncodeToPNG());
                        written++;
                        Debug.Log($"[ScreenshotTool] wrote {file}");
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[ScreenshotTool] shot '{shot.name}' failed: {e}");
                    }
                }
            }
            finally
            {
                RenderTexture.active = null;
                cam.targetTexture = null;
                rt.Release();
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(tex);
                Object.DestroyImmediate(camGo);
                Object.DestroyImmediate(volGo);
                Object.DestroyImmediate(profile);
            }
            Debug.Log($"[ScreenshotTool] {written}/{shots.Count} screenshots written to {outDir}");
        }
    }
}
