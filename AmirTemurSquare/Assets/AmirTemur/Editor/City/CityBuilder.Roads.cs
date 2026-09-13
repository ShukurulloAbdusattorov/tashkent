using System;
using System.Collections.Generic;
using UnityEngine;

namespace AmirTemur.Editor
{
    public static partial class CityBuilder
    {
        static bool IsMarkedRoadClass(string cls) => cls == "primary" || cls == "secondary" || cls == "tertiary" || cls == "residential";
        static float RoadSurfaceY(CityData.Road r) => RoadY + (r.bridge ? Mathf.Max(1, r.layer) * 5f : 0f);
        static int LaneCount(CityData.Road r, float w) => r.lanes > 0 ? r.lanes : Mathf.Max(1, Mathf.RoundToInt(w / 3.5f));

        // ------------------------------------------------------------------ roads + kerbs
        static void BuildRoads()
        {
            int nRoads = 0, nDiscs = 0, nKerbRuns = 0;
            for (int ri = 0; ri < D.roads.Count; ri++)
            {
                var r = D.roads[ri]; var line = Roads.Lines[ri];
                if (line == null) continue; // tunnel or degenerate
                try
                {
                    float hw = Roads.RoadHalfW[ri]; float y = RoadSurfaceY(r);
                    string mat = r.surface == "paving_stones" || r.surface == "sett" ? "paving_small" : "asphalt";
                    AddStrip(line, hw, y, mat, true, 1f);
                    nRoads++;

                    // junction / bend discs slightly below the ribbon (fills wedge gaps between ribbons of different roads)
                    for (int i = 0; i < line.Count; i++)
                    {
                        bool emit = i == 0 || i == line.Count - 1;
                        if (!emit)
                        {
                            var d0 = (line[i] - line[i - 1]).normalized; var d1 = (line[i + 1] - line[i]).normalized;
                            emit = Vector2.Dot(d0, d1) < 0.995f;
                        }
                        if (!emit) continue;
                        var t = T(line[i], mat);
                        t.B.AddDisc(t.Sub, XZ(line[i], y - 0.005f), hw, hw < 4f ? 14 : 24, true);
                        nDiscs++;
                    }

                    // kerbs on both edges (0.3 m wide, from road level up to 1 cm above the sidewalk), trimmed inside other roads
                    if (r.bridge) continue;
                    for (int side = -1; side <= 1; side += 2)
                    {
                        var kerbLine = OffsetLine(line, side * (hw + KerbW * 0.5f));
                        foreach (var run in SplitByCorridor(kerbLine, ri, KerbW * 0.5f, 1f))
                        {
                            AddBoxStrip(run, KerbW * 0.5f, y, KerbTopY, "kerb", true, 1f);
                            nKerbRuns++;
                        }
                    }
                }
                catch (Exception e) { Debug.LogWarning($"[CityBuilder] road {r.id} ({r.name}) failed: {e.Message}"); }
            }
            Count("roads", nRoads); Count("junctionDiscs", nDiscs); Count("kerbRuns", nKerbRuns);
        }

