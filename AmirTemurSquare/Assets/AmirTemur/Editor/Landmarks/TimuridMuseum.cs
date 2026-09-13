using System.Collections.Generic;
using UnityEngine;

namespace AmirTemur.Editor
{
    /// <summary>State Museum of Timurid History (1996): circular white-marble building on a 3-step stylobate (OSM ring r~36.7 m),
    /// 2-storey drum body (r~27 m) with recessed windows and a blue tile band, a ring of 24 slender round columns (r~28.9 m, from the
    /// OSM column parts) carrying a ring entablature, a tiled drum and the big sky-blue ribbed dome (apex ~30 m), a 3-arch entrance
    /// portico on the NW (where the OSM colonnade has its gap). OSM height (7.8) is ignored on purpose.</summary>
    public static class TimuridMuseum
    {
        const float StyloTop = 0.9f, BodyTop = 12.6f, ColTop = 10.9f;

        public static void Build(CityData city, Transform parent)
        {
            var ring = Landmarks.Footprint(city, "timurid_museum", out var lm);
            if (ring == null) throw new System.Exception("timurid_museum footprint missing in city.json");
            var c2 = MeshUtil.Centroid(ring);
            float rStylo = LK.MeanRadius(ring, c2);                    // ~36.7
            float rBody = 26.8f, rCols = 28.9f;
            var bodyPart = city.FindBuilding(1348266536); if (bodyPart != null && bodyPart.outer.Count > 6) rBody = Mathf.Clamp(LK.MeanRadius(bodyPart.outer, c2), 20f, rStylo - 6f);
            // column ring radius + entrance azimuth from the OSM column parts (2 m circles around the centre)
            var colAngles = new List<float>(); float rSum = 0;
            foreach (var b in city.buildings)
            {
                if (!b.part || b.outer.Count < 8) continue;
                var bc = MeshUtil.Centroid(b.outer); float d = (bc - c2).magnitude;
                if (d < 24f || d > 34f || LK.MeanRadius(b.outer, bc) > 2.5f) continue;
                colAngles.Add(Mathf.Atan2(bc.y - c2.y, bc.x - c2.x) * Mathf.Rad2Deg); rSum += d;
            }
            if (colAngles.Count >= 4) rCols = Mathf.Clamp(rSum / colAngles.Count, rBody + 1.5f, rStylo - 3f);
            float entranceAz = 130f; // NW, towards the square's north-west walk
            if (colAngles.Count >= 4)
            {
                // largest angular gap between mapped columns = the portico
                colAngles.Sort(); float bestGap = -1;
                for (int i = 0; i < colAngles.Count; i++)
                {
                    float a0 = colAngles[i], a1 = colAngles[(i + 1) % colAngles.Count]; if (i == colAngles.Count - 1) a1 += 360f;
                    if (a1 - a0 > bestGap) { bestGap = a1 - a0; entranceAz = (a0 + a1) * 0.5f; }
                }
            }
            var c = new Vector3(c2.x, 0, c2.y);
            var go = new GameObject("TimuridMuseum"); go.transform.SetParent(parent, false);

            // ------------------------------------------------------------ stylobate + body + colonnade
            var main = new Part();
            {
                int marble = main.Sub("marble_white"), dark = main.Sub("marble_dark"), tiles = main.Sub("tiles_blue"), glassD = main.Sub("glass_dark"), metal = main.Sub("metal_dark");
                LK.RoundSteps(main.B, marble, dark, c, rStylo - 0.9f, 3, StyloTop / 3f, 0.45f, 160);
                var cb = c + Vector3.up * StyloTop;
                LK.Cylinder(main.B, marble, cb, rBody, rBody, BodyTop - StyloTop, 128, false, false);
                // blue tile frieze near the top of the body and a plinth band
                LK.Cylinder(main.B, tiles, c + Vector3.up * (ColTop - 1.5f), rBody + 0.08f, rBody + 0.08f, 1.2f, 128, true, true);
                LK.Cylinder(main.B, dark, cb, rBody + 0.12f, rBody + 0.12f, 0.6f, 128, true, false);

                int nCols = 24; float phase = entranceAz + 360f / nCols * 0.5f;
                for (int k = 0; k < nCols; k++)
                {
                    float az = phase + k * 360f / nCols; float rel = Mathf.DeltaAngle(az, entranceAz);
                    var rv = new Vector3(Mathf.Cos(az * Mathf.Deg2Rad), 0, Mathf.Sin(az * Mathf.Deg2Rad));
                    if (Mathf.Abs(rel) > 12f) LK.Column(main.B, marble, cb + rv * rCols, 0.55f, ColTop - StyloTop, 24, 0.5f, 0.6f);
                    // window bay between columns (skip the portico)
                    float bayAz = az + 360f / nCols * 0.5f; float bayRel = Mathf.DeltaAngle(bayAz, entranceAz);
                    if (Mathf.Abs(bayRel) < 20f) continue;
                    var ft = Frame.Along(c + new Vector3(Mathf.Cos(bayAz * Mathf.Deg2Rad), 0, Mathf.Sin(bayAz * Mathf.Deg2Rad)) * rBody, bayAz - 90f);
                    // ground floor tall window + upper window, both with marble frames protruding from the curved wall
                    Window(main.B, marble, glassD, metal, ft, -1.5f, 1.5f, StyloTop + 1.2f, StyloTop + 5.4f);
                    Window(main.B, marble, glassD, metal, ft, -1.2f, 1.2f, StyloTop + 6.6f, StyloTop + 8.6f);
                }
                // ring entablature over the columns and cornice (fills the roof)
                var inner = LK.Circle(c2, rBody - 0.2f, 128); var outer = LK.Circle(c2, rCols + 1.1f, 128);
                LK.Annulus(main.B, marble, inner, outer, ColTop, BodyTop - 0.5f);
                LK.Ledge(main.B, marble, outer, 0.5f, BodyTop - 0.5f, BodyTop, true, 0.5f);
                LK.Cylinder(main.B, tiles, c + Vector3.up * (ColTop + 0.3f), rCols + 1.16f, rCols + 1.16f, 0.9f, 128, false, false);
                main.Flush("Museum_Main", go.transform);
            }

            // ------------------------------------------------------------ portico with 3 pointed arches
            var port = new Part();
            {
                int marble = port.Sub("marble_white"), tiles = port.Sub("tiles_blue"), glassD = port.Sub("glass_dark"), metal = port.Sub("metal_dark");
                var fp = Frame.Along(c + new Vector3(Mathf.Cos(entranceAz * Mathf.Deg2Rad), 0, Mathf.Sin(entranceAz * Mathf.Deg2Rad)) * rBody + Vector3.up * StyloTop, entranceAz - 90f);
                float w = 15.1f, hgt = ColTop - StyloTop, depth = 6.5f;
                var openings = new List<Vector3> { new Vector3(1.3f, 4.6f, 5.4f), new Vector3(5.9f, 9.2f, 5.4f), new Vector3(10.5f, 13.8f, 5.4f) };
                var prof = LK.ArchBayProfile(w, hgt, openings, true, 16);
                LK.Prism(port.B, marble, fp.Shift(-w * 0.5f, 0, 0), prof, depth - 1.3f, depth);
                // side walls of the portico with a pointed arch each
                var sideProf = LK.ArchBayProfile(depth - 1.3f, hgt, new List<Vector3> { new Vector3(0.9f, depth - 2.2f, 5.4f) }, true, 16);
                var fl = new Frame(fp.P(-w * 0.5f, 0, 0), fp.v, -fp.u);   // left side: u outward along v, v facing -u
                var fr = new Frame(fp.P(w * 0.5f, 0, 0), fp.v, fp.u);
                LK.Prism(port.B, marble, fl, sideProf, 0, 0.9f);
                LK.Prism(port.B, marble, fr, sideProf, 0, 0.9f);
                // roof slab, tile frieze, floor
                LK.FBox(port.B, marble, fp, new Vector3(0, hgt + 0.55f, depth * 0.5f), new Vector3(w + 2.2f, 1.1f, depth + 0.8f));
                LK.FBox(port.B, tiles, fp, new Vector3(0, hgt - 0.75f, depth + 0.06f), new Vector3(w, 1.3f, 0.12f));
                LK.FBox(port.B, marble, fp, new Vector3(0, 0.08f, depth * 0.5f), new Vector3(w, 0.16f, depth));
                // entrance doors in the body wall (pointed arch portal with dark glazing)
                var portal = LK.ArchBayProfile(9f, 8.5f, new List<Vector3> { new Vector3(1.5f, 7.5f, 4.2f) }, true, 16);
                LK.Prism(port.B, tiles, fp.Shift(-4.5f, 0, 0), portal, -0.05f, 0.45f);
                LK.FBox(port.B, glassD, fp, new Vector3(0, 3.0f, 0.12f), new Vector3(5.6f, 6.0f, 0.1f));
                LK.FBox(port.B, metal, fp, new Vector3(0, 3.0f, 0.15f), new Vector3(0.12f, 6.0f, 0.12f));
                LK.FBox(port.B, metal, fp, new Vector3(0, 2.6f, 0.15f), new Vector3(5.6f, 0.12f, 0.12f));
                port.Flush("Museum_Portico", go.transform);
            }

            // ------------------------------------------------------------ drum + ribbed dome
            var dome = new Part();
            {
                int marble = dome.Sub("marble_white"), tiles = dome.Sub("tiles_blue"), gold = dome.Sub("metal_gold");
                float rDrum = 20f, drumH = 4.2f; var cd = c + Vector3.up * BodyTop;
                LK.Cylinder(dome.B, marble, cd, rDrum, rDrum, drumH, 128, false, false);
                int pil = 32;
                for (int k = 0; k < pil; k++)
                {
                    float a = k / (float)pil * Mathf.PI * 2; var rv = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)); var tv = new Vector3(-Mathf.Sin(a), 0, Mathf.Cos(a));
                    LK.Box(dome.B, marble, cd + rv * (rDrum + 0.12f) + Vector3.up * (drumH * 0.5f), tv, rv, new Vector3(0.9f, drumH, 0.4f));
                }
                LK.Cylinder(dome.B, tiles, cd + Vector3.up * 1.6f, rDrum + 0.2f, rDrum + 0.2f, 1.6f, 128, true, true);
                LK.Ledge(dome.B, marble, LK.Circle(c2, rDrum, 128), 0.6f, BodyTop + drumH, BodyTop + drumH + 0.6f, true, 0.5f);
                float domeBase = BodyTop + drumH + 0.6f, domeR = rDrum - 0.4f, domeH = 30.2f - domeBase, bulge = 0.12f;
                var cb = c + Vector3.up * domeBase;
                LK.Dome(dome.B, tiles, cb, domeR, domeH, 128, 28, bulge);
                LK.DomeRibs(dome.B, tiles, cb, domeR, domeH, bulge, 40, 0.55f, 0.3f, 28);
                // tile band at the dome foot and a gold finial
                LK.Cylinder(dome.B, tiles, cb, domeR + 0.35f, domeR + 0.05f, 1.0f, 128, false, false);
                var apex = cb + Vector3.up * domeH;
                LK.Cylinder(dome.B, gold, apex - Vector3.up * 0.4f, 1.3f, 1.0f, 0.9f, 24, true, false);
                LK.Sphere(dome.B, gold, apex + Vector3.up * 1.2f, 0.8f, 20, 12);
                LK.Cone(dome.B, gold, apex + Vector3.up * 1.8f, 0.3f, 0f, 2.6f, 16);
                dome.Flush("Museum_Dome", go.transform);
            }
        }

        /// <summary>Window on the curved body: dark glass slightly off the wall, marble surround protruding 0.18 m, thin metal mullions.</summary>
        static void Window(MeshUtil.Builder b, int marble, int glass, int metal, Frame ft, float x0, float x1, float y0, float y1)
        {
            LK.FWall(b, glass, ft, x0, x1, y0, y1, 0.08f);
            float fw = 0.28f, d = 0.2f;
            LK.FBox(b, marble, ft, new Vector3((x0 + x1) * 0.5f, y0 - fw * 0.5f, d * 0.5f), new Vector3(x1 - x0 + 2 * fw, fw, d));
            LK.FBox(b, marble, ft, new Vector3((x0 + x1) * 0.5f, y1 + fw * 0.5f, d * 0.5f), new Vector3(x1 - x0 + 2 * fw, fw, d));
            LK.FBox(b, marble, ft, new Vector3(x0 - fw * 0.5f, (y0 + y1) * 0.5f, d * 0.5f), new Vector3(fw, y1 - y0, d));
            LK.FBox(b, marble, ft, new Vector3(x1 + fw * 0.5f, (y0 + y1) * 0.5f, d * 0.5f), new Vector3(fw, y1 - y0, d));
            LK.FBox(b, metal, ft, new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, 0.1f), new Vector3(0.08f, y1 - y0, 0.06f));
            LK.FBox(b, metal, ft, new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, 0.1f), new Vector3(x1 - x0, 0.08f, 0.06f));
        }
    }
}
