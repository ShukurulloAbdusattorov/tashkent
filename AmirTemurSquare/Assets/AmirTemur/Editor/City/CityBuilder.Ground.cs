using System;
using System.Collections.Generic;
using UnityEngine;

namespace AmirTemur.Editor
{
    public static partial class CityBuilder
    {
        // ------------------------------------------------------------------ road corridor index
        /// <summary>Spatial index of road segments with their mitred corridor quads (half width = w/2 + kerb/2, i.e. the kerb centre line).</summary>
        class RoadIndex
        {
            public class Seg
            {
                public int road, idx; public Vector2 a, b, dir; public float len, halfW;
                public Vector2 fwdA, fwdB;      // bisector normals at a / b (segment zone: dot(p-a,fwdA) >= 0 && dot(p-b,fwdB) <= 0)
                public List<Vector2> quad;      // mitred corridor quad (right edge then left edge back)
            }
            public readonly List<Seg> Segs = new List<Seg>();
            public readonly List<CityData.Road> RoadList;
            public readonly List<List<Vector2>> Lines;      // cleaned centrelines per road (null when skipped)
            public readonly List<float> HalfW;              // corridor half width per road (kerb centre line)
            public readonly List<float> RoadHalfW;          // asphalt half width per road
            readonly Dictionary<long, List<int>> grid = new Dictionary<long, List<int>>();
            const float Cell = 25f;
            int[] stamp; int stampId;
            static long Key(int cx, int cz) => (long)(cx + 100000) * 1000000L + (cz + 100000);

            public RoadIndex(List<CityData.Road> roads)
            {
                RoadList = roads; Lines = new List<List<Vector2>>(); HalfW = new List<float>(); RoadHalfW = new List<float>();
                for (int r = 0; r < roads.Count; r++)
                {
                    var road = roads[r];
                    var line = CleanLine(road.pts);
                    float w = Mathf.Max(3f, road.width > 0 ? road.width : 5f);
                    RoadHalfW.Add(w * 0.5f); HalfW.Add(w * 0.5f + KerbW * 0.5f);
                    if (road.tunnel || line.Count < 2) { Lines.Add(null); continue; }
                    Lines.Add(line);
                    var off = MitreOffsets(line);
                    float hw = HalfW[r];
                    for (int i = 0; i + 1 < line.Count; i++)
                    {
                        var s = new Seg { road = r, idx = i, a = line[i], b = line[i + 1], halfW = hw };
                        var d = s.b - s.a; s.len = d.magnitude; if (s.len < 1e-5f) continue;
                        s.dir = d / s.len;
                        // bisector "forward" normals (perpendicular to the mitre line): sum of adjacent unit directions
                        s.fwdA = i == 0 ? s.dir : (s.dir + (line[i] - line[i - 1]).normalized);
                        s.fwdB = i + 2 >= line.Count ? s.dir : (s.dir + (line[i + 2] - line[i + 1]).normalized);
                        if (s.fwdA.sqrMagnitude < 1e-6f) s.fwdA = s.dir; else s.fwdA.Normalize();
                        if (s.fwdB.sqrMagnitude < 1e-6f) s.fwdB = s.dir; else s.fwdB.Normalize();
                        s.quad = new List<Vector2> { s.a + off[i] * hw, s.b + off[i + 1] * hw, s.b - off[i + 1] * hw, s.a - off[i] * hw };
                        int si = Segs.Count; Segs.Add(s);
                        var bb = BBox(s.quad); float pad = 2f;
                        for (int cx = Mathf.FloorToInt((bb.xMin - pad) / Cell); cx <= Mathf.FloorToInt((bb.xMax + pad) / Cell); cx++)
                            for (int cz = Mathf.FloorToInt((bb.yMin - pad) / Cell); cz <= Mathf.FloorToInt((bb.yMax + pad) / Cell); cz++)
                            {
                                long k = Key(cx, cz); if (!grid.TryGetValue(k, out var l)) { l = new List<int>(); grid[k] = l; } l.Add(si);
                            }
                    }
                }
                stamp = new int[Segs.Count];
            }

            /// <summary>Segment indices whose grid cells overlap p +- radius (deduplicated).</summary>
            public void Candidates(Vector2 p, float radius, List<int> outList)
            {
                outList.Clear(); stampId++;
                for (int cx = Mathf.FloorToInt((p.x - radius) / Cell); cx <= Mathf.FloorToInt((p.x + radius) / Cell); cx++)
                    for (int cz = Mathf.FloorToInt((p.y - radius) / Cell); cz <= Mathf.FloorToInt((p.y + radius) / Cell); cz++)
                        if (grid.TryGetValue(Key(cx, cz), out var l))
                            foreach (int si in l) if (stamp[si] != stampId) { stamp[si] = stampId; outList.Add(si); }
            }