        // ------------------------------------------------------------------ markings
        static void BuildMarkings()
        {
            const string M = "road_marking_white";
            int nDash = 0, nSolid = 0, nZebra = 0;
            for (int ri = 0; ri < D.roads.Count; ri++)
            {
                var r = D.roads[ri]; var line = Roads.Lines[ri];
                if (line == null || !IsMarkedRoadClass(r.cls)) continue;
                try
                {
                    float w = Roads.RoadHalfW[ri] * 2f; float hw = w * 0.5f; float y = RoadSurfaceY(r) + (MarkY - RoadY);
                    int lanes = LaneCount(r, w);
                    bool twoWay = !r.oneway;

                    // centre line
                    if (twoWay)
                    {
                        if (lanes >= 4) nSolid += SolidLine(line, 0f, y, ri, M);
                        else nDash += DashedLine(line, 0f, y, ri, M, 3f, 6f);
                    }
                    // lane dividers
                    if (lanes >= 3)
                    {
                        float laneW = w / lanes;
                        for (int k = 1; k < lanes; k++)
                        {
                            float off = -hw + k * laneW;
                            if (twoWay && Mathf.Abs(off) < 0.3f) continue;
                            nDash += DashedLine(line, off, y, ri, M, 3f, 6f);
                        }
                    }
                    // edge lines
                    if (r.cls != "residential")
                    {
                        nSolid += SolidLine(line, hw - 0.3f, y, ri, M);
                        nSolid += SolidLine(line, -(hw - 0.3f), y, ri, M);
                    }
                }
                catch (Exception e) { Debug.LogWarning($"[CityBuilder] markings for road {r.id} failed: {e.Message}"); }
            }

            // zebra crossings at crossing nodes within 2 m of a road centreline
            foreach (var c in D.crossings)
            {
                try
                {
                    int si = Roads.Nearest(c, 2f, out _, out float t); if (si < 0) continue;
                    var s = Roads.Segs[si]; var r = D.roads[s.road];
                    float y = RoadSurfaceY(r) + (MarkY - RoadY) + 0.001f;
                    var centre = s.a + (s.b - s.a) * t; var d = s.dir; var n = Perp(d);
                    float w = Roads.RoadHalfW[s.road] * 2f;
                    float usable = w - 0.6f; int stripes = Mathf.Max(2, Mathf.FloorToInt((usable + 0.5f) / 1.0f));
                    float startOff = -(stripes * 1.0f - 0.5f) * 0.5f;
                    var tgt = T(centre, M, false);
                    for (int k = 0; k < stripes; k++)
                    {
                        float off = startOff + k * 1.0f; // stripe centre across the road
                        var cc = centre + n * off;
                        var p0 = cc - d * 1.25f - n * 0.25f; var p1 = cc + d * 1.25f - n * 0.25f;
                        var p2 = cc + d * 1.25f + n * 0.25f; var p3 = cc - d * 1.25f + n * 0.25f;
                        FlatQuadUp(tgt.B, tgt.Sub, XZ(p0, y), XZ(p1, y), XZ(p2, y), XZ(p3, y), 1f);
                    }
                    nZebra++;
                }
                catch (Exception e) { Debug.LogWarning($"[CityBuilder] crossing at {c} failed: {e.Message}"); }
            }
            Count("markingDashes", nDash); Count("markingSolidRuns", nSolid); Count("zebras", nZebra);
        }

        static int DashedLine(List<Vector2> line, float offset, float y, int roadIdx, string mat, float dash, float gap)
        {
            var o = Mathf.Abs(offset) < 1e-4f ? line : OffsetLine(line, offset);
            float len = Length(o); int n = 0;
            for (float s = 1f; s < len - 0.5f; s += dash + gap)
            {
                float e = Mathf.Min(s + dash, len);
                var p0 = PointAt(o, s, out _); var p1 = PointAt(o, e, out _);
                if (Roads.Inside((p0 + p1) * 0.5f, -0.15f, roadIdx)) continue;
                AddStrip(new List<Vector2> { p0, p1 }, 0.06f, y, mat, false, 1f);
                n++;
            }
            return n;
        }

        static int SolidLine(List<Vector2> line, float offset, float y, int roadIdx, string mat)
        {
            var o = Mathf.Abs(offset) < 1e-4f ? line : OffsetLine(line, offset);
            int n = 0;
            foreach (var run in SplitByCorridor(o, roadIdx, -0.15f, 1.5f)) { AddStrip(run, 0.06f, y, mat, false, 1f); n++; }
            return n;
        }

        // ------------------------------------------------------------------ paths
        static string PathMaterial(CityData.Path p)
        {
            if (p.cls == "cycleway") return "asphalt_worn";
            switch (p.surface)
            {
                case "paving_stones": case "sett": case "paved": return "paving_plaza";
                case "asphalt": return "asphalt_worn";
                default: return p.cls == "pedestrian" ? "paving_granite" : "sidewalk_concrete";
            }
        }

