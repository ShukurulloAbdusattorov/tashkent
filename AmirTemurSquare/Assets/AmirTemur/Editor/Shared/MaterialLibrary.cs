using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace AmirTemur.Editor
{
    /// <summary>Central material registry. Get(key) never returns null: if no textured material exists for the key a tinted HDRP/Lit fallback is created.
    /// The assets+scene agent extends <see cref="Definitions"/> with textured entries (see ARCHITECTURE.md for the key list).</summary>
    public static class MaterialLibrary
    {
        public const string Folder = "Assets/AmirTemur/Art/Materials";

        public class Def
        {
            public string textureSet;      // folder name under Assets/AmirTemur/Art/Textures/<set>/ (files <set>_BaseColor, _Normal, _Mask, optional _Height)
            public float tiling = 1f;      // texture repeats per metre (e.g. 0.25 = one repeat every 4 m)
            public Color tint = Color.white;
            public float smoothnessFallback = 0.4f, metallicFallback = 0f;
            public bool alphaClip;
            public Color emissive = Color.black; public float emissiveIntensity = 0f;
            public bool doubleSided;
            public float normalScale = 1f;
            public float tintStrength = 0.15f; // how strongly tint multiplies a textured base colour (1 = full tint)
        }

        /// <summary>Fallback colours / settings per key (used when a textured set is missing).</summary>
        public static readonly Dictionary<string, Def> Definitions = new()
        {
            ["asphalt"] = new Def { textureSet = "Asphalt012", tiling = 0.2f, tint = new Color(0.22f, 0.22f, 0.22f), smoothnessFallback = 0.35f },
            ["asphalt_worn"] = new Def { textureSet = "Asphalt012", tiling = 0.25f, tint = new Color(0.28f, 0.28f, 0.27f), smoothnessFallback = 0.3f },
            ["sidewalk_concrete"] = new Def { textureSet = "Concrete034", tiling = 0.5f, tint = new Color(0.62f, 0.6f, 0.57f), smoothnessFallback = 0.3f },
            ["paving_plaza"] = new Def { textureSet = "PavingStones131", tiling = 0.5f, tint = new Color(0.66f, 0.62f, 0.58f), smoothnessFallback = 0.45f },
            ["paving_granite"] = new Def { textureSet = "PavingStones138", tiling = 0.5f, tint = new Color(0.5f, 0.48f, 0.47f), smoothnessFallback = 0.5f },
            ["paving_small"] = new Def { textureSet = "PavingStones070", tiling = 1.0f, tint = new Color(0.55f, 0.52f, 0.5f), smoothnessFallback = 0.4f },
            ["marble_white"] = new Def { textureSet = "Marble016", tiling = 0.5f, tint = new Color(0.9f, 0.88f, 0.85f), smoothnessFallback = 0.75f },
            ["marble_dark"] = new Def { textureSet = "Marble012", tiling = 0.5f, tint = new Color(0.25f, 0.24f, 0.24f), smoothnessFallback = 0.8f },
            ["granite_red"] = new Def { textureSet = "Granite001A", tiling = 0.5f, tint = new Color(0.45f, 0.25f, 0.2f), smoothnessFallback = 0.7f },
            ["grass"] = new Def { textureSet = "Grass004", tiling = 0.5f, tint = new Color(0.3f, 0.45f, 0.18f), smoothnessFallback = 0.15f },
            ["grass_dry"] = new Def { textureSet = "Grass001", tiling = 0.5f, tint = new Color(0.45f, 0.48f, 0.22f), smoothnessFallback = 0.15f },
            ["soil"] = new Def { textureSet = "Ground037", tiling = 0.5f, tint = new Color(0.35f, 0.28f, 0.2f), smoothnessFallback = 0.1f },
            ["brick"] = new Def { textureSet = "Bricks075A", tiling = 0.5f, tint = new Color(0.6f, 0.35f, 0.28f), smoothnessFallback = 0.3f },
            ["facade_office"] = new Def { textureSet = "Facade001", tiling = 0.1f, tint = new Color(0.7f, 0.68f, 0.64f), smoothnessFallback = 0.4f },
            ["facade_glass"] = new Def { textureSet = "Facade018A", tiling = 0.1f, tint = new Color(0.45f, 0.55f, 0.62f), smoothnessFallback = 0.85f, metallicFallback = 0.3f },
            ["facade_soviet"] = new Def { textureSet = "Facade020A", tiling = 0.1f, tint = new Color(0.78f, 0.74f, 0.66f), smoothnessFallback = 0.3f },
            ["facade_classic"] = new Def { textureSet = "Facade006", tiling = 0.1f, tint = new Color(0.85f, 0.8f, 0.7f), smoothnessFallback = 0.3f },
            ["concrete_smooth"] = new Def { textureSet = "Concrete016", tiling = 0.5f, tint = new Color(0.7f, 0.69f, 0.66f), smoothnessFallback = 0.35f },
            ["concrete_rough"] = new Def { textureSet = "Concrete036", tiling = 0.5f, tint = new Color(0.6f, 0.59f, 0.56f), smoothnessFallback = 0.25f },
            ["concrete_panel"] = new Def { textureSet = "Concrete023", tiling = 0.25f, tint = new Color(0.72f, 0.7f, 0.66f), smoothnessFallback = 0.3f },
            ["plaster_white"] = new Def { textureSet = "Plaster001", tiling = 0.5f, tint = new Color(0.92f, 0.9f, 0.86f), smoothnessFallback = 0.3f },
            ["travertine"] = new Def { textureSet = "Travertine008", tiling = 0.5f, tint = new Color(0.85f, 0.8f, 0.7f), smoothnessFallback = 0.4f },
            ["metal_dark"] = new Def { textureSet = "Metal032", tiling = 1f, tint = new Color(0.15f, 0.15f, 0.16f), smoothnessFallback = 0.6f, metallicFallback = 0.9f },
            ["metal_painted"] = new Def { textureSet = "Metal034", tiling = 1f, tint = new Color(0.2f, 0.25f, 0.2f), smoothnessFallback = 0.55f, metallicFallback = 0.4f },
            ["metal_bronze"] = new Def { textureSet = "Metal009", tiling = 1f, tint = new Color(0.35f, 0.25f, 0.15f), smoothnessFallback = 0.55f, metallicFallback = 0.9f },
            ["roof_tiles"] = new Def { textureSet = "Tiles093", tiling = 0.5f, tint = new Color(0.45f, 0.3f, 0.25f), smoothnessFallback = 0.3f },
            ["roof_flat"] = new Def { textureSet = "Concrete036", tiling = 0.3f, tint = new Color(0.4f, 0.4f, 0.4f), smoothnessFallback = 0.2f },
            ["tiles_blue"] = new Def { textureSet = "Tiles074", tiling = 1f, tint = new Color(0.1f, 0.45f, 0.75f), tintStrength = 1f, smoothnessFallback = 0.8f },
            ["water"] = new Def { textureSet = "", tiling = 1f, tint = new Color(0.1f, 0.3f, 0.4f, 0.7f), smoothnessFallback = 0.98f },
            ["rock"] = new Def { textureSet = "Rock030", tiling = 0.5f, tint = new Color(0.45f, 0.43f, 0.4f), smoothnessFallback = 0.25f },
            ["wood"] = new Def { textureSet = "Wood051", tiling = 1f, tint = new Color(0.45f, 0.3f, 0.18f), smoothnessFallback = 0.45f },
            ["kerb"] = new Def { textureSet = "Concrete016", tiling = 1f, tint = new Color(0.68f, 0.67f, 0.64f), smoothnessFallback = 0.3f },
            ["road_marking_white"] = new Def { textureSet = "", tiling = 1f, tint = new Color(0.9f, 0.9f, 0.88f), smoothnessFallback = 0.3f },
            ["glass"] = new Def { textureSet = "", tiling = 1f, tint = new Color(0.6f, 0.7f, 0.75f, 0.35f), smoothnessFallback = 0.95f, metallicFallback = 0.1f },
            ["window_lit"] = new Def { textureSet = "", tiling = 1f, tint = new Color(0.3f, 0.3f, 0.3f), smoothnessFallback = 0.9f, emissive = new Color(1f, 0.85f, 0.6f), emissiveIntensity = 3f },
            ["fabric_red"] = new Def { textureSet = "Fabric030", tiling = 2f, tint = new Color(0.55f, 0.1f, 0.1f), smoothnessFallback = 0.2f },
            ["leather"] = new Def { textureSet = "Leather011", tiling = 2f, tint = new Color(0.3f, 0.2f, 0.12f), smoothnessFallback = 0.4f },
            ["metal_plates"] = new Def { textureSet = "MetalPlates006", tiling = 1f, tint = new Color(0.5f, 0.5f, 0.5f), smoothnessFallback = 0.6f, metallicFallback = 0.9f },
        };

        static readonly Dictionary<string, Material> _cache = new();

        public static Material Get(string key)
        {
            if (_cache.TryGetValue(key, out var m) && m != null) return m;
            MeshUtil.EnsureFolder(Folder);
            string path = $"{Folder}/{key}.mat";
            m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = Create(key);
                AssetDatabase.CreateAsset(m, path);
            }
            _cache[key] = m;
            return m;
        }

        /// <summary>Rebuilds every material asset from definitions and available textures (call after textures import).</summary>
        public static void RebuildAll()
        {
            MeshUtil.EnsureFolder(Folder);
            foreach (var kv in Definitions)
            {
                string path = $"{Folder}/{kv.Key}.mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null) { m = Create(kv.Key); AssetDatabase.CreateAsset(m, path); }
                else Configure(m, kv.Key, kv.Value);
                _cache[kv.Key] = m;
            }
            AssetDatabase.SaveAssets();
        }

        static Material Create(string key)
        {
            var shader = Shader.Find("HDRP/Lit");
            var m = new Material(shader) { name = key };
            Definitions.TryGetValue(key, out var def);
            Configure(m, key, def ?? new Def { tint = new Color(0.5f, 0.5f, 0.5f) });
            return m;
        }

        public static Texture2D FindTexture(string set, string suffix)
        {
            if (string.IsNullOrEmpty(set)) return null;
            foreach (var ext in new[] { "png", "jpg", "tga", "exr" })
            {
                var t = AssetDatabase.LoadAssetAtPath<Texture2D>($"Assets/AmirTemur/Art/Textures/{set}/{set}_{suffix}.{ext}");
                if (t != null) return t;
            }
            return null;
        }

        public static void Configure(Material m, string key, Def def)
        {
            var baseTex = FindTexture(def.textureSet, "BaseColor");
            var normal = FindTexture(def.textureSet, "Normal");
            var mask = FindTexture(def.textureSet, "Mask");
            var height = FindTexture(def.textureSet, "Height");
            m.SetColor("_BaseColor", baseTex != null ? Color.Lerp(Color.white, def.tint, def.tintStrength) : def.tint);
            m.SetTexture("_BaseColorMap", baseTex);
            m.SetTexture("_NormalMap", normal);
            m.SetFloat("_NormalScale", def.normalScale);
            m.SetTexture("_MaskMap", mask);
            if (mask == null) { m.SetFloat("_Smoothness", def.smoothnessFallback); m.SetFloat("_Metallic", def.metallicFallback); }
            else { m.SetFloat("_Smoothness", 1f); m.SetFloat("_Metallic", 1f); m.SetFloat("_SmoothnessRemapMin", 0f); m.SetFloat("_SmoothnessRemapMax", 1f); }
            m.SetTextureScale("_BaseColorMap", new Vector2(def.tiling, def.tiling));
            if (height != null) { m.SetTexture("_HeightMap", height); }
            if (def.alphaClip) { m.SetFloat("_AlphaCutoffEnable", 1f); m.SetFloat("_AlphaCutoff", 0.5f); }
            if (def.doubleSided) { m.SetFloat("_DoubleSidedEnable", 1f); }
            if (def.emissiveIntensity > 0f)
            {
                m.SetFloat("_UseEmissiveIntensity", 1f);
                m.SetColor("_EmissiveColor", def.emissive * def.emissiveIntensity);
                m.SetColor("_EmissiveColorLDR", def.emissive);
                m.SetFloat("_EmissiveIntensity", def.emissiveIntensity);
                m.SetFloat("_EmissiveExposureWeight", 0.6f);
            }
            if (def.tint.a < 0.999f)
            {
                // transparent surface
                m.SetFloat("_SurfaceType", 1f); m.SetFloat("_BlendMode", 0f); m.SetFloat("_EnableBlendModePreserveSpecularLighting", 1f);
                m.SetFloat("_ZWrite", 0f); m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
            HDMaterial.ValidateMaterial(m);
            EditorUtility.SetDirty(m);
        }
    }
}