            readonly List<int> tmp = new List<int>();

            /// <summary>Segments whose corridor (halfW + extra) comes within reach of p.</summary>
            public void Near(Vector2 p, float extra, List<int> outList)
            {
                float maxHw = 14f;
                Candidates(p, maxHw + extra, tmp);
                outList.Clear();
                foreach (int si in tmp) { var s = Segs[si]; if (DistToSeg(p, s.a, s.b) < s.halfW + extra) outList.Add(si); }
            }

            /// <summary>True when p lies inside any road corridor (mitred quad or capsule of radius halfW+margin), ignoring excludeRoad.</summary>
            public bool Inside(Vector2 p, float margin = 0f, int excludeRoad = -1)
            {
                Candidates(p, 14f + Mathf.Max(0f, margin), tmp);
                foreach (int si in tmp)
                {
                    var s = Segs[si]; if (s.road == excludeRoad) continue;
                    if (DistToSeg(p, s.a, s.b) < s.halfW + margin) return true;
                    if (margin >= 0f && MeshUtil.PointInPolygon(p, s.quad)) return true;
                }
                return false;
            }

            /// <summary>True when p lies inside any mitred corridor quad (exactly what the road ribbons cover).</summary>
            public bool InsideQuad(Vector2 p)
            {
                Candidates(p, 14f, tmp);
                foreach (int si in tmp) if (MeshUtil.PointInPolygon(p, Segs[si].quad)) return true;
                return false;
            }

            /// <summary>Nearest segment index (or -1) to p within maxDist.</summary>
            public int Nearest(Vector2 p, float maxDist, out float dist, out float t)
            {
                Candidates(p, maxDist + 14f, tmp);
                int best = -1; dist = maxDist; t = 0;
                foreach (int si in tmp)
                {
                    var s = Segs[si]; float d = DistToSeg(p, s.a, s.b, out float tt);
                    if (d < dist) { dist = d; best = si; t = tt; }
                }
                return best;
            }
        }

        // ------------------------------------------------------------------ ground
        class HolePoly { public List<Vector2> ring; public Rect bb; }

        static void BuildGround()
        {
            // data extent (+ margin), snapped to 64 m cells
            float x0 = float.MaxValue, z0 = float.MaxValue, x1 = float.MinValue, z1 = float.MinValue;
            void Acc(IList<Vector2> pts) { foreach (var p in pts) { x0 = Mathf.Min(x0, p.x); z0 = Mathf.Min(z0, p.y); x1 = Mathf.Max(x1, p.x); z1 = Mathf.Max(z1, p.y); } }
            foreach (var b in D.buildings) Acc(b.outer);
            foreach (var r in D.roads) Acc(r.pts);
            foreach (var a in D.areas) Acc(a.outer);
            if (x0 > x1) { x0 = -900; x1 = 750; z0 = -650; z1 = 700; }
            const float cell = 64f;
            x0 = Mathf.Floor((x0 - 30f) / cell) * cell; z0 = Mathf.Floor((z0 - 30f) / cell) * cell;
            x1 = Mathf.Ceil((x1 + 30f) / cell) * cell; z1 = Mathf.Ceil((z1 + 30f) / cell) * cell;

            // areas that sit below ground level are cut out of the ground plane
            var holes = new List<HolePoly>();
            foreach (var a in D.areas)
            {
                if (a.kind != "parking" && a.kind != "fountain" && a.kind != "water") continue;
                var ring = MeshUtil.Clean(a.outer); if (!ValidRing(ring, 2f)) continue;
                var cut = MeshUtil.OffsetRing(ring, a.kind == "parking" ? KerbW * 0.5f : 0.175f);
                if (!ValidRing(cut, 2f)) cut = ring;
                holes.Add(new HolePoly { ring = cut, bb = BBox(cut) });
            }

            var cutter = new GroundCutter(Roads, holes);
            int cells = 0;
            for (float x = x0; x < x1; x += cell)
                for (float z = z0; z < z1; z += cell)
                    cells += cutter.Recurse(x, z, cell);
            Count("groundCells", cells);

            // outer skirt (dry grass) slightly below ground level to hide the edge of the world
            const float skirt = 1500f;
            var t = T(new Vector2(0, 0), "grass_dry");
            float ys = -0.03f;
            FlatQuadUp(t.B, t.Sub, new Vector3(-skirt, ys, -skirt), new Vector3(-skirt, ys, z0), new Vector3(skirt, ys, z0), new Vector3(skirt, ys, -skirt), 1f);
            FlatQuadUp(t.B, t.Sub, new Vector3(-skirt, ys, z1), new Vector3(-skirt, ys, skirt), new Vector3(skirt, ys, skirt), new Vector3(skirt, ys, z1), 1f);
            FlatQuadUp(t.B, t.Sub, new Vector3(-skirt, ys, z0), new Vector3(-skirt, ys, z1), new Vector3(x0, ys, z1), new Vector3(x0, ys, z0), 1f);
            FlatQuadUp(t.B, t.Sub, new Vector3(x1, ys, z0), new Vector3(x1, ys, z1), new Vector3(skirt, ys, z1), new Vector3(skirt, ys, z0), 1f);
            // a plain plate under everything catches anything looking through jagged edges
            FlatQuadUp(t.B, t.Sub, new Vector3(x0, -0.35f, z0), new Vector3(x0, -0.35f, z1), new Vector3(x1, -0.35f, z1), new Vector3(x1, -0.35f, z0), 1f);
        }

