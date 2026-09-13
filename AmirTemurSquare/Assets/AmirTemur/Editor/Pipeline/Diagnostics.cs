using System.Text;
using UnityEditor;
using UnityEngine;

namespace AmirTemur.Editor
{
    public static class Diagnostics
    {
        /// <summary>Prints world bounds and hierarchy transforms of every source model and generated prefab.</summary>
        public static void ModelBounds()
        {
            var sb = new StringBuilder();
            foreach (var kv in PrefabLibrary.Sources)
            {
                string fbx = $"Assets/AmirTemur/Art/Models/{kv.Value}/{kv.Value}.fbx";
                var src = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
                sb.AppendLine($"[Diag] ===== {kv.Key} ({kv.Value}) fbx={(src != null)}");
                if (src != null)
                {
                    var inst = (GameObject)PrefabUtility.InstantiatePrefab(src);
                    Dump(inst.transform, sb, 0);
                    var rs = inst.GetComponentsInChildren<Renderer>();
                    if (rs.Length > 0)
                    {
                        var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                        sb.AppendLine($"[Diag]   FBX world bounds center={b.center} size={b.size}");
                    }
                    Object.DestroyImmediate(inst);
                }
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabLibrary.Folder}/{kv.Key}.prefab");
                if (prefab != null)
                {
                    var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    var rs = inst.GetComponentsInChildren<Renderer>();
                    if (rs.Length > 0)
                    {
                        var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                        sb.AppendLine($"[Diag]   PREFAB world bounds center={b.center} size={b.size} renderers={rs.Length}");
                    }
                    Dump(inst.transform, sb, 0);
                    Object.DestroyImmediate(inst);
                }
            }
            Debug.Log(sb.ToString());
        }

        static void Dump(Transform t, StringBuilder sb, int depth)
        {
            if (depth > 3) return;
            var mf = t.GetComponent<MeshFilter>();
            string mesh = mf != null && mf.sharedMesh != null ? $" mesh={mf.sharedMesh.name} verts={mf.sharedMesh.vertexCount} localBounds={mf.sharedMesh.bounds.size}" : "";
            var r = t.GetComponent<Renderer>();
            string wb = r != null ? $" worldBounds={r.bounds.size}" : "";
            sb.AppendLine($"[Diag]   {new string(' ', depth * 2)}{t.name} pos={t.localPosition} rot={t.localEulerAngles} scale={t.localScale} active={t.gameObject.activeSelf}{mesh}{wb}");
            for (int i = 0; i < t.childCount && i < 12; i++) Dump(t.GetChild(i), sb, depth + 1);
            if (t.childCount > 12) sb.AppendLine($"[Diag]   {new string(' ', depth * 2)}... {t.childCount - 12} more children");
        }
    }
}
