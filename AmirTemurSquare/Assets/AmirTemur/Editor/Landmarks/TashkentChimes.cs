using System.Collections.Generic;
using UnityEngine;

namespace AmirTemur.Editor
{
    /// <summary>Tashkent Chimes: two ~31 m square clock towers (1947 original and its 2009 twin) in beige stone: stepped plinth,
    /// base block, shaft with recessed ornamented panels and an arched doorway, clock stage with a 3 m clock on each side,
    /// open 4-arch belfry with a bell, and a small ribbed dome with a gold finial. Positions come from the 'tower' clock POIs
    /// (fallback: landmark centre and a twin 25 m east); orientation from the OSM tower building parts.</summary>
    public static class TashkentChimes
    {
        const float Side = 5.6f;

        public static void Build(CityData city, Transform parent)
        {
            var positions = new List<Vector2>();
            foreach (var p in city.pois)
            {
                bool clockTower = p.kind == "tower" || p.kind == "clock";
                if (!clockTower && p.tags != null && p.tags.TryGetValue("man_made", out var mm) && mm == "tower" && p.tags.TryGetValue("amenity", out var am) && am == "clock") clockTower = true;
                if (clockTower) positions.Add(p.pos);
            }
            if (positions.Count == 0 && city.landmarks.TryGetValue("chimes", out var lm)) { positions.Add(lm.center); positions.Add(lm.center + new Vector2(25f, 0)); }
            if (positions.Count == 0) { positions.Add(new Vector2(0f, -172f)); positions.Add(new Vector2(87f, -167f)); }

            var go = new GameObject("TashkentChimes"); go.transform.SetParent(parent, false);
            int idx = 0;
            foreach (var pos in positions)
            {
                var centre = pos; float angle = 0f;
                CityData.Building best = null; float bestD = 6f;
                foreach (var b in city.buildings)
                {
                    if (!b.part || b.height < 12f || b.outer.Count < 4) continue;
                    var bc = MeshUtil.Centroid(b.outer); float d = (bc - pos).magnitude;
                    if (d < bestD) { bestD = d; best = b; }
                }
                if (best != null) { centre = MeshUtil.Centroid(best.outer); angle = LK.PrincipalAngle(best.outer); }
                BuildTower(go.transform, new Vector3(centre.x, 0, centre.y), angle, idx++);
            }
        }