        /// <summary>Quadtree ground generator: emits the y=0 sidewalk plane minus road corridors (exact half-plane clipping along straight
        /// road edges, 0.25 m cells around junctions) and minus sunken area polygons.</summary>
        class GroundCutter
        {
            readonly RoadIndex roads; readonly List<HolePoly> holes;
            readonly List<int> near = new List<int>();
            const float MinLeaf = 0.25f; const float ClipLeaf = 2f;
            const string GroundMat = "sidewalk_concrete";

            public GroundCutter(RoadIndex r, List<HolePoly> h) { roads = r; holes = h; }

            public int Recurse(float x0, float z0, float s)
            {
                var c = new Vector2(x0 + s * 0.5f, z0 + s * 0.5f); float halfDiag = s * 0.7072f;
                bool holeNear = false;
                foreach (var h in holes)
                {
                    if (h.bb.xMax < x0 || h.bb.xMin > x0 + s || h.bb.yMax < z0 || h.bb.yMin > z0 + s) continue;
                    int inside = 0;
                    if (MeshUtil.PointInPolygon(new Vector2(x0, z0), h.ring)) inside++;
                    if (MeshUtil.PointInPolygon(new Vector2(x0 + s, z0), h.ring)) inside++;
                    if (MeshUtil.PointInPolygon(new Vector2(x0 + s, z0 + s), h.ring)) inside++;
                    if (MeshUtil.PointInPolygon(new Vector2(x0, z0 + s), h.ring)) inside++;
                    bool bbInside = h.bb.xMin >= x0 && h.bb.xMax <= x0 + s && h.bb.yMin >= z0 && h.bb.yMax <= z0 + s;
                    bool crossing = (inside > 0 && inside < 4) || bbInside || EdgeCrossesCell(h.ring, x0, z0, s);
                    if (inside == 4 && !crossing) return 0; // fully inside the hole
                    if (crossing) holeNear = true;
                }
                var mine = new List<int>();
                roads.Near(c, halfDiag, mine);
                if (mine.Count == 0 && !holeNear) { EmitCell(x0, z0, s); return 1; }
                // cell entirely inside one corridor quad -> nothing to emit
                foreach (int si in mine)
                {
                    var q = roads.Segs[si].quad;
                    if (MeshUtil.PointInPolygon(new Vector2(x0, z0), q) && MeshUtil.PointInPolygon(new Vector2(x0 + s, z0), q)
                        && MeshUtil.PointInPolygon(new Vector2(x0 + s, z0 + s), q) && MeshUtil.PointInPolygon(new Vector2(x0, z0 + s), q)) return 0;
                }
                float minLeaf = (holeNear && mine.Count == 0) ? 0.5f : MinLeaf;
                if (s <= minLeaf + 1e-4f)
                {
                    if (roads.InsideQuad(c) || InHole(c)) return 0;
                    EmitCell(x0, z0, s); return 1;
                }
                if (!holeNear && s <= ClipLeaf + 1e-4f)
                {
                    int n = TryClip(x0, z0, s, mine);
                    if (n >= 0) return n;
                }
                float h2 = s * 0.5f;
                return Recurse(x0, z0, h2) + Recurse(x0 + h2, z0, h2) + Recurse(x0, z0 + h2, h2) + Recurse(x0 + h2, z0 + h2, h2);
            }

            bool InHole(Vector2 p)
            {
                foreach (var h in holes) if (h.bb.Contains(p) && MeshUtil.PointInPolygon(p, h.ring)) return true;
                return false;
            }

