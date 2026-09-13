using System.Collections.Generic;
using UnityEngine;

namespace AmirTemur.Editor
{
    /// <summary>Palace of International Forums "Uzbekistan" (2009): white marble neoclassical-modern block on the OSM oriented rectangle
    /// (~113 x 87 m, short side facing the square), full-height square colonnade around the perimeter, deep entablature and cornice,
    /// glass curtain front with a tall dark-glass entrance, tall narrow recessed windows on the other sides, broad front stair,
    /// gold shallow dome on a drum (OSM dome part) with ring and spike, flagpoles.</summary>
    public static class ForumPalace
    {
        const float StairRise = 0.18f, StairTread = 0.42f; const int StairSteps = 8;
        const float ColTop = 36.5f, FriezeTop = 38.6f, RoofY = 40.2f;

        public static void Build(CityData city, Transform parent)
        {
            var ring = Landmarks.Footprint(city, "forum_palace", out var lm);
            if (ring == null) throw new System.Exception("forum_palace footprint missing in city.json");
            var f = LK.OrientedRect(ring, out var half);
            // +u must point towards the square (monument at the origin)
            if (Vector3.Dot(f.u, -f.o) < 0) f = new Frame(f.o, -f.u, -f.v);
            float hx = half.x, hz = half.y;               // ~56.7, ~43.3
            float plinth = StairSteps * StairRise;         // 1.44
            float bx = hx - 3.5f, bz = hz - 3.5f;          // body inset behind the colonnade

            var go = new GameObject("ForumPalace"); go.transform.SetParent(parent, false);

            // ------------------------------------------------------------ stylobate + stairs
            var basePart = new Part();
            {
                int dark = basePart.Sub("marble_dark"), marble = basePart.Sub("marble_white"), rail = basePart.Sub("metal_dark");
                var rect = LK.Rect(f, -hx, hx, -hz, hz);
                basePart.B.AddWalls(dark, rect, 0, plinth);
                basePart.B.AddPolygon(marble, rect, null, plinth, 0.5f);
                var ff = new Frame(f.P(hx, 0, 0), f.v, f.u);   // front edge, v outward towards the square
                LK.Stairs(basePart.B, marble, ff, 0, 60f, 0, StairSteps, StairRise, StairTread, 0);
                float depth = StairSteps * StairTread;
                for (int s = -1; s <= 1; s += 2)
                {
                    LK.FBox(basePart.B, dark, ff, new Vector3(s * 30.6f, plinth * 0.5f, depth * 0.5f), new Vector3(1.2f, plinth, depth));
                    LK.FBox(basePart.B, dark, ff, new Vector3(s * 30.6f, plinth + 0.25f, depth * 0.5f), new Vector3(1.3f, 0.5f, depth + 0.3f));
                }
                // intermediate handrails on the stair (sloped bars on posts)
                for (int s = -1; s <= 1; s += 2)
                {
                    var top = ff.P(s * 15f, plinth + 1.0f, 0.2f); var bot = ff.P(s * 15f, 1.0f, depth - 0.2f);
                    LK.Limb(basePart.B, rail, top, bot, 0.06f);
                    for (int k = 0; k <= 4; k++)
                    {
                        float t = k / 4f; var pTop = Vector3.Lerp(top, bot, t); var pBase = pTop - Vector3.up * 1.0f;
                        LK.Limb(basePart.B, rail, pBase, pTop, 0.05f);
                    }
                }
                basePart.Flush("Forum_Base", go.transform);
            }

            // ------------------------------------------------------------ body walls
            var body = new Part();
            {
                int marble = body.Sub("marble_white"), fglass = body.Sub("facade_glass"), glassD = body.Sub("glass_dark"), dark = body.Sub("metal_dark"), roof = body.Sub("roof_flat");
                var bodyRing = LK.Rect(f, -bx, bx, -bz, bz);
                body.B.AddPolygon(roof, bodyRing, null, FriezeTop - 0.5f, 1f);

                // front: glass curtain wall between marble piers, central entrance box
                var ffr = new Frame(f.P(bx, 0, -bz), f.v, f.u);
                float fw = 2 * bz;
                LK.FWall(body.B, fglass, ffr, 0, fw, plinth, ColTop, 0);
                LK.FWall(body.B, marble, ffr, 0, fw, ColTop, FriezeTop, 0.02f);
                int piers = 10;
                for (int k = 0; k <= piers; k++)
                {
                    float x = fw * k / piers; if (k == 5) continue; // keep the entrance bay clear
                    LK.FBox(body.B, marble, ffr, new Vector3(x, (plinth + ColTop) * 0.5f, 0.25f), new Vector3(1.0f, ColTop - plinth, 0.5f));
                }
                LK.FBox(body.B, dark, ffr, new Vector3(bz, plinth + 10.3f, 0.5f), new Vector3(13.2f, 20.6f, 1.0f));
                LK.FBox(body.B, glassD, ffr, new Vector3(bz, plinth + 10.0f, 0.65f), new Vector3(12.0f, 20.0f, 1.3f));
                // horizontal mullions on the entrance glass + hood
                for (int k = 1; k < 5; k++) LK.FBox(body.B, dark, ffr, new Vector3(bz, plinth + k * 4.0f, 1.32f), new Vector3(12.0f, 0.12f, 0.1f));
                LK.FBox(body.B, marble, ffr, new Vector3(bz, plinth + 7.2f, 1.5f), new Vector3(16f, 0.5f, 1.8f));
                for (int s = -1; s <= 1; s += 2) LK.FBox(body.B, marble, ffr, new Vector3(bz + s * 7.6f, plinth + 3.6f, 1.2f), new Vector3(0.8f, 7.2f, 1.2f));

                // sides and back: marble with tall narrow recessed windows every 8 m
                var faces = new[]
                {
                    new Frame(f.P(bx, 0, bz), -f.u, f.v),     // +v side, u runs from x=bx to -bx
                    new Frame(f.P(-bx, 0, -bz), f.u, -f.v),   // -v side
                    new Frame(f.P(-bx, 0, bz), -f.v, -f.u),   // back
                };
                var lens = new[] { 2 * bx, 2 * bx, 2 * bz };
                for (int i = 0; i < faces.Length; i++)
                {
                    var wins = new List<Vector4>();
                    int bays = Mathf.Max(1, Mathf.RoundToInt(lens[i] / 8f)); float bw = lens[i] / bays;
                    for (int k = 0; k < bays; k++) { float xc = (k + 0.5f) * bw; wins.Add(new Vector4(xc - 1.1f, xc + 1.1f, plinth + 3.0f, ColTop - 4.0f)); }
                    LK.HoledWall(body.B, marble, faces[i], lens[i], plinth, FriezeTop, wins);
                    foreach (var w in wins)
                    {
                        LK.RecessedWindow(body.B, dark, glassD, marble, faces[i], w.x, w.y, w.z, w.w, 0, 0.45f, 0.15f, 0.05f);
                        for (int m = 1; m < 8; m++) LK.FBox(body.B, dark, faces[i], new Vector3((w.x + w.y) * 0.5f, w.z + (w.w - w.z) * m / 8f, -0.4f), new Vector3(w.y - w.x, 0.1f, 0.08f));
                    }
                }
                body.Flush("Forum_Body", go.transform);
            }

            // ------------------------------------------------------------ colonnade, entablature, cornice, parapet
            var colon = new Part();
            {
                int marble = colon.Sub("marble_white");
                float cx = hx - 1.0f, cz = hz - 1.0f;
                int baysZ = Mathf.RoundToInt(2 * cz / 4f); if (baysZ % 2 == 0) baysZ++;   // odd => open bay on the axis for the entrance
                int baysX = Mathf.RoundToInt(2 * cx / 4f);
                var placed = new HashSet<Vector2Int>();
                void Col(float x, float z)
                {
                    var key = new Vector2Int(Mathf.RoundToInt(x * 10), Mathf.RoundToInt(z * 10)); if (!placed.Add(key)) return;
                    LK.SquareColumn(colon.B, marble, f, x, z, plinth, ColTop, 0.9f, 0.7f, 0.9f);
                }
                for (int k = 0; k <= baysZ; k++) { float z = -cz + 2 * cz * k / baysZ; Col(cx, z); Col(-cx, z); }
                for (int k = 0; k <= baysX; k++) { float x = -cx + 2 * cx * k / baysX; Col(x, cz); Col(x, -cz); }

                var bodyRing = LK.Rect(f, -bx, bx, -bz, bz);
                var outerRing = LK.Rect(f, -hx - 0.3f, hx + 0.3f, -hz - 0.3f, hz + 0.3f);
                LK.Annulus(colon.B, marble, bodyRing, outerRing, ColTop, FriezeTop);
                LK.Ledge(colon.B, marble, outerRing, 1.2f, FriezeTop, RoofY, true, 0.5f);
                // dentil course under the cornice
                var dentRing = MeshUtil.OffsetRing(outerRing, 0.6f);
                LK.Ledge(colon.B, marble, dentRing, 0.0f, FriezeTop - 0.6f, FriezeTop, false);
                // parapet
                float px = hx + 1.5f, pz = hz + 1.5f;
                LK.FBox(colon.B, marble, f, new Vector3(0, RoofY + 0.45f, pz - 0.2f), new Vector3(2 * px, 0.9f, 0.4f));
                LK.FBox(colon.B, marble, f, new Vector3(0, RoofY + 0.45f, -pz + 0.2f), new Vector3(2 * px, 0.9f, 0.4f));
                LK.FBox(colon.B, marble, f, new Vector3(px - 0.2f, RoofY + 0.45f, 0), new Vector3(0.4f, 0.9f, 2 * pz));
                LK.FBox(colon.B, marble, f, new Vector3(-px + 0.2f, RoofY + 0.45f, 0), new Vector3(0.4f, 0.9f, 2 * pz));
                colon.Flush("Forum_Colonnade", go.transform);
            }

            // ------------------------------------------------------------ dome on its drum (OSM part 235797552 gives centre & radius)
            var domePart = new Part();
            {
                int marble = domePart.Sub("marble_white"), gold = domePart.Sub("metal_gold");
                Vector2 dc; float rd;
                var dp = city.FindBuilding(235797552);
                if (dp != null && dp.outer.Count >= 6) { dc = MeshUtil.Centroid(dp.outer); rd = LK.MeanRadius(dp.outer, dc); }
                else { var p = f.P(hx * 0.3f, 0, 0); dc = new Vector2(p.x, p.z); rd = 30f; }
                rd = Mathf.Clamp(rd, 18f, Mathf.Min(bx, bz) - 2f);
                var c3 = new Vector3(dc.x, RoofY, dc.y);
                float drumH = 3.6f;
                LK.Cylinder(domePart.B, marble, c3, rd, rd, drumH, 96, false, false);
                int pil = 32;
                for (int k = 0; k < pil; k++)
                {
                    float a = k / (float)pil * Mathf.PI * 2; var rv = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)); var tv = new Vector3(-Mathf.Sin(a), 0, Mathf.Cos(a));
                    LK.Box(domePart.B, marble, c3 + rv * (rd + 0.15f) + Vector3.up * (drumH * 0.5f), tv, rv, new Vector3(1.2f, drumH, 0.5f));
                }
                LK.Ledge(domePart.B, marble, LK.Circle(dc, rd, 96), 0.7f, RoofY + drumH, RoofY + drumH + 0.7f, true);
                float domeBase = RoofY + drumH + 0.7f, domeR = rd - 0.4f, domeH = 6.5f;
                LK.Dome(domePart.B, gold, c3 + Vector3.up * (drumH + 0.7f), domeR, domeH, 96, 22, 0f);
                // ring and spike at the apex
                float ringR = 3.4f; float yRing = domeBase + domeH * Mathf.Sqrt(Mathf.Max(0f, 1f - (ringR / domeR) * (ringR / domeR))) - 0.1f;
                var rc = new Vector3(dc.x, yRing, dc.y);
                LK.Cylinder(domePart.B, gold, rc, ringR, ringR, 1.2f, 48, true, false);
                LK.Cylinder(domePart.B, gold, rc + Vector3.up * 1.2f, 1.2f, 0.9f, 0.6f, 24, true, false);
                LK.Cone(domePart.B, gold, rc + Vector3.up * 1.8f, 0.5f, 0.0f, 3.2f, 16);
                LK.Sphere(domePart.B, gold, rc + Vector3.up * 3.0f, 0.5f, 16, 10);
                domePart.Flush("Forum_Dome", go.transform);
            }

            // ------------------------------------------------------------ flagpoles in front of the stairs
            var poles = new Part();
            {
                int metal = poles.Sub("metal_dark");
                float x = hx + StairSteps * StairTread + 9f;
                for (int k = -3; k <= 3; k++) LK.Flagpole(poles.B, metal, f.P(x, 0, k * 8f), 15f, 0.09f);
                poles.Flush("Forum_Flagpoles", go.transform);
            }
        }
    }
}
