using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AmirTemur.Editor
{
    /// <summary>Local horizontal frame: P(x,y,z) = o + u*x + up*y + v*z. u and v are unit horizontal vectors.
    /// All landmark geometry is authored in frames so rotated footprints (from OSM) are handled without quaternions.</summary>
    public struct Frame
    {
        public Vector3 o, u, v;
        public Frame(Vector3 origin, Vector3 along, Vector3 across) { o = origin; u = Flat(along); v = Flat(across); }
        static Vector3 Flat(Vector3 d) { d.y = 0; return d.sqrMagnitude < 1e-8f ? Vector3.right : d.normalized; }

        /// <summary>u = direction at angDeg (CCW from +x east towards +z north, map view), v = u rotated +90 degrees CCW.</summary>
        public static Frame Along(Vector3 origin, float angDeg)
        {
            float a = angDeg * Mathf.Deg2Rad; var u = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            return new Frame(origin, u, new Vector3(-u.z, 0, u.x));
        }
        /// <summary>Facade frame along ring edge a->b of a CCW (map view) ring: origin a, u along the edge, v = outward normal.</summary>
        public static Frame Facade(Vector2 a, Vector2 b, float y = 0)
        {
            var u = new Vector3(b.x - a.x, 0, b.y - a.y).normalized;
            return new Frame(new Vector3(a.x, y, a.y), u, new Vector3(u.z, 0, -u.x));
        }
        public Vector3 P(float x, float y, float z) => o + u * x + Vector3.up * y + v * z;
        public Vector2 XZ(float x, float z) { var p = P(x, 0, z); return new Vector2(p.x, p.z); }
        public Frame Shift(float x, float y, float z) => new Frame(P(x, y, z), u, v);
        /// <summary>Frame at P(x,y,z) whose u is this.v and whose v is -this.u.</summary>
        public Frame Rot90(float x, float y, float z) => new Frame(P(x, y, z), v, -u);
        /// <summary>Frame at P(x,y,z) with both axes reversed (looking back).</summary>
        public Frame Flip(float x, float y, float z) => new Frame(P(x, y, z), -u, -v);
    }

    /// <summary>Material-key -> submesh index registry for one mesh.</summary>
    public class MatSet
    {
        readonly List<string> _keys = new();
        public int Sub(string key) { int i = _keys.IndexOf(key); if (i < 0) { _keys.Add(key); i = _keys.Count - 1; } return i; }
        public int Count => _keys.Count;
        public Material[] Materials(int count)
        {
            var m = new Material[count];
            for (int i = 0; i < count; i++) m[i] = MaterialLibrary.Get(i < _keys.Count ? _keys[i] : "concrete_smooth");
            return m;
        }
    }

    /// <summary>A builder + material set that can be flushed to a saved mesh asset / static GameObject. Flush resets it so the
    /// same Part can be reused for the next chunk.</summary>
    public class Part
    {
        public MeshUtil.Builder B = new MeshUtil.Builder();
        public MatSet M = new MatSet();
        public int Sub(string key) => M.Sub(key);
        public int Verts => B.V.Count;

        public GameObject Flush(string name, Transform parent, bool collide = true)
        {
            if (B.V.Count == 0) return null;
            var mesh = B.ToMesh(name);
            MeshUtil.SaveMeshAsset(mesh, $"{Landmarks.Folder}/{name}.asset");
            var go = MeshUtil.MakeStatic(name, mesh, M.Materials(mesh.subMeshCount), parent, collide);
            B = new MeshUtil.Builder(); M = new MatSet();
            return go;
        }
        /// <summary>Flushes when the vertex count passed the soft limit (chunk counter is advanced).</summary>
        public void FlushIfLarge(string baseName, ref int chunk, Transform parent, int limit = 120000)
        {
            if (B.V.Count >= limit) { Flush($"{baseName}_{chunk}", parent); chunk++; }
        }
    }

    /// <summary>Geometry helpers on top of MeshUtil.Builder. Every primitive here fixes triangle winding from a desired normal,
    /// so callers only need to supply the outward direction. UVs are in metres.</summary>
    public static class LK
    {
        // ------------------------------------------------------------------ primitives
        public static void Tri(MeshUtil.Builder b, int sub, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 n, Vector2 u0, Vector2 u1, Vector2 u2)
        {
            var gn = Vector3.Cross(p1 - p0, p2 - p0);
            int i0 = b.Add(p0, n, u0), i1 = b.Add(p1, n, u1), i2 = b.Add(p2, n, u2);
            if (Vector3.Dot(gn, n) >= 0) b.Tri(sub, i0, i1, i2); else b.Tri(sub, i0, i2, i1);
        }

        /// <summary>Quad p0..p3 (consecutive around the perimeter) rendered with its face pointing along n.</summary>
        public static void QuadN(MeshUtil.Builder b, int sub, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 n, Vector2 u0, Vector2 u1, Vector2 u2, Vector2 u3)
        {
            var gn = Vector3.Cross(p1 - p0, p2 - p0);
            if (gn.sqrMagnitude < 1e-14f) gn = Vector3.Cross(p2 - p0, p3 - p0);
            int i0 = b.Add(p0, n, u0), i1 = b.Add(p1, n, u1), i2 = b.Add(p2, n, u2), i3 = b.Add(p3, n, u3);
            if (Vector3.Dot(gn, n) >= 0) { b.Tri(sub, i0, i1, i2); b.Tri(sub, i0, i2, i3); }
            else { b.Tri(sub, i0, i2, i1); b.Tri(sub, i0, i3, i2); }
        }

        /// <summary>Rectangle centred at c spanning w along e1 and h along e2 (unit vectors), facing n.</summary>
        public static void Face(MeshUtil.Builder b, int sub, Vector3 c, Vector3 e1, Vector3 e2, float w, float h, Vector3 n, float u0 = 0f, float v0 = 0f)
        {
            var a = c - e1 * (w * 0.5f) - e2 * (h * 0.5f);
            var p1 = c + e1 * (w * 0.5f) - e2 * (h * 0.5f);
            var p2 = c + e1 * (w * 0.5f) + e2 * (h * 0.5f);
            var p3 = c - e1 * (w * 0.5f) + e2 * (h * 0.5f);
            QuadN(b, sub, a, p1, p2, p3, n, new Vector2(u0, v0), new Vector2(u0 + w, v0), new Vector2(u0 + w, v0 + h), new Vector2(u0, v0 + h));
        }

        /// <summary>Box centred at c with orthonormal axes (ax, ay, az) and size s along them.</summary>
        public static void Box3(MeshUtil.Builder b, int sub, Vector3 c, Vector3 ax, Vector3 ay, Vector3 az, Vector3 s)
        {
            ax.Normalize(); ay.Normalize(); az.Normalize();
            Vector3 hx = ax * (s.x * 0.5f), hy = ay * (s.y * 0.5f), hz = az * (s.z * 0.5f);
            Face(b, sub, c + hx, az, ay, s.z, s.y, ax);
            Face(b, sub, c - hx, az, ay, s.z, s.y, -ax);
            Face(b, sub, c + hz, ax, ay, s.x, s.y, az);
            Face(b, sub, c - hz, ax, ay, s.x, s.y, -az);
            Face(b, sub, c + hy, ax, az, s.x, s.z, ay);
            Face(b, sub, c - hy, ax, az, s.x, s.z, -ay);
        }
        /// <summary>Upright box centred at c with horizontal axes ax (size.x) and az (size.z).</summary>
        public static void Box(MeshUtil.Builder b, int sub, Vector3 c, Vector3 ax, Vector3 az, Vector3 s) => Box3(b, sub, c, ax, Vector3.up, az, s);
        /// <summary>Upright box in frame f: local centre lc, size s (x along f.u, y up, z along f.v).</summary>
        public static void FBox(MeshUtil.Builder b, int sub, Frame f, Vector3 lc, Vector3 s) => Box3(b, sub, f.P(lc.x, lc.y, lc.z), f.u, Vector3.up, f.v, s);
        /// <summary>Upright box in frame f spanning local [min..max].</summary>
        public static void FBoxMinMax(MeshUtil.Builder b, int sub, Frame f, Vector3 min, Vector3 max) => FBox(b, sub, f, (min + max) * 0.5f, max - min);

        /// <summary>Square-section bar from a to c (any direction) with side thickness t (t2 for the second side).</summary>
        public static void Limb(MeshUtil.Builder b, int sub, Vector3 a, Vector3 c, float t, float t2 = -1f)
        {
            var axis = c - a; float len = axis.magnitude; if (len < 1e-4f) return; axis /= len;
            var helper = Mathf.Abs(Vector3.Dot(axis, Vector3.up)) > 0.9f ? Vector3.right : Vector3.up;
            var e1 = Vector3.Cross(axis, helper).normalized; var e2 = Vector3.Cross(axis, e1).normalized;
            Box3(b, sub, (a + c) * 0.5f, e1, axis, e2, new Vector3(t, len, t2 < 0 ? t : t2));
        }

        /// <summary>Single vertical wall quad from map point a to b2, facing n (horizontal).</summary>
        public static void WallSeg(MeshUtil.Builder b, int sub, Vector2 a, Vector2 b2, float y0, float y1, Vector3 n, float uOffset = 0f)
        {
            float len = (b2 - a).magnitude;
            QuadN(b, sub, new Vector3(a.x, y0, a.y), new Vector3(b2.x, y0, b2.y), new Vector3(b2.x, y1, b2.y), new Vector3(a.x, y1, a.y), n,
                new Vector2(uOffset, y0), new Vector2(uOffset + len, y0), new Vector2(uOffset + len, y1), new Vector2(uOffset, y1));
        }
        /// <summary>Vertical wall in frame f from local x0 to x1 at depth z, facing +v (or -v when facingOut is false).</summary>
        public static void FWall(MeshUtil.Builder b, int sub, Frame f, float x0, float x1, float y0, float y1, float z, bool facingOut = true)
        {
            QuadN(b, sub, f.P(x0, y0, z), f.P(x1, y0, z), f.P(x1, y1, z), f.P(x0, y1, z), facingOut ? f.v : -f.v,
                new Vector2(x0, y0), new Vector2(x1, y0), new Vector2(x1, y1), new Vector2(x0, y1));
        }
        /// <summary>Horizontal quad in frame f spanning x0..x1, z0..z1 at height y, facing up (or down).</summary>
        public static void FFloor(MeshUtil.Builder b, int sub, Frame f, float x0, float x1, float z0, float z1, float y, bool up = true)
        {
            QuadN(b, sub, f.P(x0, y, z0), f.P(x1, y, z0), f.P(x1, y, z1), f.P(x0, y, z1), up ? Vector3.up : Vector3.down,
                new Vector2(x0, z0), new Vector2(x1, z0), new Vector2(x1, z1), new Vector2(x0, z1));
        }

        /// <summary>Extrudes a closed 2D profile (x along f.u, y up) from depth z0 to z1 along f.v with outward side faces.</summary>
        public static void Prism(MeshUtil.Builder b, int sub, Frame f, List<Vector2> profile, float z0, float z1, bool front = true, bool back = true)
        {
            var prof = MeshUtil.Clean(profile);
            if (prof.Count < 3) return;
            if (MeshUtil.SignedArea(prof) < 0) prof.Reverse();
            var tris = MeshUtil.Triangulate(prof, null, out var verts);
            if (front)
                for (int i = 0; i < tris.Count; i += 3)
                {
                    Vector2 a = verts[tris[i]], c = verts[tris[i + 1]], d = verts[tris[i + 2]];
                    Tri(b, sub, f.P(a.x, a.y, z1), f.P(c.x, c.y, z1), f.P(d.x, d.y, z1), f.v, a, c, d);
                }
            if (back)
                for (int i = 0; i < tris.Count; i += 3)
                {
                    Vector2 a = verts[tris[i]], c = verts[tris[i + 1]], d = verts[tris[i + 2]];
                    Tri(b, sub, f.P(a.x, a.y, z0), f.P(c.x, c.y, z0), f.P(d.x, d.y, z0), -f.v, a, c, d);
                }
            int n = prof.Count; float u = 0;
            for (int i = 0; i < n; i++)
            {
                var p = prof[i]; var q = prof[(i + 1) % n]; var d = q - p; float len = d.magnitude; if (len < 1e-5f) continue; d /= len;
                var nrm2 = new Vector2(d.y, -d.x); // outward for a CCW profile (x right, y up)
                var nrm = (f.u * nrm2.x + Vector3.up * nrm2.y).normalized;
                QuadN(b, sub, f.P(p.x, p.y, z0), f.P(q.x, q.y, z0), f.P(q.x, q.y, z1), f.P(p.x, p.y, z1), nrm,
                    new Vector2(u, z0), new Vector2(u + len, z0), new Vector2(u + len, z1), new Vector2(u, z1));
                u += len;
            }
        }

        /// <summary>Rectangular bay profile [0..w]x[0..h] with arched openings cut from the bottom edge.
        /// Each opening: (x0, x1, springHeight). pointed = two-centred Islamic arch, else semicircular.</summary>
        public static List<Vector2> ArchBayProfile(float w, float h, List<Vector3> openings, bool pointed, int arcSeg = 14)
        {
            var prof = new List<Vector2> { new Vector2(0, 0) };
            openings.Sort((p, q) => p.x.CompareTo(q.x));
            foreach (var op in openings)
            {
                float xa = op.x, xb = op.y, ys = op.z; float r = (xb - xa) * 0.5f; float cm = (xa + xb) * 0.5f;
                prof.Add(new Vector2(xa, 0)); prof.Add(new Vector2(xa, ys));
                if (!pointed)
                {
                    for (int i = 1; i < arcSeg; i++) { float t = Mathf.PI - Mathf.PI * i / arcSeg; prof.Add(new Vector2(cm + r * Mathf.Cos(t), ys + r * Mathf.Sin(t))); }
                }
                else
                {
                    float R = r * 1.25f; float cl = xa + R, cr = xb - R;
                    float tEnd = Mathf.Acos(Mathf.Clamp((cm - cl) / R, -1f, 1f)); // parametric angle at the apex
                    int half = Mathf.Max(3, arcSeg / 2);
                    for (int i = 1; i <= half; i++) { float t = Mathf.PI - (Mathf.PI - tEnd) * i / half; prof.Add(new Vector2(cl + R * Mathf.Cos(t), ys + R * Mathf.Sin(t))); }
                    for (int i = half - 1; i >= 1; i--) { float t = Mathf.PI - (Mathf.PI - tEnd) * i / half; prof.Add(new Vector2(cr - R * Mathf.Cos(t), ys + R * Mathf.Sin(t))); }
                }
                prof.Add(new Vector2(xb, ys)); prof.Add(new Vector2(xb, 0));
            }
            prof.Add(new Vector2(w, 0)); prof.Add(new Vector2(w, h)); prof.Add(new Vector2(0, h));
            return prof;
        }

        // ------------------------------------------------------------------ surfaces of revolution
        /// <summary>Surface of revolution around 'axis' through c. profile = (radius, offsetAlongAxis) from bottom to top, smooth normals.</summary>
        public static void Lathe(MeshUtil.Builder b, int sub, Vector3 c, Vector3 axis, List<Vector2> profile, int seg = 32, bool capTop = false, bool capBottom = false)
        {
            axis.Normalize();
            var helper = Mathf.Abs(Vector3.Dot(axis, Vector3.up)) > 0.9f ? Vector3.right : Vector3.up;
            var e1 = Vector3.Cross(axis, helper).normalized; var e2 = Vector3.Cross(axis, e1).normalized;
            int np = profile.Count; if (np < 2) return;
            var pn = new Vector2[np]; float rmax = 0; var arc = new float[np];
            for (int j = 0; j < np; j++) rmax = Mathf.Max(rmax, profile[j].x);
            for (int j = 0; j < np; j++)
            {
                Vector2 acc = Vector2.zero;
                if (j > 0) { var d = profile[j] - profile[j - 1]; if (d.sqrMagnitude > 1e-10f) acc += new Vector2(d.y, -d.x).normalized; }
                if (j < np - 1) { var d = profile[j + 1] - profile[j]; if (d.sqrMagnitude > 1e-10f) acc += new Vector2(d.y, -d.x).normalized; }
                pn[j] = acc.sqrMagnitude < 1e-10f ? new Vector2(1, 0) : acc.normalized;
                arc[j] = j == 0 ? 0 : arc[j - 1] + (profile[j] - profile[j - 1]).magnitude;
            }
            int start = b.V.Count;
            for (int j = 0; j < np; j++)
                for (int i = 0; i <= seg; i++)
                {
                    float t = i / (float)seg * Mathf.PI * 2; var er = e1 * Mathf.Cos(t) + e2 * Mathf.Sin(t);
                    var p = c + er * profile[j].x + axis * profile[j].y;
                    var n = (er * pn[j].x + axis * pn[j].y).normalized;
                    b.Add(p, n, new Vector2(rmax * t, arc[j]));
                }
            for (int j = 0; j < np - 1; j++)
                for (int i = 0; i < seg; i++)
                {
                    int i00 = start + j * (seg + 1) + i, i01 = i00 + 1, i10 = i00 + seg + 1, i11 = i10 + 1;
                    var gn = Vector3.Cross(b.V[i01] - b.V[i00], b.V[i11] - b.V[i00]);
                    if (gn.sqrMagnitude < 1e-12f) gn = Vector3.Cross(b.V[i11] - b.V[i00], b.V[i10] - b.V[i00]);
                    bool ok = Vector3.Dot(gn, b.N[i00] + b.N[i11]) >= 0;
                    if (ok) { b.Tri(sub, i00, i01, i11); b.Tri(sub, i00, i11, i10); }
                    else { b.Tri(sub, i00, i11, i01); b.Tri(sub, i00, i10, i11); }
                }
            if (capTop && profile[np - 1].x > 1e-4f) DiscN(b, sub, c + axis * profile[np - 1].y, axis, profile[np - 1].x, seg);
            if (capBottom && profile[0].x > 1e-4f) DiscN(b, sub, c + axis * profile[0].y, -axis, profile[0].x, seg);
        }

        /// <summary>Dome profile point for t in [0,1] (0 = base, 1 = apex): ellipsoid r,h with optional bulge (onion / Timurid).</summary>
        public static Vector2 DomeProfile(float t, float r, float h, float bulge)
        {
            float a = t * Mathf.PI * 0.5f;
            float rr = r * (Mathf.Cos(a) + bulge * Mathf.Sin(2 * a));
            return new Vector2(Mathf.Max(0, rr), h * Mathf.Sin(a));
        }
        public static void Dome(MeshUtil.Builder b, int sub, Vector3 baseCentre, float r, float h, int seg = 64, int lat = 18, float bulge = 0f)
        {
            var prof = new List<Vector2>();
            for (int j = 0; j <= lat; j++) prof.Add(DomeProfile(j / (float)lat, r, h, bulge));
            Lathe(b, sub, baseCentre, Vector3.up, prof, seg);
        }
        /// <summary>Raised ribs on a dome built with the same parameters. Ribs stop where they would touch near the apex.</summary>
        public static void DomeRibs(MeshUtil.Builder b, int sub, Vector3 c, float r, float h, float bulge, int ribs, float ribW, float ribH, int lat = 18)
        {
            float stopR = ribs * ribW * 1.15f / (2 * Mathf.PI);
            for (int k = 0; k < ribs; k++)
            {
                float th = k / (float)ribs * Mathf.PI * 2;
                var rv = new Vector3(Mathf.Cos(th), 0, Mathf.Sin(th)); var tv = new Vector3(-Mathf.Sin(th), 0, Mathf.Cos(th));
                Vector3 pL0 = Vector3.zero, pL1 = Vector3.zero, pR0 = Vector3.zero, pR1 = Vector3.zero; bool has = false; float vprev = 0;
                for (int j = 0; j <= lat; j++)
                {
                    float t = j / (float)lat; var p = DomeProfile(t, r, h, bulge);
                    var d = DomeProfile(Mathf.Min(1, t + 0.01f), r, h, bulge) - DomeProfile(Mathf.Max(0, t - 0.01f), r, h, bulge);
                    var n2 = new Vector2(d.y, -d.x).normalized; var N = (rv * n2.x + Vector3.up * n2.y).normalized;
                    var S = c + rv * p.x + Vector3.up * p.y;
                    Vector3 L0 = S - tv * (ribW * 0.5f), R0 = S + tv * (ribW * 0.5f), L1 = L0 + N * ribH, R1 = R0 + N * ribH;
                    bool end = p.x < stopR;
                    if (end) { var ap = c + Vector3.up * p.y; L0 = ap; R0 = ap; L1 = ap + N * ribH; R1 = L1; }
                    if (has)
                    {
                        float v1 = vprev + (S - (pL0 + pR0) * 0.5f).magnitude;
                        QuadN(b, sub, pL0, pL1, L1, L0, -tv, new Vector2(0, vprev), new Vector2(ribH, vprev), new Vector2(ribH, v1), new Vector2(0, v1));
                        QuadN(b, sub, pL1, pR1, R1, L1, N, new Vector2(0, vprev), new Vector2(ribW, vprev), new Vector2(ribW, v1), new Vector2(0, v1));
                        QuadN(b, sub, pR1, pR0, R0, R1, tv, new Vector2(0, vprev), new Vector2(ribH, vprev), new Vector2(ribH, v1), new Vector2(0, v1));
                        vprev = v1;
                    }
                    else { QuadN(b, sub, L0, L1, R1, R0, -Vector3.up, Vector2.zero, new Vector2(ribH, 0), new Vector2(ribH, ribW), new Vector2(0, ribW)); }
                    pL0 = L0; pL1 = L1; pR0 = R0; pR1 = R1; has = true;
                    if (end) break;
                }
            }
        }
        public static void Sphere(MeshUtil.Builder b, int sub, Vector3 c, float r, int seg = 20, int lat = 12)
        {
            var prof = new List<Vector2>();
            for (int j = 0; j <= lat; j++) { float a = -Mathf.PI * 0.5f + Mathf.PI * j / lat; prof.Add(new Vector2(r * Mathf.Cos(a), r * Mathf.Sin(a))); }
            Lathe(b, sub, c, Vector3.up, prof, seg);
        }
        /// <summary>Capsule along 'axis' starting at c: radius r, straight length len (total length len + 2r).</summary>
        public static void Capsule(MeshUtil.Builder b, int sub, Vector3 c, Vector3 axis, float r, float len, int seg = 20, int lat = 6)
        {
            var prof = new List<Vector2>();
            for (int j = 0; j <= lat; j++) { float a = -Mathf.PI * 0.5f + Mathf.PI * 0.5f * j / lat; prof.Add(new Vector2(r * Mathf.Cos(a), r * Mathf.Sin(a))); }
            for (int j = 0; j <= lat; j++) { float a = Mathf.PI * 0.5f * j / lat; prof.Add(new Vector2(r * Mathf.Cos(a), len + r * Mathf.Sin(a))); }
            Lathe(b, sub, c, axis, prof, seg);
        }
        /// <summary>Flat disc centred at c facing n (any direction).</summary>
        public static void DiscN(MeshUtil.Builder b, int sub, Vector3 c, Vector3 n, float r, int seg = 32)
        {
            n.Normalize();
            var helper = Mathf.Abs(Vector3.Dot(n, Vector3.up)) > 0.9f ? Vector3.right : Vector3.up;
            var e1 = Vector3.Cross(n, helper).normalized; var e2 = Vector3.Cross(n, e1).normalized;
            for (int i = 0; i < seg; i++)
            {
                float a0 = i / (float)seg * Mathf.PI * 2, a1 = (i + 1) / (float)seg * Mathf.PI * 2;
                var p0 = c + (e1 * Mathf.Cos(a0) + e2 * Mathf.Sin(a0)) * r; var p1 = c + (e1 * Mathf.Cos(a1) + e2 * Mathf.Sin(a1)) * r;
                Tri(b, sub, c, p0, p1, n, Vector2.zero, new Vector2(Mathf.Cos(a0) * r, Mathf.Sin(a0) * r), new Vector2(Mathf.Cos(a1) * r, Mathf.Sin(a1) * r));
            }
        }
        public static void Cone(MeshUtil.Builder b, int sub, Vector3 baseCentre, float r0, float r1, float h, int seg = 16)
        {
            Lathe(b, sub, baseCentre, Vector3.up, new List<Vector2> { new Vector2(r0, 0), new Vector2(r1, h) }, seg, r1 > 1e-3f, false);
        }
        /// <summary>Vertical (tapered) cylinder with outward-facing sides. Replaces MeshUtil.Builder.AddCylinder, whose side
        /// triangles are wound inward (verified: their geometric normals oppose the stored outward normals).</summary>
        public static void Cylinder(MeshUtil.Builder b, int sub, Vector3 baseCentre, float rBottom, float rTop, float h, int seg = 24, bool capTop = true, bool capBottom = false)
        {
            Lathe(b, sub, baseCentre, Vector3.up, new List<Vector2> { new Vector2(rBottom, 0), new Vector2(rTop, h) }, seg, capTop, capBottom);
        }

        // ------------------------------------------------------------------ rings / architectural elements
        public static List<Vector2> Circle(Vector2 c, float r, int n = 64, float phaseDeg = 0f)
        {
            var l = new List<Vector2>(n);
            for (int i = 0; i < n; i++) { float a = phaseDeg * Mathf.Deg2Rad + i / (float)n * Mathf.PI * 2; l.Add(c + new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r)); }
            return l;
        }
        /// <summary>CCW rectangle ring in frame f: local x in [x0,x1], z in [z0,z1].</summary>
        public static List<Vector2> Rect(Frame f, float x0, float x1, float z0, float z1)
        {
            var l = new List<Vector2> { f.XZ(x0, z0), f.XZ(x1, z0), f.XZ(x1, z1), f.XZ(x0, z1) };
            if (MeshUtil.SignedArea(l) < 0) l.Reverse();
            return l;
        }
        public static List<Vector2> CCW(List<Vector2> ring) { var r = MeshUtil.Clean(ring); if (MeshUtil.SignedArea(r) < 0) r.Reverse(); return r; }

        /// <summary>Angle (deg, CCW from +x) of the ring's longest edge, reduced to (-45, 45].</summary>
        public static float PrincipalAngle(List<Vector2> ring)
        {
            float best = 0, bestLen = -1;
            for (int i = 0; i < ring.Count; i++)
            {
                var d = ring[(i + 1) % ring.Count] - ring[i]; float l = d.magnitude;
                if (l > bestLen) { bestLen = l; best = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg; }
            }
            while (best > 45f) best -= 90f; while (best <= -45f) best += 90f;
            return best;
        }
        /// <summary>Oriented bounding rectangle: frame at the rectangle centre with u along the longest edge; half sizes along u and v.</summary>
        public static Frame OrientedRect(List<Vector2> ring, out Vector2 half)
        {
            float bestLen = -1; Vector2 dir = Vector2.right;
            for (int i = 0; i < ring.Count; i++)
            {
                var d = ring[(i + 1) % ring.Count] - ring[i]; float l = d.magnitude;
                if (l > bestLen) { bestLen = l; dir = d / l; }
            }
            var perp = new Vector2(-dir.y, dir.x);
            float umin = float.MaxValue, umax = float.MinValue, vmin = float.MaxValue, vmax = float.MinValue;
            foreach (var p in ring) { float pu = Vector2.Dot(p, dir), pv = Vector2.Dot(p, perp); umin = Mathf.Min(umin, pu); umax = Mathf.Max(umax, pu); vmin = Mathf.Min(vmin, pv); vmax = Mathf.Max(vmax, pv); }
            var c2 = dir * ((umin + umax) * 0.5f) + perp * ((vmin + vmax) * 0.5f);
            half = new Vector2((umax - umin) * 0.5f, (vmax - vmin) * 0.5f);
            return new Frame(new Vector3(c2.x, 0, c2.y), new Vector3(dir.x, 0, dir.y), new Vector3(perp.x, 0, perp.y));
        }

        /// <summary>Ledge / cornice: band between y0 and y1 that projects d metres outward from ring (walls, underside, and top annulus or full top).</summary>
        public static void Ledge(MeshUtil.Builder b, int sub, List<Vector2> ring, float d, float y0, float y1, bool fillTop = false, float uvScale = 1f)
        {
            ring = CCW(ring);
            var outer = d > 1e-4f ? MeshUtil.OffsetRing(ring, d) : ring;
            b.AddWalls(sub, outer, y0, y1, uvScale);
            if (d > 1e-4f) b.AddPolygon(sub, outer, new List<List<Vector2>> { ring }, y0, uvScale, true);
            if (fillTop) b.AddPolygon(sub, outer, null, y1, uvScale);
            else if (d > 1e-4f) b.AddPolygon(sub, outer, new List<List<Vector2>> { ring }, y1, uvScale);
        }
        /// <summary>Solid band between two rings (inner/outer) from y0 to y1, closed on all sides.</summary>
        public static void Annulus(MeshUtil.Builder b, int sub, List<Vector2> inner, List<Vector2> outer, float y0, float y1, float uvScale = 1f)
        {
            inner = CCW(inner); outer = CCW(outer);
            b.AddWalls(sub, outer, y0, y1, uvScale);
            var innerRev = new List<Vector2>(inner); innerRev.Reverse(); // CW ring => faces point inward
            b.AddWalls(sub, innerRev, y0, y1, uvScale);
            b.AddPolygon(sub, outer, new List<List<Vector2>> { inner }, y0, uvScale, true);
            b.AddPolygon(sub, outer, new List<List<Vector2>> { inner }, y1, uvScale, false);
        }

        /// <summary>Round column with flared base and capital: base at basePos, total height h.</summary>
        public static void Column(MeshUtil.Builder b, int sub, Vector3 basePos, float r, float h, int seg = 20, float baseH = 0.45f, float capH = 0.55f, float capAbacus = 0f)
        {
            LK.Cylinder(b, sub, basePos, r * 1.35f, r * 1.15f, baseH, seg, true, false);
            LK.Cylinder(b, sub, basePos + Vector3.up * baseH, r, r * 0.9f, h - baseH - capH, seg, false, false);
            LK.Cylinder(b, sub, basePos + Vector3.up * (h - capH), r * 0.9f, r * 1.35f, capH, seg, true, false);
            if (capAbacus > 0) Box(b, sub, basePos + Vector3.up * (h + capAbacus * 0.5f), Vector3.right, Vector3.forward, new Vector3(r * 2.9f, capAbacus, r * 2.9f));
        }
        /// <summary>Square pier with plinth and capital block in frame f at local (x,z), from y0 to y1, side s.</summary>
        public static void SquareColumn(MeshUtil.Builder b, int sub, Frame f, float x, float z, float y0, float y1, float s, float plinthH = 0.6f, float capH = 0.8f)
        {
            FBox(b, sub, f, new Vector3(x, y0 + plinthH * 0.5f, z), new Vector3(s * 1.35f, plinthH, s * 1.35f));
            FBox(b, sub, f, new Vector3(x, (y0 + plinthH + y1 - capH) * 0.5f, z), new Vector3(s, (y1 - capH) - (y0 + plinthH), s));
            FBox(b, sub, f, new Vector3(x, y1 - capH * 0.5f, z), new Vector3(s * 1.3f, capH, s * 1.3f));
        }

        /// <summary>Straight flight of n steps in frame f, centred at local x = xc, width w. Top landing edge at local z = zNear (building side);
        /// the flight descends outward along +v. Step 0 (lowest) has its front edge at zNear + n*tread. Rises from y0.</summary>
        public static void Stairs(MeshUtil.Builder b, int sub, Frame f, float xc, float w, float zNear, int n, float rise, float tread, float y0)
        {
            float zFar = zNear + n * tread;
            for (int k = 0; k < n; k++)
            {
                float zFront = zFar - k * tread; float zc = (zNear + zFront) * 0.5f; float depth = zFront - zNear;
                float yTop = y0 + (k + 1) * rise;
                FBox(b, sub, f, new Vector3(xc, (y0 + yTop) * 0.5f, zc), new Vector3(w, yTop - y0, depth));
            }
        }
        /// <summary>Concentric circular steps. Top platform (radius rTop) at c.y + n*rise; lowest step radius rTop + (n-1)*tread.</summary>
        public static void RoundSteps(MeshUtil.Builder b, int subTread, int subRiser, Vector3 c, float rTop, int n, float rise, float tread, int seg = 96)
        {
            for (int k = 0; k < n; k++)
            {
                float r = rTop + (n - 1 - k) * tread; float y = k * rise;
                LK.Cylinder(b, subRiser, c + Vector3.up * y, r, r, rise, seg, false, false);
                b.AddDisc(subTread, c + Vector3.up * (y + rise), r, seg, true);
            }
        }
        public static void Flagpole(MeshUtil.Builder b, int subPole, Vector3 basePos, float h, float r = 0.08f)
        {
            LK.Cylinder(b, subPole, basePos, r * 2.5f, r * 2.5f, 0.6f, 12, true, false);
            LK.Cylinder(b, subPole, basePos + Vector3.up * 0.6f, r, r * 0.6f, h - 0.6f, 10, true, false);
            Sphere(b, subPole, basePos + Vector3.up * h, r * 1.6f, 10, 6);
        }
        /// <summary>Simple handrail (top rail + posts) along a straight line in frame f at depth z, from x0 to x1.</summary>
        public static void Handrail(MeshUtil.Builder b, int sub, Frame f, float x0, float x1, float z, float y0, float h = 1.0f, float postSpacing = 1.5f)
        {
            FBox(b, sub, f, new Vector3((x0 + x1) * 0.5f, y0 + h, z), new Vector3(x1 - x0, 0.06f, 0.06f));
            int n = Mathf.Max(1, Mathf.RoundToInt((x1 - x0) / postSpacing));
            for (int i = 0; i <= n; i++) { float x = x0 + (x1 - x0) * i / n; FBox(b, sub, f, new Vector3(x, y0 + h * 0.5f, z), new Vector3(0.05f, h, 0.05f)); }
        }

        /// <summary>Recessed window with a reveal in a wall lying in frame f (wall face at depth z, facing +v).
        /// Glass sits 'recess' behind the wall face, reveals close the sides, frame boxes protrude 'frameOut' from the wall.</summary>
        public static void RecessedWindow(MeshUtil.Builder b, int subFrame, int subGlass, int subReveal, Frame f, float x0, float x1, float y0, float y1, float z, float recess = 0.3f, float frameW = 0.12f, float frameOut = 0.05f)
        {
            float zg = z - recess;
            FWall(b, subGlass, f, x0, x1, y0, y1, zg, true);
            QuadN(b, subReveal, f.P(x0, y0, zg), f.P(x0, y1, zg), f.P(x0, y1, z), f.P(x0, y0, z), f.u, new Vector2(0, y0), new Vector2(0, y1), new Vector2(recess, y1), new Vector2(recess, y0));
            QuadN(b, subReveal, f.P(x1, y0, zg), f.P(x1, y1, zg), f.P(x1, y1, z), f.P(x1, y0, z), -f.u, new Vector2(0, y0), new Vector2(0, y1), new Vector2(recess, y1), new Vector2(recess, y0));
            QuadN(b, subReveal, f.P(x0, y0, zg), f.P(x1, y0, zg), f.P(x1, y0, z), f.P(x0, y0, z), Vector3.up, new Vector2(x0, 0), new Vector2(x1, 0), new Vector2(x1, recess), new Vector2(x0, recess));
            QuadN(b, subReveal, f.P(x0, y1, zg), f.P(x1, y1, zg), f.P(x1, y1, z), f.P(x0, y1, z), Vector3.down, new Vector2(x0, 0), new Vector2(x1, 0), new Vector2(x1, recess), new Vector2(x0, recess));
            float zc = z + frameOut * 0.5f - 0.005f; float d = frameOut + 0.01f;
            FBox(b, subFrame, f, new Vector3((x0 + x1) * 0.5f, y0 - frameW * 0.5f, zc), new Vector3(x1 - x0 + frameW * 2, frameW, d));
            FBox(b, subFrame, f, new Vector3((x0 + x1) * 0.5f, y1 + frameW * 0.5f, zc), new Vector3(x1 - x0 + frameW * 2, frameW, d));
            FBox(b, subFrame, f, new Vector3(x0 - frameW * 0.5f, (y0 + y1) * 0.5f, zc), new Vector3(frameW, y1 - y0, d));
            FBox(b, subFrame, f, new Vector3(x1 + frameW * 0.5f, (y0 + y1) * 0.5f, zc), new Vector3(frameW, y1 - y0, d));
        }

        /// <summary>Wall in frame f (x 0..len, y0..y1, at depth z, facing +v) with axis-aligned rectangular holes (x0,x1,y0,y1 as Vector4).
        /// Built by band decomposition (horizontal bands split at every hole edge, quads between holes) - no triangulation with holes,
        /// which MeshUtil.Triangulate does not handle reliably for many holes.</summary>
        public static void HoledWall(MeshUtil.Builder b, int sub, Frame f, float len, float y0, float y1, List<Vector4> holes, float z = 0f)
        {
            const float eps = 1e-3f;
            var hs = new List<Vector4>();
            if (holes != null)
                foreach (var h in holes)
                {
                    float x0 = Mathf.Max(0, h.x), x1 = Mathf.Min(len, h.y), hy0 = Mathf.Max(y0, h.z), hy1 = Mathf.Min(y1, h.w);
                    if (x1 - x0 > eps && hy1 - hy0 > eps) hs.Add(new Vector4(x0, x1, hy0, hy1));
                }
            var ys = new List<float> { y0, y1 };
            foreach (var h in hs) { ys.Add(h.z); ys.Add(h.w); }
            ys.Sort();
            var iv = new List<Vector2>();
            for (int i = 0; i < ys.Count - 1; i++)
            {
                float ya = ys[i], yb = ys[i + 1]; if (yb - ya <= eps) continue;
                float ym = (ya + yb) * 0.5f;
                iv.Clear();
                foreach (var h in hs) if (h.z <= ym && h.w >= ym) iv.Add(new Vector2(h.x, h.y));
                iv.Sort((p, q) => p.x.CompareTo(q.x));
                float x = 0;
                foreach (var s in iv)
                {
                    if (s.x > x + eps) FWall(b, sub, f, x, s.x, ya, yb, z, true);
                    x = Mathf.Max(x, s.y);
                }
                if (x < len - eps) FWall(b, sub, f, x, len, ya, yb, z, true);
            }
        }

        /// <summary>Facade frame of face k (0 = +u, 1 = +v, 2 = -u, 3 = -v) of a square of half side h centred on frame f at height y.
        /// The returned frame runs along the face (x from 0 to 2h) with v pointing outward.</summary>
        public static Frame SquareFace(Frame f, int k, float h, float y)
        {
            switch (k & 3)
            {
                case 0: return new Frame(f.P(h, y, -h), f.v, f.u);
                case 1: return new Frame(f.P(h, y, h), -f.u, f.v);
                case 2: return new Frame(f.P(-h, y, h), -f.v, -f.u);
                default: return new Frame(f.P(-h, y, -h), f.u, -f.v);
            }
        }

        /// <summary>Approximate ring radius (mean distance of vertices to centre).</summary>
        public static float MeanRadius(List<Vector2> ring, Vector2 c) { float s = 0; foreach (var p in ring) s += (p - c).magnitude; return s / Mathf.Max(1, ring.Count); }
    }
}