        static void BuildTower(Transform parent, Vector3 c, float angleDeg, int index)
        {
            var part = new Part(); var b = part.B;
            int stone = part.Sub("travertine"), dark = part.Sub("marble_dark"), tiles = part.Sub("tiles_blue"), metal = part.Sub("metal_dark"),
                gold = part.Sub("metal_gold"), face = part.Sub("clock_face"), bronze = part.Sub("bronze_statue"), glassD = part.Sub("glass_dark");
            var f = Frame.Along(c, angleDeg);
            float h = Side * 0.5f;

            // which face looks towards the square (north / the monument)? doorway goes there
            int doorFace = 0; float bestDot = float.MinValue;
            for (int k = 0; k < 4; k++) { var ff = LK.SquareFace(f, k, h, 0); float d = Vector3.Dot(ff.v, (Vector3.zero - c).normalized); if (d > bestDot) { bestDot = d; doorFace = k; } }

            // ---- plinth: 3 steps + base block
            LK.FBox(b, dark, f, new Vector3(0, 0.2f, 0), new Vector3(8.2f, 0.4f, 8.2f));
            LK.FBox(b, dark, f, new Vector3(0, 0.6f, 0), new Vector3(7.5f, 0.4f, 7.5f));
            LK.FBox(b, dark, f, new Vector3(0, 1.0f, 0), new Vector3(6.9f, 0.4f, 6.9f));
            float y = 1.2f;
            LK.FBox(b, dark, f, new Vector3(0, y + 1.0f, 0), new Vector3(6.4f, 2.0f, 6.4f));
            LK.FBox(b, stone, f, new Vector3(0, y + 2.2f, 0), new Vector3(6.7f, 0.4f, 6.7f)); // torus moulding
            y += 2.4f;

            // ---- shaft with recessed panels, ornament bands and a doorway
            float shaftTop = 16.0f;
            for (int k = 0; k < 4; k++)
            {
                var ff = LK.SquareFace(f, k, h, 0);
                var holes = new List<Vector4> { new Vector4(1.1f, Side - 1.1f, y + 1.2f, shaftTop - 1.4f) };
                LK.HoledWall(b, stone, ff, Side, y, shaftTop, holes);
                var w = holes[0];
                LK.RecessedWindow(b, stone, stone, dark, ff, w.x, w.y, w.z, w.w, 0, 0.3f, 0.16f, 0.06f);
                // ornament bands inside the panel (blue tile) and a small sill ledge
                LK.FBox(b, tiles, ff, new Vector3(Side * 0.5f, w.z + 3.3f, -0.22f), new Vector3(w.y - w.x - 0.1f, 0.6f, 0.14f));
                LK.FBox(b, tiles, ff, new Vector3(Side * 0.5f, w.w - 1.5f, -0.22f), new Vector3(w.y - w.x - 0.1f, 0.6f, 0.14f));
                LK.FBox(b, tiles, ff, new Vector3(Side * 0.5f, (w.z + w.w) * 0.5f, -0.22f), new Vector3(1.0f, 3.2f, 0.14f));
                if (k == doorFace)
                {
                    var door = LK.ArchBayProfile(3.0f, 4.6f, new List<Vector3> { new Vector3(0.5f, 2.5f, 2.4f) }, true, 12);
                    LK.Prism(b, dark, ff.Shift(Side * 0.5f - 1.5f, y, 0), door, -0.32f, 0.28f);
                    LK.FBox(b, metal, ff, new Vector3(Side * 0.5f, y + 1.4f, -0.05f), new Vector3(1.9f, 2.8f, 0.1f));
                    LK.FBox(b, glassD, ff, new Vector3(Side * 0.5f, y + 3.1f, -0.05f), new Vector3(1.9f, 0.8f, 0.08f));
                }
                else
                {
                    LK.FBox(b, metal, ff, new Vector3(Side * 0.5f, w.z + 1.4f, -0.31f), new Vector3(1.3f, 2.0f, 0.06f)); // dark slit window
                }
            }
            var shaftRing = LK.Rect(f, -h, h, -h, h);
            LK.Ledge(b, stone, shaftRing, 0.55f, shaftTop - 0.9f, shaftTop, true);
            LK.Ledge(b, stone, shaftRing, 0.3f, shaftTop - 1.3f, shaftTop - 0.9f, false);

            // ---- clock stage
            float clockY0 = shaftTop, clockY1 = shaftTop + 5.6f;
            LK.FBox(b, stone, f, new Vector3(0, (clockY0 + clockY1) * 0.5f, 0), new Vector3(Side, clockY1 - clockY0, Side));
            for (int k = 0; k < 4; k++)
            {
                var ff = LK.SquareFace(f, k, h, 0);
                for (int s = 0; s < 2; s++) LK.FBox(b, stone, ff, new Vector3(s == 0 ? 0.45f : Side - 0.45f, (clockY0 + clockY1) * 0.5f, 0.12f), new Vector3(0.8f, clockY1 - clockY0, 0.3f));
                var cc = ff.P(Side * 0.5f, clockY0 + 2.8f, 0.05f);
                LK.DiscN(b, face, cc, ff.v, 1.5f, 48);
                // rim, hour marks and hands (about 10:10)
                LK.Lathe(b, metal, cc - ff.v * 0.02f, ff.v, new List<Vector2> { new Vector2(1.62f, 0), new Vector2(1.62f, 0.14f), new Vector2(1.48f, 0.14f), new Vector2(1.48f, 0.0f) }, 48);
                for (int m = 0; m < 12; m++)
                {
                    float a = m / 12f * Mathf.PI * 2; var rd = Vector3.up * Mathf.Cos(a) + ff.u * Mathf.Sin(a); var td = Vector3.Cross(ff.v, rd).normalized;
                    bool q = m % 3 == 0;
                    LK.Box3(b, metal, cc + rd * 1.28f + ff.v * 0.05f, rd, td, ff.v, new Vector3(q ? 0.32f : 0.2f, q ? 0.14f : 0.09f, 0.06f));
                }
                Hand(b, metal, cc, ff, 300f, 0.95f, 0.12f, 0.09f);
                Hand(b, metal, cc, ff, 60f, 1.35f, 0.09f, 0.13f);
                LK.Lathe(b, gold, cc + ff.v * 0.08f, ff.v, new List<Vector2> { new Vector2(0.12f, 0), new Vector2(0.12f, 0.1f), new Vector2(0.0f, 0.13f) }, 16);
            }
            LK.Ledge(b, stone, shaftRing, 0.55f, clockY1 - 0.7f, clockY1, true);

            // ---- belfry: corner piers + 4 arched panels, bell inside
            float belY0 = clockY1, belY1 = clockY1 + 5.4f;
            LK.FBox(b, stone, f, new Vector3(0, belY0 + 0.2f, 0), new Vector3(Side + 0.4f, 0.4f, Side + 0.4f));
            for (int k = 0; k < 4; k++)
            {
                var ff = LK.SquareFace(f, k, h, 0);
                var prof = LK.ArchBayProfile(Side, belY1 - belY0, new List<Vector3> { new Vector3(1.2f, Side - 1.2f, belY1 - belY0 - 3.1f) }, false, 16);
                LK.Prism(b, stone, ff.Shift(0, belY0, 0), prof, -0.6f, 0);
                // little balustrade in the opening
                LK.FBox(b, stone, ff, new Vector3(Side * 0.5f, belY0 + 0.75f, -0.3f), new Vector3(Side - 2.4f, 0.12f, 0.2f));
                for (int m = 0; m < 6; m++) LK.FBox(b, stone, ff, new Vector3(1.4f + m * (Side - 2.8f) / 5f, belY0 + 0.45f, -0.3f), new Vector3(0.1f, 0.6f, 0.1f));
            }
            var bell = new List<Vector2> { new Vector2(0.15f, 0), new Vector2(0.55f, 0.05f), new Vector2(0.62f, 0.15f), new Vector2(0.55f, 0.45f), new Vector2(0.42f, 0.9f), new Vector2(0.32f, 1.25f), new Vector2(0.12f, 1.45f), new Vector2(0.0f, 1.5f) };
            LK.Lathe(b, bronze, f.P(0, belY0 + 2.2f, 0), Vector3.up, bell, 24, false, true);
            LK.FBox(b, metal, f, new Vector3(0, belY0 + 3.85f, 0), new Vector3(0.16f, 0.3f, 0.16f));
            LK.FBox(b, metal, f, new Vector3(0, belY1 - 0.55f, 0), new Vector3(Side - 1.6f, 0.2f, 0.2f)); // bell beam
            LK.Ledge(b, stone, shaftRing, 0.6f, belY1 - 0.8f, belY1, true);

            // ---- ribbed dome cap with gold finial
            float domeBase = belY1 + 0.6f;
            LK.FBox(b, stone, f, new Vector3(0, belY1 + 0.3f, 0), new Vector3(4.6f, 0.6f, 4.6f));
            float dr = 2.2f, dh = 2.6f, bulge = 0.18f; var cb = f.P(0, domeBase, 0);
            LK.Cylinder(b, tiles, cb - Vector3.up * 0.3f, dr + 0.15f, dr, 0.3f, 48, false, false);
            LK.Dome(b, tiles, cb, dr, dh, 48, 16, bulge);
            LK.DomeRibs(b, tiles, cb, dr, dh, bulge, 16, 0.22f, 0.1f, 16);
            var apex = cb + Vector3.up * dh;
            LK.Sphere(b, gold, apex + Vector3.up * 0.25f, 0.35f, 16, 10);
            LK.Cone(b, gold, apex + Vector3.up * 0.5f, 0.12f, 0f, 1.8f, 12);
            LK.Sphere(b, gold, apex + Vector3.up * 1.4f, 0.14f, 10, 6);

            part.Flush($"Chimes_Tower_{index}", parent);
        }

        /// <summary>Clock hand at angleDeg clockwise from 12 o'clock (as seen from the front of face frame ff).</summary>
        static void Hand(MeshUtil.Builder b, int sub, Vector3 centre, Frame ff, float angleDeg, float len, float w, float lift)
        {
            float a = angleDeg * Mathf.Deg2Rad;
            var viewerRight = Vector3.Cross(Vector3.up, -ff.v).normalized; // right-hand side for someone looking at the face
            var d = (Vector3.up * Mathf.Cos(a) + viewerRight * Mathf.Sin(a)).normalized;
            var td = Vector3.Cross(ff.v, d).normalized;
            LK.Box3(b, sub, centre + d * (len * 0.5f - 0.15f) + ff.v * lift, d, td, ff.v, new Vector3(len, w, 0.05f));
        }
    }
}
