using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AmirTemur.Editor
{
    /// <summary>Prop prefab registry. Prefabs are produced by AssetImportSetup under Assets/AmirTemur/Art/Prefabs/<key>.prefab.
    /// Get() returns null when a prefab is missing; callers must handle null (e.g. fall back to procedural geometry).</summary>
    public static class PrefabLibrary
    {
        public const string Folder = "Assets/AmirTemur/Art/Prefabs";

        /// <summary>key -> Poly Haven model folder name (Assets/AmirTemur/Art/Models/<model>/<model>.fbx)</summary>
        public static readonly Dictionary<string, string> Sources = new()
        {
            ["tree_jacaranda"] = "jacaranda_tree",
            ["tree_small"] = "tree_small_02",
            ["tree_island"] = "island_tree_02",
            ["shrub_02"] = "shrub_02",
            ["shrub_03"] = "shrub_03",
            ["shrub_04"] = "shrub_04",
            ["fir_sapling"] = "fir_sapling_medium",
            ["lamp_01"] = "street_lamp_01",
            ["lamp_02"] = "street_lamp_02",
            ["bench_wood"] = "painted_wooden_bench",
            ["bench_modern"] = "modular_street_seating",
            ["trash_can"] = "metal_trash_can",
            ["hydrant"] = "fire_hydrant",
            ["manhole"] = "water_manhole_cover",
            ["horse_statue"] = "horse_statue_01",
            ["planter"] = "planter_box_01",
            ["car_covered"] = "covered_car",
        };

        static readonly Dictionary<string, GameObject> _cache = new();

        public static GameObject Get(string key)
        {
            if (_cache.TryGetValue(key, out var p) && p != null) return p;
            p = AssetDatabase.LoadAssetAtPath<GameObject>($"{Folder}/{key}.prefab");
            if (p == null && Sources.TryGetValue(key, out var model))
                p = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/AmirTemur/Art/Models/{model}/{model}.fbx");
            if (p != null) _cache[key] = p;
            return p;
        }

        /// <summary>Instantiates a prefab (linked instance) at position/yaw/scale under parent; returns null if missing.</summary>
        public static GameObject Place(string key, Vector3 pos, float yawDeg, float scale, Transform parent, bool isStatic = true)
        {
            var prefab = Get(key); if (prefab == null) return null;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yawDeg, 0));
            go.transform.localScale = Vector3.one * scale;
            if (isStatic) GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic | StaticEditorFlags.ContributeGI);
            return go;
        }
    }
}
