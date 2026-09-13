using System.Collections.Generic;
using UnityEngine;

namespace AmirTemur.Editor
{
    /// <summary>Hotel Uzbekistan (1974, brutalist, 71.5 m): a bent slab following the OSM footprint. The concave west (square-facing)
    /// edges carry the precast concrete "panjara" sun-screen lattice in front of a dark recessed window wall; the east edges get
    /// recessed strip windows in a holed wall; the short ends stay blank concrete. 2-storey podium with recessed glass ground floor,
    /// entrance canopy, roof plant block and rooftop sign.</summary>
    public static class HotelUzbekistan
    {
        const float PodiumH = 8.0f, GroundH = 4.6f, RowH = 3.3f; const int Rows = 17;
        const float CellW = 1.8f, FrameT = 0.25f, FrameD = 0.8f, BandH = 0.35f;

        public static void Build(CityData city, Transform parent)
        {
            var ring = Landmarks.Footprint(city, "hotel_uzbekistan", out var lm);
            if (ring == null) throw new System.Exception("hotel_uzbekistan footprint missing in city.json");
            int n = ring.Count;
            float latticeTop = PodiumH + Rows * RowH;      // 64.1
            float roofY = latticeTop + 1.9f;               // 66.0
            float totalH = lm != null && lm.height > 60f ? lm.height : 71.5f;

            var go = new GameObject("HotelUzbekistan"); go.transform.SetParent(parent, false);

            // classify edges by outward normal
            var west = new bool[n]; var east = new bool[n]; var nrm = new Vector2[n]; var dir = new Vector2[n]; var len = new float[n];
            for (int i = 0; i < n; i++)
            {
                var a = ring[i]; var b = ring[(i + 1) % n]; var d = b - a; len[i] = d.magnitude; dir[i] = d / Mathf.Max(1e-5f, len[i]);
                nrm[i] = new Vector2(dir[i].y, -dir[i].x);
                west[i] = nrm[i].x < -0.5f; east[i] = nrm[i].x > 0.5f;
            }
            var inset = MeshUtil.OffsetRing(ring, -2.0f);

            // ------------------------------------------------------------ body walls, roof, cornices
            var body = new Part();
            {
                int concrete = body.Sub("concrete_panel"), smooth = body.Sub("concrete_smooth"), roof = body.Sub("roof_flat"), glassD = body.Sub("glass_dark"), dark = body.Sub("metal_dark");
                for (int i = 0; i < n; i++)
                {
                    var a = ring[i]; var b = ring[(i + 1) % n]; var n3 = new Vector3(nrm[i].x, 0, nrm[i].y);
                    if (east[i]) BuildEastWall(body, Frame.Facade(a, b), len[i], PodiumH, roofY);
                    else LK.WallSeg(body.B, concrete, a, b, PodiumH, roofY, n3);
                    // podium
                    if (west[i])
                    {
                        var f = Frame.Facade(a, b);
                        LK.WallSeg(body.B, body.Sub("facade_glass"), inset[i], inset[(i + 1) % n], 0, GroundH, n3);
                        LK.QuadN(body.B, smooth, new Vector3(a.x, GroundH, a.y), new Vector3(b.x, GroundH, b.y), new Vector3(inset[(i + 1) % n].x, GroundH, inset[(i + 1) % n].y), new Vector3(inset[i].x, GroundH, inset[i].y),
                            Vector3.down, new Vector2(0, 0), new Vector2(len[i], 0), new Vector2(len[i], 2), new Vector2(0, 2));
                        LK.WallSeg(body.B, smooth, a, b, GroundH, PodiumH, n3);
                        if (len[i] > 4f) LK.FWall(body.B, glassD, f, 0.8f, len[i] - 0.8f, GroundH + 0.9f, PodiumH - 0.8f, 0.03f);
                        // round podium columns on the footprint line
                        int cols = Mathf.Max(1, Mathf.RoundToInt(len[i] / 6f));
                        for (int j = 0; j < cols; j++)
                        {
                            var p = a + dir[i] * (len[i] * j / cols) - nrm[i] * 0.45f;
                            LK.Cylinder(body.B, smooth, new Vector3(p.x, 0, p.y), 0.38f, 0.38f, GroundH, 16, false, false);
                        }
                        bool prevWest = west[(i + n - 1) % n], nextWest = west[(i + 1) % n];
                        if (!prevWest) LK.WallSeg(body.B, smooth, inset[i], a, 0, GroundH, new Vector3(dir[i].x, 0, dir[i].y));
                        if (!nextWest) LK.WallSeg(body.B, smooth, b, inset[(i + 1) % n], 0, GroundH, new Vector3(-dir[i].x, 0, -dir[i].y));
                    }
                    else
                    {
                        LK.WallSeg(body.B, smooth, a, b, 0, PodiumH, n3);
                        if (east[i] && len[i] > 4f)
                        {
                            var f = Frame.Facade(a, b);
                            LK.FWall(body.B, glassD, f, 0.8f, len[i] - 0.8f, 1.0f, GroundH - 0.6f, 0.03f);
                            LK.FWall(body.B, glassD, f, 0.8f, len[i] - 0.8f, GroundH + 0.9f, PodiumH - 0.8f, 0.03f);
                        }
                    }
                }
                LK.Ledge(body.B, smooth, ring, 0.4f, PodiumH - 0.35f, PodiumH + 0.05f, false);   // podium slab edge
                LK.Ledge(body.B, concrete, ring, 0.5f, latticeTop, latticeTop + 0.5f, false);      // cornice over the lattice
                LK.Ledge(body.B, concrete, ring, 0.3f, roofY - 0.3f, roofY + 0.9f, false);         // parapet
                body.B.AddPolygon(roof, ring, null, roofY, 1f);

                // roof plant block + rooftop sign (oriented along the slab's longest edge)
                var c2 = MeshUtil.Centroid(ring);
                var fr = Frame.Along(new Vector3(c2.x, roofY, c2.y), LK.PrincipalAngle(ring) + 90f);
                // PrincipalAngle is reduced to (-45,45]; the slab's long axis is ~75 deg so add 90 to align u with it
                LK.FBox(body.B, smooth, fr, new Vector3(0, 1.25f, 0), new Vector3(30f, 2.5f, 9f));
                float signH = Mathf.Max(2.5f, totalH - roofY - 2.5f);
                LK.FBox(body.B, dark, fr, new Vector3(0, 2.5f + signH * 0.5f, 0), new Vector3(32f, signH, 0.5f));
                for (int j = -3; j <= 3; j++) LK.FBox(body.B, dark, fr, new Vector3(j * 5f, 2.5f + signH * 0.5f, 0), new Vector3(0.3f, signH, 1.2f));
                int lit = body.Sub("clock_face");
                LK.FBox(body.B, lit, fr, new Vector3(0, 2.5f + signH * 0.5f, 0), new Vector3(30f, signH - 0.8f, 0.62f));
                body.Flush("Hotel_Body", go.transform);
            }

            // ------------------------------------------------------------ panjara lattice on the west edges
            var lat = new Part(); int chunk = 0;
            for (int i = 0; i < n; i++)
            {
                if (!west[i]) continue;
                var f = Frame.Facade(ring[i], ring[(i + 1) % n]);
                bool first = !west[(i + n - 1) % n];
                BuildLattice(lat, f, len[i], first);
                lat.FlushIfLarge("Hotel_Lattice", ref chunk, go.transform, 100000);
            }
            lat.Flush($"Hotel_Lattice_{chunk}", go.transform);

            // ------------------------------------------------------------ entrance canopy at the middle of the west chain
            {
                float total = 0; for (int i = 0; i < n; i++) if (west[i]) total += len[i];
                float half = total * 0.5f, acc = 0; Frame fc = Frame.Along(Vector3.zero, 0); bool found = false;
                for (int i = 0; i < n && !found; i++)
                {
                    if (!west[i]) continue;
                    if (acc + len[i] >= half) { var p = ring[i] + dir[i] * (half - acc); fc = Frame.Facade(p, p + dir[i]); found = true; }
                    acc += len[i];
                }
                if (found)
                {
                    var can = new Part(); int smooth = can.Sub("concrete_smooth"), dark = can.Sub("metal_dark"), glassD = can.Sub("glass_dark"), gran = can.Sub("paving_granite");
                    LK.FBox(can.B, smooth, fc, new Vector3(0, 4.15f, 3.4f), new Vector3(26f, 0.45f, 8.8f));
                    LK.FBox(can.B, dark, fc, new Vector3(0, 4.15f, 7.85f), new Vector3(26f, 0.75f, 0.14f));
                    for (int j = -1; j <= 1; j += 2)
                    {
                        LK.Cylinder(can.B, smooth, fc.P(j * 5.5f, 0, 6.6f), 0.32f, 0.32f, 3.95f, 16, false, false);
                        LK.Cylinder(can.B, smooth, fc.P(j * 11.5f, 0, 6.6f), 0.32f, 0.32f, 3.95f, 16, false, false);
                    }
                    // entrance doors (dark glass with frames) on the recessed glass line, and a low granite step
                    LK.FBox(can.B, dark, fc, new Vector3(0, 1.6f, -1.9f), new Vector3(9.2f, 3.2f, 0.16f));
                    LK.FBox(can.B, glassD, fc, new Vector3(0, 1.55f, -1.86f), new Vector3(8.6f, 2.9f, 0.1f));
                    LK.FBox(can.B, gran, fc, new Vector3(0, 0.08f, 0.6f), new Vector3(14f, 0.16f, 5.4f));
                    can.Flush("Hotel_Canopy", go.transform);
                }
            }
        }

