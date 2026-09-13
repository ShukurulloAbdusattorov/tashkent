using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace AmirTemur.Editor
{
    /// <summary>Shared procedural-mesh helpers. All units are metres; x = east, z = north, y = up.</summary>
    public static class MeshUtil
    {
        // ------------------------------------------------------------------ polygons
        public static float SignedArea(IList<Vector2> ring)
        {
            float a = 0;
            for (int i = 0; i < ring.Count; i++)
            {
                var p = ring[i]; var q = ring[(i + 1) % ring.Count];
                a += p.x * q.y - q.x * p.y;
            }
            return a * 0.5f;
        }

        public static Vector2 Centroid(IList<Vector2> ring)
        {
            var c = Vector2.zero; foreach (var p in ring) c += p; return c / Mathf.Max(1, ring.Count);
        }

        /// <summary>Removes consecutive duplicates and a closing duplicate of the first vertex.</summary>
        public static List<Vector2> Clean(IList<Vector2> ring, float eps = 0.01f)
        {
            var r = new List<Vector2>();
            foreach (var p in ring)
                if (r.Count == 0 || (r[r.Count - 1] - p).sqrMagnitude > eps * eps) r.Add(p);
            if (r.Count > 1 && (r[0] - r[r.Count - 1]).sqrMagnitude <= eps * eps) r.RemoveAt(r.Count - 1);
            return r;
        }

        /// <summary>Ear-clipping triangulation with holes (holes are bridged into the outer ring). Outer must be CCW, holes CW.
        /// Returns indices into the concatenated vertex list outer + holes (in order).</summary>
        public static List<int> Triangulate(List<Vector2> outer, List<List<Vector2>> holes, out List<Vector2> vertices)
        {
            outer = Clean(outer);
            if (SignedArea(outer) < 0) outer.Reverse();
            var all = new List<Vector2>(outer);
            var poly = new List<int>();
            for (int i = 0; i < outer.Count; i++) poly.Add(i);

            if (holes != null)
            {
                var hs = new List<List<Vector2>>();
                foreach (var h in holes)
                {
                    var hc = Clean(h);
                    if (hc.Count < 3) continue;
                    if (SignedArea(hc) > 0) hc.Reverse();
                    hs.Add(hc);
                }
                // sort holes by max x so bridging is robust
                hs.Sort((a, b) => MaxX(b).CompareTo(MaxX(a)));
                foreach (var h in hs)
                {
                    int baseIdx = all.Count;
                    all.AddRange(h);
                    // find hole vertex with max x
                    int hi = 0; for (int i = 1; i < h.Count; i++) if (h[i].x > h[hi].x) hi = i;
                    var hp = h[hi];
                    // find closest visible outer-poly vertex to the right
                    int best = -1; float bestD = float.MaxValue;
                    for (int i = 0; i < poly.Count; i++)
                    {
                        var pv = all[poly[i]];
                        if (pv.x < hp.x) continue;
                        float d = (pv - hp).sqrMagnitude;
                        if (d < bestD && !SegmentIntersectsPoly(hp, pv, all, poly)) { bestD = d; best = i; }
                    }
                    if (best < 0)
                    {
                        for (int i = 0; i < poly.Count; i++) { float d = (all[poly[i]] - hp).sqrMagnitude; if (d < bestD) { bestD = d; best = i; } }
                    }
                    // splice: outer[best] -> hole[hi..] around -> hole[hi] -> outer[best]
                    var newPoly = new List<int>();
                    for (int i = 0; i <= best; i++) newPoly.Add(poly[i]);
                    for (int k = 0; k <= h.Count; k++) newPoly.Add(baseIdx + (hi + k) % h.Count);
                    for (int i = best; i < poly.Count; i++) newPoly.Add(poly[i]);
                    poly = newPoly;
                }
            }

            vertices = all;
            var tris = new List<int>();
            var idx = new List<int>(poly);
            int guard = 0;
            while (idx.Count > 3 && guard++ < 100000)
            {
                bool clipped = false;
                for (int i = 0; i < idx.Count; i++)
                {
                    int i0 = idx[(i + idx.Count - 1) % idx.Count], i1 = idx[i], i2 = idx[(i + 1) % idx.Count];
                    var a = all[i0]; var b = all[i1]; var c = all[i2];
                    if (Cross(b - a, c - a) <= 1e-7f) continue; // reflex or degenerate
                    bool ok = true;
                    for (int j = 0; j < idx.Count && ok; j++)
                    {
                        int ij = idx[j];
                        if (ij == i0 || ij == i1 || ij == i2) continue;
                        var p = all[ij];
                        if ((p == a) || (p == b) || (p == c)) continue;
                        if (PointInTri(p, a, b, c)) ok = false;
                    }
                    if (!ok) continue;
                    tris.Add(i0); tris.Add(i1); tris.Add(i2);
                    idx.RemoveAt(i); clipped = true; break;
                }
                if (!clipped)
                {
                    // fallback: remove the vertex with smallest |cross| to avoid infinite loop
                    int worst = 0; float wv = float.MaxValue;
                    for (int i = 0; i < idx.Count; i++)
                    {
                        var a = all[idx[(i + idx.Count - 1) % idx.Count]]; var b = all[idx[i]]; var c = all[idx[(i + 1) % idx.Count]];
                        float v = Mathf.Abs(Cross(b - a, c - a)); if (v < wv) { wv = v; worst = i; }
                    }
                    int p0 = idx[(worst + idx.Count - 1) % idx.Count], p1 = idx[worst], p2 = idx[(worst + 1) % idx.Count];
                    if (Cross(all[p1] - all[p0], all[p2] - all[p0]) > 0) { tris.Add(p0); tris.Add(p1); tris.Add(p2); }
                    idx.RemoveAt(worst);
                }
            }
            if (idx.Count == 3) { tris.Add(idx[0]); tris.Add(idx[1]); tris.Add(idx[2]); }
            return tris;
        }

        static float MaxX(List<Vector2> r) { float m = float.MinValue; foreach (var p in r) m = Mathf.Max(m, p.x); return m; }
        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
        static bool PointInTri(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float c1 = Cross(b - a, p - a), c2 = Cross(c - b, p - b), c3 = Cross(a - c, p - c);
            return c1 > 1e-6f && c2 > 1e-6f && c3 > 1e-6f;
        }
        static bool SegmentIntersectsPoly(Vector2 a, Vector2 b, List<Vector2> all, List<int> poly)
        {
            for (int i = 0; i < poly.Count; i++)
            {
                var p = all[poly[i]]; var q = all[poly[(i + 1) % poly.Count]];
                if (p == b || q == b) continue;
                if (SegmentsIntersect(a, b, p, q)) return true;
            }
            return false;
        }
        public static bool SegmentsIntersect(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4)
        {
            float d1 = Cross(p4 - p3, p1 - p3), d2 = Cross(p4 - p3, p2 - p3), d3 = Cross(p2 - p1, p3 - p1), d4 = Cross(p2 - p1, p4 - p1);
            return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
        }
        public static bool PointInPolygon(Vector2 p, IList<Vector2> ring)
        {
            bool inside = false;
            for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
            {
                var a = ring[i]; var b = ring[j];
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
            }
            return inside;
        }

        /// <summary>Offsets a closed ring outward (positive) or inward (negative) by d metres using miter joins.</summary>
        public static List<Vector2> OffsetRing(List<Vector2> ring, float d)
        {
            ring = Clean(ring); var res = new List<Vector2>(ring.Count);
            bool ccw = SignedArea(ring) > 0;
            int n = ring.Count;
            for (int i = 0; i < n; i++)
            {
                var p0 = ring[(i + n - 1) % n]; var p1 = ring[i]; var p2 = ring[(i + 1) % n];
                var d1 = (p1 - p0).normalized; var d2 = (p2 - p1).normalized;
                var n1 = ccw ? new Vector2(d1.y, -d1.x) : new Vector2(-d1.y, d1.x);
                var n2 = ccw ? new Vector2(d2.y, -d2.x) : new Vector2(-d2.y, d2.x);
                var bis = (n1 + n2); float len = bis.magnitude;
                if (len < 1e-4f) { res.Add(p1 + n1 * d); continue; }
                bis /= len; float cosHalf = Vector2.Dot(bis, n1); float m = d / Mathf.Max(0.35f, cosHalf);
                res.Add(p1 + bis * m);
            }
            return res;
        }

        // ------------------------------------------------------------------ mesh building
        public class Builder
        {
            public List<Vector3> V = new List<Vector3>(); public List<Vector3> N = new List<Vector3>(); public List<Vector2> UV = new List<Vector2>();
            public List<Color> C = new List<Color>();
            public Dictionary<int, List<int>> Sub = new Dictionary<int, List<int>>();
            public int VertexCount => V.Count;
            public List<int> Tris(int sub) { if (!Sub.TryGetValue(sub, out var l)) { l = new List<int>(); Sub[sub] = l; } return l; }
            public int Add(Vector3 v, Vector3 n, Vector2 uv) { V.Add(v); N.Add(n); UV.Add(uv); return V.Count - 1; }
            public void Tri(int sub, int a, int b, int c) { var t = Tris(sub); t.Add(a); t.Add(b); t.Add(c); }
            public void Quad(int sub, int a, int b, int c, int d) { Tri(sub, a, b, c); Tri(sub, a, c, d); }
            /// <summary>Adds a quad from 4 corners (CCW seen from the front / normal side), with planar UVs scaled by uvScale.</summary>
            public void AddQuad(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 uva, Vector2 uvb, Vector2 uvc, Vector2 uvd)
            {
                var n = Vector3.Cross(b - a, c - a).normalized;
                int i0 = Add(a, n, uva), i1 = Add(b, n, uvb), i2 = Add(c, n, uvc), i3 = Add(d, n, uvd);
                Quad(sub, i0, i1, i2, i3);
            }
            /// <summary>Axis-aligned box centred at c with size s. UVs in metres.</summary>
            public void AddBox(int sub, Vector3 c, Vector3 s, float uvScale = 1f)
            {
                var h = s * 0.5f;
                Vector3 p000 = c + new Vector3(-h.x, -h.y, -h.z), p100 = c + new Vector3(h.x, -h.y, -h.z), p010 = c + new Vector3(-h.x, h.y, -h.z), p110 = c + new Vector3(h.x, h.y, -h.z);
                Vector3 p001 = c + new Vector3(-h.x, -h.y, h.z), p101 = c + new Vector3(h.x, -h.y, h.z), p011 = c + new Vector3(-h.x, h.y, h.z), p111 = c + new Vector3(h.x, h.y, h.z);
                // -z (front, seen from -z): p100,p000,p010,p110
                AddQuad(sub, p100, p000, p010, p110, U(s.x, 0, uvScale), U(0, 0, uvScale), U(0, s.y, uvScale), U(s.x, s.y, uvScale));
                // +z
                AddQuad(sub, p001, p101, p111, p011, U(0, 0, uvScale), U(s.x, 0, uvScale), U(s.x, s.y, uvScale), U(0, s.y, uvScale));
                // -x
                AddQuad(sub, p000, p001, p011, p010, U(0, 0, uvScale), U(s.z, 0, uvScale), U(s.z, s.y, uvScale), U(0, s.y, uvScale));
                // +x
                AddQuad(sub, p101, p100, p110, p111, U(0, 0, uvScale), U(s.z, 0, uvScale), U(s.z, s.y, uvScale), U(0, s.y, uvScale));
                // +y
                AddQuad(sub, p010, p011, p111, p110, U(0, 0, uvScale), U(0, s.z, uvScale), U(s.x, s.z, uvScale), U(s.x, 0, uvScale));
                // -y
                AddQuad(sub, p001, p000, p100, p101, U(0, s.z, uvScale), U(0, 0, uvScale), U(s.x, 0, uvScale), U(s.x, s.z, uvScale));
            }
            static Vector2 U(float a, float b, float s) => new Vector2(a * s, b * s);

            /// <summary>Adds a cylinder (y axis) with optional caps. UVs: u around circumference in metres, v height in metres.</summary>
            public void AddCylinder(int sub, Vector3 baseCentre, float radiusBottom, float radiusTop, float height, int segments = 24, bool capTop = true, bool capBottom = false)
            {
                float circ = 2 * Mathf.PI * Mathf.Max(radiusBottom, radiusTop);
                int start = V.Count;
                for (int i = 0; i <= segments; i++)
                {
                    float a = i / (float)segments * Mathf.PI * 2; float cx = Mathf.Cos(a), sz = Mathf.Sin(a);
                    var nrm = new Vector3(cx, (radiusBottom - radiusTop) / Mathf.Max(0.001f, height), sz).normalized;
                    Add(baseCentre + new Vector3(cx * radiusBottom, 0, sz * radiusBottom), nrm, new Vector2(circ * i / segments, 0));
                    Add(baseCentre + new Vector3(cx * radiusTop, height, sz * radiusTop), nrm, new Vector2(circ * i / segments, height));
                }
                for (int i = 0; i < segments; i++)
                {
                    int b0 = start + i * 2, t0 = b0 + 1, b1 = b0 + 2, t1 = b0 + 3;
                    Tri(sub, b0, t0, t1); Tri(sub, b0, t1, b1);
                }
                if (capTop) AddDisc(sub, baseCentre + Vector3.up * height, radiusTop, segments, true);
                if (capBottom) AddDisc(sub, baseCentre, radiusBottom, segments, false);
            }
            public void AddDisc(int sub, Vector3 centre, float radius, int segments = 24, bool up = true)
            {
                var n = up ? Vector3.up : Vector3.down;
                int c = Add(centre, n, new Vector2(centre.x, centre.z));
                int start = V.Count;
                for (int i = 0; i <= segments; i++)
                {
                    float a = i / (float)segments * Mathf.PI * 2; var p = centre + new Vector3(Mathf.Cos(a) * radius, 0, Mathf.Sin(a) * radius);
                    Add(p, n, new Vector2(p.x, p.z));
                }
                for (int i = 0; i < segments; i++)
                {
                    if (up) Tri(sub, c, start + i + 1, start + i); else Tri(sub, c, start + i, start + i + 1);
                }
            }

            /// <summary>Flat polygon (with holes) at height y, facing up. UVs = world xz * uvScale.</summary>
            public void AddPolygon(int sub, List<Vector2> outer, List<List<Vector2>> holes, float y, float uvScale = 1f, bool flip = false)
            {
                var tris = Triangulate(outer, holes, out var verts);
                int start = V.Count;
                var n = flip ? Vector3.down : Vector3.up;
                foreach (var p in verts) Add(new Vector3(p.x, y, p.y), n, new Vector2(p.x * uvScale, p.y * uvScale));
                // Triangulate returns CCW in x/z (map view). In Unity (left-handed, y up) a CCW-in-xz triangle faces DOWN, so swap winding for up-facing.
                for (int i = 0; i < tris.Count; i += 3)
                {
                    if (flip) { Tri(sub, start + tris[i], start + tris[i + 1], start + tris[i + 2]); }
                    else { Tri(sub, start + tris[i], start + tris[i + 2], start + tris[i + 1]); }
                }
            }

            /// <summary>Vertical walls along a ring from y0 to y1 with outward normals (ring CCW in map view = outward on the left-handed system handled here).
            /// UVs: u = distance along wall in metres, v = height in metres.</summary>
            public void AddWalls(int sub, List<Vector2> ring, float y0, float y1, float uvScale = 1f, float uOffset = 0f)
            {
                ring = Clean(ring);
                bool ccw = SignedArea(ring) > 0;
                float u = uOffset;
                int n = ring.Count;
                for (int i = 0; i < n; i++)
                {
                    var a = ring[i]; var b = ring[(i + 1) % n];
                    float len = (b - a).magnitude; if (len < 1e-4f) continue;
                    var dir = (b - a) / len;
                    // outward normal for a CCW (map view) ring is to the right of the direction of travel in xz: (dir.y, -dir.x)
                    var nrm2 = ccw ? new Vector2(dir.y, -dir.x) : new Vector2(-dir.y, dir.x);
                    var nrm = new Vector3(nrm2.x, 0, nrm2.y);
                    var A0 = new Vector3(a.x, y0, a.y); var B0 = new Vector3(b.x, y0, b.y); var A1 = new Vector3(a.x, y1, a.y); var B1 = new Vector3(b.x, y1, b.y);
                    int i0 = Add(A0, nrm, new Vector2(u * uvScale, y0 * uvScale)), i1 = Add(B0, nrm, new Vector2((u + len) * uvScale, y0 * uvScale));
                    int i2 = Add(B1, nrm, new Vector2((u + len) * uvScale, y1 * uvScale)), i3 = Add(A1, nrm, new Vector2(u * uvScale, y1 * uvScale));
                    // choose winding so the face normal points along nrm
                    var faceN = Vector3.Cross(B0 - A0, A1 - A0);
                    if (Vector3.Dot(faceN, nrm) > 0) { Tri(sub, i0, i1, i2); Tri(sub, i0, i2, i3); }
                    else { Tri(sub, i0, i2, i1); Tri(sub, i0, i3, i2); }
                    u += len;
                }
            }

            /// <summary>Ribbon along a centreline (y taken from points) of given width, UV u across (0..1 * uScale), v along in metres.</summary>
            public void AddRibbon(int sub, List<Vector3> line, float width, float uScale = 1f, float vScale = 1f, float yOffset = 0f)
            {
                if (line.Count < 2) return;
                int start = V.Count; float dist = 0; float hw = width * 0.5f;
                for (int i = 0; i < line.Count; i++)
                {
                    Vector3 dir;
                    if (i == 0) dir = line[1] - line[0];
                    else if (i == line.Count - 1) dir = line[i] - line[i - 1];
                    else dir = (line[i + 1] - line[i]).normalized + (line[i] - line[i - 1]).normalized;
                    dir.y = 0; dir = dir.sqrMagnitude < 1e-6f ? Vector3.forward : dir.normalized;
                    var side = new Vector3(dir.z, 0, -dir.x); // right side
                    float m = 1f;
                    if (i > 0 && i < line.Count - 1)
                    {
                        var d0 = (line[i] - line[i - 1]); d0.y = 0; d0.Normalize();
                        float cosA = Vector3.Dot(d0, dir); m = 1f / Mathf.Max(0.5f, cosA);
                    }
                    if (i > 0) dist += Vector3.Distance(line[i], line[i - 1]);
                    var p = line[i] + Vector3.up * yOffset;
                    Add(p - side * hw * m, Vector3.up, new Vector2(0, dist * vScale));
                    Add(p + side * hw * m, Vector3.up, new Vector2(uScale, dist * vScale));
                }
                for (int i = 0; i < line.Count - 1; i++)
                {
                    int l0 = start + i * 2, r0 = l0 + 1, l1 = l0 + 2, r1 = l0 + 3;
                    // up-facing winding in left-handed system: (l0, l1, r0) & (r0, l1, r1)
                    Tri(sub, l0, l1, r0); Tri(sub, r0, l1, r1);
                }
            }

            public Mesh ToMesh(string name = "mesh", bool recalcNormals = false)
            {
                var m = new Mesh { name = name };
                m.indexFormat = V.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
                m.SetVertices(V); m.SetNormals(N); m.SetUVs(0, UV);
                if (C.Count == V.Count) m.SetColors(C);
                var keys = new List<int>(Sub.Keys); keys.Sort();
                m.subMeshCount = keys.Count == 0 ? 1 : keys[keys.Count - 1] + 1;
                for (int s = 0; s < m.subMeshCount; s++) m.SetTriangles(Sub.TryGetValue(s, out var t) ? t : new List<int>(), s);
                if (recalcNormals) m.RecalculateNormals();
                m.RecalculateBounds(); m.RecalculateTangents();
                return m;
            }
        }

        // ------------------------------------------------------------------ assets
        public static string EnsureFolder(string assetFolder)
        {
            // assetFolder like "Assets/AmirTemur/Generated/City"
            var parts = assetFolder.Split('/'); string cur = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
            return cur;
        }

        public static Mesh SaveMeshAsset(Mesh mesh, string assetPath)
        {
            EnsureFolder(Path.GetDirectoryName(assetPath).Replace('\\', '/'));
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
            if (existing != null) { AssetDatabase.DeleteAsset(assetPath); }
            AssetDatabase.CreateAsset(mesh, assetPath);
            return mesh;
        }

        /// <summary>Creates a static GameObject with MeshFilter/MeshRenderer (and MeshCollider when collide) using given materials per submesh.</summary>
        public static GameObject MakeStatic(string name, Mesh mesh, Material[] mats, Transform parent, bool collide = true, bool contributeGI = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var mf = go.AddComponent<MeshFilter>(); mf.sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterials = mats;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            if (collide) { var mc = go.AddComponent<MeshCollider>(); mc.sharedMesh = mesh; }
            var flags = StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic | StaticEditorFlags.NavigationStatic;
            if (contributeGI) flags |= StaticEditorFlags.ContributeGI | StaticEditorFlags.OccluderStatic;
            GameObjectUtility.SetStaticEditorFlags(go, flags);
            return go;
        }

        public static List<Vector2> ToV2(IList<float[]> pts)
        {
            var l = new List<Vector2>(pts.Count); foreach (var p in pts) l.Add(new Vector2(p[0], p[1])); return l;
        }
        public static List<Vector3> ToV3(IList<float[]> pts, float y = 0)
        {
            var l = new List<Vector3>(pts.Count); foreach (var p in pts) l.Add(new Vector3(p[0], y, p[1])); return l;
        }
        public static Vector3 XZ(Vector2 p, float y = 0) => new Vector3(p.x, y, p.y);
    }
}
