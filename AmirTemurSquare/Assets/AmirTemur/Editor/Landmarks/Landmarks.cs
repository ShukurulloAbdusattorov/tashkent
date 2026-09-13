using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AmirTemur.Editor
{
    /// <summary>Bespoke procedural models of the Amir Temur Square landmarks. Entry point: <see cref="Build"/>.
    /// Each landmark lives in its own file (AmirTemurMonument, HotelUzbekistan, ForumPalace, TimuridMuseum, TashkentChimes)
    /// and is wrapped in try/catch so a failure in one never aborts the rest.</summary>
    public static class Landmarks
    {
        public const string Folder = "Assets/AmirTemur/Generated/Landmarks";

        /// <summary>OSM ids (buildings + parts) that the landmark models replace. CityBuilder objects whose names contain one of these ids
        /// are deactivated so the generic extrusions do not clash with the bespoke geometry.</summary>
        public static readonly long[] ReplacedOsmIds =
        {
            44491343, 523861784, 1323494334,                 // Hotel Uzbekistan + parts
            44351799, 235797552,                             // Forum palace + dome part
            31972619, 523383977, 1348266535, 1348266536, 1348266537, 1348266538, 1348266539, 1348266540, 1348266541, 1348266542,
            1348266543, 1348266544, 1348266545, 1348266546, 1348266547, 1348266548, 1348266549, // Timurid museum + parts
            252339710, 252339711,                            // clock tower parts
        };

        public static void Build(Transform root)
        {
            RegisterMaterials();
            MeshUtil.EnsureFolder(Folder);
            var city = CityData.Load();

            var parentGo = new GameObject("Landmarks");
            if (root != null) parentGo.transform.SetParent(root, false);
            var parent = parentGo.transform;

            if (root != null) HideOsmCopies(root, ReplacedOsmIds);

            Run("AmirTemurMonument", () => AmirTemurMonument.Build(city, parent));
            Run("HotelUzbekistan", () => HotelUzbekistan.Build(city, parent));
            Run("ForumPalace", () => ForumPalace.Build(city, parent));
            Run("TimuridMuseum", () => TimuridMuseum.Build(city, parent));
            Run("TashkentChimes", () => TashkentChimes.Build(city, parent));

            AssetDatabase.SaveAssets();
            Debug.Log("[Landmarks] done");
        }

        static void Run(string name, Action build)
        {
            try
            {
                var t0 = DateTime.Now;
                build();
                Debug.Log($"[Landmarks] built {name} in {(DateTime.Now - t0).TotalSeconds:F1}s");
            }
            catch (Exception e)
            {
                Debug.LogError($"[Landmarks] {name} FAILED: {e}");
            }
        }

        /// <summary>Extra material keys used by the landmarks (registered before first use so MaterialLibrary.Get builds them).</summary>
        public static void RegisterMaterials()
        {
            var d = MaterialLibrary.Definitions;
            if (!d.ContainsKey("metal_gold"))
                d["metal_gold"] = new MaterialLibrary.Def { textureSet = "Metal009", tiling = 0.5f, tint = new Color(0.85f, 0.66f, 0.30f), smoothnessFallback = 0.75f, metallicFallback = 0.95f };
            if (!d.ContainsKey("glass_dark"))
                d["glass_dark"] = new MaterialLibrary.Def { textureSet = "", tiling = 1f, tint = new Color(0.07f, 0.10f, 0.13f), smoothnessFallback = 0.96f, metallicFallback = 0.35f };
            if (!d.ContainsKey("granite_brown"))
                d["granite_brown"] = new MaterialLibrary.Def { textureSet = "Granite005A", tiling = 0.5f, tint = new Color(0.42f, 0.24f, 0.18f), smoothnessFallback = 0.85f };
            if (!d.ContainsKey("bronze_statue"))
                d["bronze_statue"] = new MaterialLibrary.Def { textureSet = "Metal009", tiling = 1f, tint = new Color(0.30f, 0.22f, 0.13f), tintStrength = 0.9f, smoothnessFallback = 0.5f, metallicFallback = 0.9f };
            if (!d.ContainsKey("clock_face"))
                d["clock_face"] = new MaterialLibrary.Def { textureSet = "", tiling = 1f, tint = new Color(0.95f, 0.94f, 0.88f), smoothnessFallback = 0.5f, emissive = new Color(1f, 0.95f, 0.8f), emissiveIntensity = 1.5f };
        }

        static void HideOsmCopies(Transform root, IList<long> ids)
        {
            var names = new List<string>(ids.Count);
            foreach (var id in ids) names.Add(id.ToString());
            int n = 0;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == root || !t.gameObject.activeSelf) continue;
                foreach (var s in names)
                {
                    if (t.name.Contains(s)) { t.gameObject.SetActive(false); n++; break; }
                }
            }
            if (n > 0) Debug.Log($"[Landmarks] deactivated {n} CityBuilder object(s) that overlap landmark footprints");
        }

        /// <summary>Building footprint (cleaned, CCW) for a landmark key; falls back to the given ring when missing.</summary>
        public static List<Vector2> Footprint(CityData city, string landmarkKey, out CityData.Landmark lm)
        {
            lm = null;
            if (city.landmarks.TryGetValue(landmarkKey, out lm))
            {
                var b = city.FindBuilding(lm.id);
                if (b != null && b.outer.Count >= 3) return LK.CCW(b.outer);
            }
            return null;
        }
    }
}