        /// <summary>Panjara: continuous vertical mullions and horizontal bands (precast frames 0.25 m thick, 0.8 m deep) forming
        /// cells ~1.8 x 3.3 m; each cell has splayed reveals leading to a smaller dark window on the recessed wall.</summary>
        static void BuildLattice(Part part, Frame f, float len, bool firstOfChain)
        {
            var b = part.B; int frame = part.Sub("concrete_panel"), splay = part.Sub("concrete_smooth"), glass = part.Sub("glass_dark"), dark = part.Sub("metal_dark");
            int cols = Mathf.Max(1, Mathf.RoundToInt(len / CellW)); float cw = len / cols;
            float H = Rows * RowH;
            for (int k = firstOfChain ? 0 : 1; k <= cols; k++)
                LK.FBox(b, frame, f, new Vector3(k * cw, PodiumH + (H + BandH) * 0.5f, FrameD * 0.5f), new Vector3(FrameT, H + BandH, FrameD));
            for (int r = 0; r <= Rows; r++)
                LK.FBox(b, frame, f, new Vector3(len * 0.5f, PodiumH + r * RowH + BandH * 0.5f, FrameD * 0.5f), new Vector3(len, BandH, FrameD));
            for (int r = 0; r < Rows; r++)
            {
                float y0 = PodiumH + r * RowH + BandH, y1 = PodiumH + (r + 1) * RowH;
                float yi0 = y0 + 0.8f, yi1 = y1 - 0.25f;
                for (int k = 0; k < cols; k++)
                {
                    float x0 = k * cw + FrameT * 0.5f, x1 = (k + 1) * cw - FrameT * 0.5f;
                    float xi0 = x0 + 0.28f, xi1 = x1 - 0.28f;
                    if (xi1 - xi0 < 0.4f) { xi0 = x0 + 0.1f; xi1 = x1 - 0.1f; }
                    // splayed reveals from the frame front opening to the wall opening
                    LK.QuadN(b, splay, f.P(x0, y0, FrameD), f.P(x1, y0, FrameD), f.P(xi1, yi0, 0), f.P(xi0, yi0, 0), Vector3.up, new Vector2(x0, 0), new Vector2(x1, 0), new Vector2(xi1, 1), new Vector2(xi0, 1));
                    LK.QuadN(b, splay, f.P(x0, y1, FrameD), f.P(x1, y1, FrameD), f.P(xi1, yi1, 0), f.P(xi0, yi1, 0), Vector3.down, new Vector2(x0, 0), new Vector2(x1, 0), new Vector2(xi1, 1), new Vector2(xi0, 1));
                    LK.QuadN(b, splay, f.P(x0, y0, FrameD), f.P(x0, y1, FrameD), f.P(xi0, yi1, 0), f.P(xi0, yi0, 0), f.u, new Vector2(0, y0), new Vector2(0, y1), new Vector2(1, yi1), new Vector2(1, yi0));
                    LK.QuadN(b, splay, f.P(x1, y0, FrameD), f.P(x1, y1, FrameD), f.P(xi1, yi1, 0), f.P(xi1, yi0, 0), -f.u, new Vector2(0, y0), new Vector2(0, y1), new Vector2(1, yi1), new Vector2(1, yi0));
                    // window on the wall with a dark transom
                    LK.FWall(b, glass, f, xi0, xi1, yi0, yi1, 0.03f);
                    LK.FBox(b, dark, f, new Vector3((xi0 + xi1) * 0.5f, yi0 + 0.9f, 0.05f), new Vector3(xi1 - xi0, 0.08f, 0.08f));
                }
            }
        }