        static void BuildPaths()
        {
            int nPaths = 0, nSteps = 0;
            foreach (var p in D.paths)
            {
                if (p.tunnel) continue;
                try
                {
                    var line = CleanLine(p.pts); if (line.Count < 2) continue;
                    float w = p.width > 0.5f ? p.width : 2f; float hw = w * 0.5f;
                    string mat = PathMaterial(p);
                    var runs = SplitByCorridor(line, -1, KerbW * 0.5f, 1.5f);
                    foreach (var run in runs)
                    {
                        AddBoxStrip(run, hw, PathBottomY, PathTopY, mat, true, 1f);
                        if (p.cls == "steps") { nSteps += AddSteps(run, hw); }
                    }
                    nPaths++;
                }
                catch (Exception e) { Debug.LogWarning($"[CityBuilder] path {p.id} failed: {e.Message}"); }
            }
            Count("paths", nPaths); Count("stepBoxes", nSteps);
        }

        /// <summary>3–6 step boxes (0.15 m rise each) along the run, rising towards its end.</summary>
        static int AddSteps(List<Vector2> run, float hw)
        {
            float len = Length(run); if (len < 1.0f) return 0;
            int n = Mathf.Clamp(Mathf.RoundToInt(len / 0.5f), 3, 6);
            float d = len / n; int made = 0;
            for (int k = 0; k < n; k++)
            {
                var a = PointAt(run, k * d, out _); var b = PointAt(run, (k + 1) * d, out _);
                var dir = b - a; if (dir.sqrMagnitude < 1e-6f) continue;
                float top = PathTopY + 0.15f * (k + 1);
                AddOBox((a + b) * 0.5f, dir, dir.magnitude + 0.01f, hw * 2f, PathTopY - 0.01f, top, "concrete_smooth", true);
                made++;
            }
            return made;
        }

        // ------------------------------------------------------------------ barriers
        static void BuildBarriers()
        {
            int nKerb = 0, nFence = 0, nHedge = 0, nWall = 0, nPosts = 0;
            foreach (var k in D.kerbs)
            {
                try
                {
                    var line = CleanLine(k.pts); if (line.Count < 2) continue;
                    float h = k.height > 0.02f ? Mathf.Min(k.height, 0.4f) : 0.15f;
                    AddBoxStrip(line, 0.15f, -0.02f, h, "kerb", true, 1f); nKerb++;
                }
                catch (Exception e) { Debug.LogWarning($"[CityBuilder] kerb {k.id} failed: {e.Message}"); }
            }
            foreach (var f in D.fences)
            {
                try
                {
                    var line = CleanLine(f.pts); if (line.Count < 2) continue;
                    float h = f.height > 0.3f ? f.height : 1.2f;
                    float len = Length(line);
                    for (float s = 0; s <= len + 0.01f; s += 2f)
                    {
                        var p = PointAt(line, Mathf.Min(s, len), out _);
                        var t = T(p, "metal_dark");
                        t.B.AddBox(t.Sub, new Vector3(p.x, h * 0.5f, p.y), new Vector3(0.05f, h, 0.05f), 1f);
                        nPosts++;
                    }
                    AddBoxStrip(line, 0.02f, h * 0.42f, h * 0.42f + 0.04f, "metal_dark", false, 1f);
                    AddBoxStrip(line, 0.02f, h - 0.05f, h, "metal_dark", false, 1f);
                    nFence++;
                }
                catch (Exception e) { Debug.LogWarning($"[CityBuilder] fence {f.id} failed: {e.Message}"); }
            }
            foreach (var hdg in D.hedges)
            {
                try
                {
                    var line = CleanLine(hdg.pts); if (line.Count < 2) continue;
                    float h = hdg.height > 0.3f ? Mathf.Min(hdg.height, 2.5f) : 1.0f;
                    AddBoxStrip(line, 0.4f, 0f, h, "grass#hedge", true, 1f); nHedge++;
                }
                catch (Exception e) { Debug.LogWarning($"[CityBuilder] hedge {hdg.id} failed: {e.Message}"); }
            }
            foreach (var wl in D.walls)
            {
                try
                {
                    var line = CleanLine(wl.pts); if (line.Count < 2) continue;
                    float h = wl.height > 0.2f ? wl.height : 1.2f;
                    AddBoxStrip(line, 0.15f, 0f, h, "concrete_rough", true, 1f); nWall++;
                }
                catch (Exception e) { Debug.LogWarning($"[CityBuilder] wall {wl.id} failed: {e.Message}"); }
            }
            Count("barrierKerbs", nKerb); Count("fences", nFence); Count("fencePosts", nPosts); Count("hedges", nHedge); Count("walls", nWall);
        }
    }
}