            static bool EdgeCrossesCell(List<Vector2> ring, float x0, float z0, float s)
            {
                Vector2 c0 = new Vector2(x0, z0), c1 = new Vector2(x0 + s, z0), c2 = new Vector2(x0 + s, z0 + s), c3 = new Vector2(x0, z0 + s);
                for (int i = 0; i < ring.Count; i++)
                {
                    var a = ring[i]; var b = ring[(i + 1) % ring.Count];
                    if (Mathf.Max(a.x, b.x) < x0 || Mathf.Min(a.x, b.x) > x0 + s || Mathf.Max(a.y, b.y) < z0 || Mathf.Min(a.y, b.y) > z0 + s) continue;
                    if (MeshUtil.SegmentsIntersect(a, b, c0, c1) || MeshUtil.SegmentsIntersect(a, b, c1, c2) || MeshUtil.SegmentsIntersect(a, b, c2, c3) || MeshUtil.SegmentsIntersect(a, b, c3, c0)) return true;
                }
                return false;
            }

            /// <summary>Exact clipping when all nearby segments form one contiguous run of a single road. Returns emitted piece count or -1.</summary>
            int TryClip(float x0, float z0, float s, List<int> segs)
            {
                segs.Sort((p, q) => { var a = roads.Segs[p]; var b = roads.Segs[q]; return a.road != b.road ? a.road.CompareTo(b.road) : a.idx.CompareTo(b.idx); });
                var first = roads.Segs[segs[0]];
                for (int k = 1; k < segs.Count; k++)
                {
                    var sg = roads.Segs[segs[k]];
                    if (sg.road != first.road || sg.idx != roads.Segs[segs[k - 1]].idx + 1) return -1;
                }
                var cellPoly = new List<Vector2> { new Vector2(x0, z0), new Vector2(x0 + s, z0), new Vector2(x0 + s, z0 + s), new Vector2(x0, z0 + s) };
                int emitted = 0;
                // region before the first segment's start bisector
                var last = roads.Segs[segs[segs.Count - 1]];
                var before = ClipHalfPlane(cellPoly, first.a, -first.fwdA, 0f);
                if (before.Count >= 3) { EmitPoly(before); emitted++; }
                var after = ClipHalfPlane(cellPoly, last.b, last.fwdB, 0f);
                if (after.Count >= 3) { EmitPoly(after); emitted++; }
                foreach (int si in segs)
                {
                    var sg = roads.Segs[si];
                    var poly = ClipHalfPlane(cellPoly, sg.a, sg.fwdA, 0f);           // zone start
                    poly = ClipHalfPlane(poly, sg.b, -sg.fwdB, 0f);                    // zone end
                    if (poly.Count < 3) continue;
                    var n = Perp(sg.dir);
                    float side = Vector2.Dot(MeshUtil.Centroid(poly) - sg.a, n); if (side < 0) n = -n;
                    poly = ClipHalfPlane(poly, sg.a, n, sg.halfW);                     // outside the corridor edge line
                    if (poly.Count >= 3) { EmitPoly(poly); emitted++; }
                }
                return emitted;
            }

            /// <summary>Keeps the part of a convex polygon with dot(p - o, n) >= d.</summary>
            static List<Vector2> ClipHalfPlane(List<Vector2> poly, Vector2 o, Vector2 n, float d)
            {
                var res = new List<Vector2>(poly.Count + 2);
                for (int i = 0; i < poly.Count; i++)
                {
                    var a = poly[i]; var b = poly[(i + 1) % poly.Count];
                    float fa = Vector2.Dot(a - o, n) - d, fb = Vector2.Dot(b - o, n) - d;
                    if (fa >= 0) res.Add(a);
                    if ((fa >= 0) != (fb >= 0)) { float t = fa / (fa - fb); res.Add(a + (b - a) * t); }
                }
                return res;
            }

            void EmitCell(float x0, float z0, float s)
            {
                var t = T(new Vector2(x0 + s * 0.5f, z0 + s * 0.5f), GroundMat);
                FlatQuadUp(t.B, t.Sub, new Vector3(x0, 0, z0), new Vector3(x0, 0, z0 + s), new Vector3(x0 + s, 0, z0 + s), new Vector3(x0 + s, 0, z0), 1f);
            }

            void EmitPoly(List<Vector2> poly)
            {
                if (Mathf.Abs(MeshUtil.SignedArea(poly)) < 1e-4f) return;
                var t = T(MeshUtil.Centroid(poly), GroundMat);
                AddFan(t.B, t.Sub, poly, 0f, 1f);
            }
        }
    }
}