        /// <summary>East wall between y0 and y1 with real window holes (band-decomposed wall) and recessed windows in them.</summary>
        static void BuildEastWall(Part part, Frame f, float len, float y0, float y1)
        {
            var b = part.B; int wall = part.Sub("concrete_panel"), frame = part.Sub("metal_dark"), glass = part.Sub("glass_dark"), reveal = part.Sub("concrete_smooth");
            var wins = new List<Vector4>();
            float spacing = 3.6f, ww = 2.4f;
            int cols = Mathf.FloorToInt((len - 1.2f) / spacing);
            float margin = (len - cols * spacing) * 0.5f;
            if (cols >= 1)
                for (int r = 0; r < Rows; r++)
                {
                    float wy0 = PodiumH + r * RowH + 0.95f, wy1 = PodiumH + (r + 1) * RowH - 0.4f;
                    for (int k = 0; k < cols; k++)
                    {
                        float wx0 = margin + k * spacing + (spacing - ww) * 0.5f, wx1 = wx0 + ww;
                        wins.Add(new Vector4(wx0, wx1, wy0, wy1));
                    }
                }
            LK.HoledWall(b, wall, f, len, y0, y1, wins);
            foreach (var w in wins)
            {
                LK.RecessedWindow(b, frame, glass, reveal, f, w.x, w.y, w.z, w.w, 0, 0.35f, 0.1f, 0.04f);
                LK.FBox(b, reveal, f, new Vector3((w.x + w.y) * 0.5f, w.z - 0.12f, 0.12f), new Vector3(w.y - w.x + 0.5f, 0.12f, 0.3f)); // sill
            }
        }
    }
}
