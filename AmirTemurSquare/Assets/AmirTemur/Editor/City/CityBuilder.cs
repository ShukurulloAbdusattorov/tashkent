using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace AmirTemur.Editor
{
    /// <summary>
    /// Generates the OSM city slice (ground, roads, paths, areas, buildings, barriers, props, lights) from city.json.
    /// Entry point: <see cref="Build"/>. Static geometry is combined per 100 m tile (one mesh per tile for collidable
    /// geometry, one for non-collidable such as markings/water/glass), buildings are one mesh + MeshCollider each.
    /// Split over several partial files: Ground, Roads, Areas, Props.
    /// </summary>
    public static partial class CityBuilder
    {
        /// <summary>Building ids that are built by the Landmarks module instead (hotel_uzbekistan, forum_palace, timurid_museum, chimes).</summary>
        public static HashSet<long> SkipBuildingIds = new HashSet<long>();

        public const string GenFolder = "Assets/AmirTemur/Generated/City";
        public const string VariantFolder = "Assets/AmirTemur/Art/Materials/Variants";
        public const float TileSize = 100f;
        public const int MaxVertsPerMesh = 60000;

        // road/kerb layering (metres)
        const float RoadY = -0.12f;
        const float MarkY = -0.115f;
        const float KerbW = 0.3f;
        const float KerbTopY = 0.01f;
        const float PathTopY = 0.08f;
        const float PathBottomY = -0.02f;
        const float GrassY = 0.06f;
        const float PlazaY = 0.02f;
        const float ParkingY = -0.05f;

        static readonly string[] LandmarkSkipKeys = { "hotel_uzbekistan", "forum_palace", "timurid_museum", "chimes" };

        // ------------------------------------------------------------------ state (per build)
        static CityData D;
        static System.Random Rng;
        static Transform CityRoot, PropsRoot, FxRoot;
        static TileSet Tiles;
        static RoadIndex Roads;
        static PolyIndex BuildingIndex;
        static readonly Dictionary<string, Transform> TileRoots = new Dictionary<string, Transform>();
        static readonly Dictionary<string, Transform> PropTileRoots = new Dictionary<string, Transform>();
        static readonly Dictionary<string, int> Counts = new Dictionary<string, int>();
        static readonly Dictionary<string, Material> VariantMats = new Dictionary<string, Material>();
        static readonly HashSet<string> NoCollideMats = new HashSet<string> { "road_marking_white", "water", "glass" };

        static CityBuilder()
        {
            try { EnsureSkipIds(); } catch (Exception) { /* data may not be present at domain reload */ }
        }

        /// <summary>Fills <see cref="SkipBuildingIds"/> from CityData.landmarks (hotel_uzbekistan, forum_palace, timurid_museum, chimes).</summary>
        public static void EnsureSkipIds()
        {
            var d = CityData.Load();
            foreach (var k in LandmarkSkipKeys)
                if (d.landmarks.TryGetValue(k, out var lm) && lm.id != 0) SkipBuildingIds.Add(lm.id);
        }

        // ------------------------------------------------------------------ entry
        public static GameObject Build(Transform root)
        {
            D = CityData.Load();
            EnsureSkipIds();
            Rng = new System.Random(20240613);
            Counts.Clear(); TileRoots.Clear(); PropTileRoots.Clear(); VariantMats.Clear();

            var city = new GameObject("City");
            city.transform.SetParent(root, false);
            CityRoot = city.transform;
            PropsRoot = new GameObject("Props").transform; PropsRoot.SetParent(CityRoot, false);
            FxRoot = new GameObject("FX").transform; FxRoot.SetParent(CityRoot, false);
            Tiles = new TileSet();

            try
            {
                Stage("Prepare", 0.00f, Prepare);
                Stage("Ground", 0.05f, BuildGround);
                Stage("Roads", 0.15f, BuildRoads);
                Stage("Markings", 0.25f, BuildMarkings);
                Stage("Paths", 0.32f, BuildPaths);
                Stage("Areas", 0.40f, BuildAreas);
                Stage("Barriers", 0.50f, BuildBarriers);
                Stage("Buildings", 0.55f, BuildBuildings);
                Stage("Stops+Subway", 0.68f, BuildStopsAndSubways);
                Stage("Flush tiles", 0.72f, FlushTiles);
                Stage("Props", 0.80f, BuildProps);
                Stage("Lamps", 0.90f, BuildLamps);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.SaveAssets();
            }

            var sb = new System.Text.StringBuilder("[CityBuilder] done. ");
            foreach (var kv in Counts) sb.Append(kv.Key).Append('=').Append(kv.Value).Append(' ');
            Debug.Log(sb.ToString());
            return city;
        }

        static void Stage(string name, float progress, Action a)
        {
            EditorUtility.DisplayProgressBar("CityBuilder", name, progress);
            try { a(); }
            catch (Exception e) { Debug.LogError($"[CityBuilder] stage '{name}' failed: {e}"); }
        }

        static void Count(string key, int n = 1) { Counts.TryGetValue(key, out var c); Counts[key] = c + n; }

        static void Prepare()
        {
            // clean output folder
            if (AssetDatabase.IsValidFolder(GenFolder)) AssetDatabase.DeleteAsset(GenFolder);
            MeshUtil.EnsureFolder(GenFolder);
            MeshUtil.EnsureFolder(GenFolder + "/Tiles");
            MeshUtil.EnsureFolder(GenFolder + "/Buildings");
            MeshUtil.EnsureFolder(VariantFolder);

            // Extra material keys (public Definitions dictionary; MaterialLibrary itself is untouched).
            if (!MaterialLibrary.Definitions.ContainsKey("hedge"))
                MaterialLibrary.Definitions["hedge"] = new MaterialLibrary.Def { textureSet = "Grass004", tiling = 1f, tint = new Color(0.25f, 0.4f, 0.16f), smoothnessFallback = 0.2f };

            // Facade textures: one repeat spans N floors (see FacadeFloorsPerRepeat); building UVs are expressed in
            // floors, so the facade materials must tile 1:1. Update the definition and the existing material asset.
            foreach (var key in FacadeFloorsPerRepeat.Keys)
            {
                if (MaterialLibrary.Definitions.TryGetValue(key, out var def)) def.tiling = 1f;
                var m = MaterialLibrary.Get(key);
                if (m != null && m.HasProperty("_BaseColorMap"))
                {
                    m.SetTextureScale("_BaseColorMap", Vector2.one);
                    EditorUtility.SetDirty(m);
                }
            }

            // tinted / emissive variants used by the generator
            RegisterVariant("grass#hedge", "hedge", new Color(0.3f, 0.45f, 0.2f), 0.7f);
            RegisterVariant("grass#foliage", "grass", new Color(0.25f, 0.45f, 0.18f), 0.8f);
            RegisterVariant("metal_painted#red", "metal_painted", new Color(0.7f, 0.12f, 0.1f), 0.9f);
            RegisterVariant("window_lit#metroM", "window_lit", new Color(0.05f, 0.15f, 0.6f), 1f, new Color(0.15f, 0.35f, 1f), 12f);

            Roads = new RoadIndex(D.roads);
            var rings = new List<List<Vector2>>();
            foreach (var b in D.buildings) if (b.outer.Count >= 3 && !b.part) rings.Add(b.outer);
            BuildingIndex = new PolyIndex(rings);
        }

        // ------------------------------------------------------------------ tiles
        public static string TileName(Vector2 p)
        {
            int tx = Mathf.FloorToInt(p.x / TileSize), tz = Mathf.FloorToInt(p.y / TileSize);
            return $"Tile_{tx}_{tz}";
        }

        static Transform TileRoot(string tile)
        {
            if (TileRoots.TryGetValue(tile, out var t) && t != null) return t;
            var go = new GameObject(tile); go.transform.SetParent(CityRoot, false);
            TileRoots[tile] = go.transform; return go.transform;
        }

        static Transform PropTileRoot(Vector2 p)
        {
            string tile = TileName(p);
            if (PropTileRoots.TryGetValue(tile, out var t) && t != null) return t;
            var go = new GameObject(tile); go.transform.SetParent(PropsRoot, false);
            PropTileRoots[tile] = go.transform; return go.transform;
        }

        class TileBuilder
        {
            public MeshUtil.Builder B = new MeshUtil.Builder();
            public List<string> Mats = new List<string>();
            public int Sub(string mat) { int i = Mats.IndexOf(mat); if (i < 0) { Mats.Add(mat); i = Mats.Count - 1; } return i; }
        }

        class TileSet
        {
            public readonly Dictionary<string, List<TileBuilder>> Map = new Dictionary<string, List<TileBuilder>>();
            public TileBuilder Get(Vector2 pos, bool collide)
            {
                string key = TileName(pos) + (collide ? "" : "_nc");
                if (!Map.TryGetValue(key, out var list)) { list = new List<TileBuilder>(); Map[key] = list; }
                if (list.Count == 0 || list[list.Count - 1].B.VertexCount > MaxVertsPerMesh) list.Add(new TileBuilder());
                return list[list.Count - 1];
            }
        }

        /// <summary>Target (builder + submesh index) for a piece of static geometry at world xz position with a material key.</summary>
        struct Target { public MeshUtil.Builder B; public int Sub; }
        static Target T(Vector2 pos, string mat, bool collide = true)
        {
            if (NoCollideMats.Contains(mat)) collide = false;
            var tb = Tiles.Get(pos, collide);
            return new Target { B = tb.B, Sub = tb.Sub(mat) };
        }
        static Target T(Vector3 pos, string mat, bool collide = true) => T(new Vector2(pos.x, pos.z), mat, collide);

        static void FlushTiles()
        {
            int meshes = 0;
            foreach (var kv in Tiles.Map)
            {
                bool collide = !kv.Key.EndsWith("_nc");
                string tile = collide ? kv.Key : kv.Key.Substring(0, kv.Key.Length - 3);
                for (int i = 0; i < kv.Value.Count; i++)
                {
                    var tb = kv.Value[i];
                    if (tb.B.VertexCount == 0) continue;
                    try
                    {
                        string name = $"{kv.Key}_{i}";
                        var mesh = tb.B.ToMesh(name);
                        MeshUtil.SaveMeshAsset(mesh, $"{GenFolder}/Tiles/{name}.asset");
                        var mats = new Material[tb.Mats.Count];
                        for (int m = 0; m < mats.Length; m++) mats[m] = Mat(tb.Mats[m]);
                        MeshUtil.MakeStatic(name, mesh, mats, TileRoot(tile), collide);
                        meshes++;
                    }
                    catch (Exception e) { Debug.LogError($"[CityBuilder] tile {kv.Key}[{i}] failed: {e.Message}"); }
                }
            }
            Count("tileMeshes", meshes);
            Tiles = new TileSet();
        }

        // ------------------------------------------------------------------ materials
        static Material Mat(string key)
        {
            if (VariantMats.TryGetValue(key, out var v) && v != null) return v;
            return MaterialLibrary.Get(key);
        }

        /// <summary>Creates (or loads) a tinted/emissive copy of a library material, saved under Art/Materials/Variants. Returns the key to use with <see cref="Mat"/>.</summary>
        static string RegisterVariant(string key, string baseKey, Color tint, float tintLerp = 0.6f, Color emissive = default, float emissiveIntensity = 0f)
        {
            if (VariantMats.TryGetValue(key, out var existing) && existing != null) return key;
            MeshUtil.EnsureFolder(VariantFolder);
            string safe = key.Replace('#', '_').Replace('@', '_').Replace('/', '_');
            string path = $"{VariantFolder}/{safe}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                var baseMat = MaterialLibrary.Get(baseKey);
                m = new Material(baseMat) { name = safe };
                bool textured = m.HasProperty("_BaseColorMap") && m.GetTexture("_BaseColorMap") != null;
                var c = textured ? Color.Lerp(Color.white, tint, tintLerp) : tint;
                c.a = tint.a;
                m.SetColor("_BaseColor", c);
                if (emissiveIntensity > 0f)
                {
                    m.SetFloat("_UseEmissiveIntensity", 1f);
                    m.SetColor("_EmissiveColor", emissive * emissiveIntensity);
                    m.SetColor("_EmissiveColorLDR", emissive);
                    m.SetFloat("_EmissiveIntensity", emissiveIntensity);
                    m.SetFloat("_EmissiveExposureWeight", 0.5f);
                }
                HDMaterial.ValidateMaterial(m);
                AssetDatabase.CreateAsset(m, path);
            }
            VariantMats[key] = m;
            return key;
        }

        static readonly Dictionary<string, Color> NamedColours = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
        {
            ["white"] = new Color(0.95f, 0.95f, 0.93f), ["grey"] = new Color(0.6f, 0.6f, 0.6f), ["gray"] = new Color(0.6f, 0.6f, 0.6f),
            ["lightgrey"] = new Color(0.78f, 0.78f, 0.78f), ["lightgray"] = new Color(0.78f, 0.78f, 0.78f), ["darkgrey"] = new Color(0.35f, 0.35f, 0.35f),
            ["beige"] = new Color(0.85f, 0.78f, 0.65f), ["yellow"] = new Color(0.9f, 0.8f, 0.4f), ["cream"] = new Color(0.95f, 0.9f, 0.78f),
            ["brown"] = new Color(0.45f, 0.3f, 0.2f), ["red"] = new Color(0.6f, 0.2f, 0.15f), ["blue"] = new Color(0.3f, 0.45f, 0.7f),
            ["green"] = new Color(0.35f, 0.55f, 0.35f), ["orange"] = new Color(0.9f, 0.55f, 0.25f), ["pink"] = new Color(0.9f, 0.65f, 0.7f),
            ["black"] = new Color(0.12f, 0.12f, 0.12f), ["silver"] = new Color(0.75f, 0.75f, 0.78f), ["tan"] = new Color(0.82f, 0.7f, 0.55f),
        };

        static bool ParseColour(string s, out Color c)
        {
            c = Color.white;
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Trim();
            if (NamedColours.TryGetValue(s, out c)) return true;
            if (ColorUtility.TryParseHtmlString(s.StartsWith("#") ? s : "#" + s, out c)) return true;
            if (ColorUtility.TryParseHtmlString(s, out c)) return true;
            return false;
        }

        // ------------------------------------------------------------------ random
        static float R(float a, float b) => a + (float)Rng.NextDouble() * (b - a);
        static int RI(int aInclusive, int bInclusive) => Rng.Next(aInclusive, bInclusive + 1);
        static float R(System.Random r, float a, float b) => a + (float)r.NextDouble() * (b - a);

        // ------------------------------------------------------------------ 2D helpers (map view: x east, y north)
        static Vector3 XZ(Vector2 p, float y) => new Vector3(p.x, y, p.y);
        static Vector2 V2(Vector3 p) => new Vector2(p.x, p.z);
        /// <summary>Right-hand side of travel in map view (matches MeshUtil.Builder.AddRibbon side vector).</summary>
        static Vector2 Perp(Vector2 d) => new Vector2(d.y, -d.x);
        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
        static float YawDeg(Vector2 forward) => Mathf.Atan2(forward.x, forward.y) * Mathf.Rad2Deg;

        static List<Vector2> CleanLine(List<Vector2> pts, float eps = 0.02f)
        {
            var r = new List<Vector2>(pts.Count);
            foreach (var p in pts) if (r.Count == 0 || (r[r.Count - 1] - p).sqrMagnitude > eps * eps) r.Add(p);
            return r;
        }

        static float Length(List<Vector2> line)
        {
            float l = 0; for (int i = 0; i + 1 < line.Count; i++) l += (line[i + 1] - line[i]).magnitude; return l;
        }

        /// <summary>Point at arc-length s along a polyline (clamped), and the unit direction there.</summary>
        static Vector2 PointAt(List<Vector2> line, float s, out Vector2 dir)
        {
            dir = Vector2.up;
            if (line.Count == 1) return line[0];
            float acc = 0;
            for (int i = 0; i + 1 < line.Count; i++)
            {
                var d = line[i + 1] - line[i]; float len = d.magnitude; if (len < 1e-6f) continue;
                dir = d / len;
                if (s <= acc + len || i == line.Count - 2) { float t = Mathf.Clamp01((s - acc) / len); return line[i] + d * t; }
                acc += len;
            }
            return line[line.Count - 1];
        }

        /// <summary>Per-vertex offset vectors (right side, mitred, mitre limit 2) identical to AddRibbon's construction. p + Off[i]*w is the right edge at width w.</summary>
        static List<Vector2> MitreOffsets(List<Vector2> line)
        {
            int n = line.Count; var res = new List<Vector2>(n);
            for (int i = 0; i < n; i++)
            {
                Vector2 dir;
                if (n < 2) dir = Vector2.up;
                else if (i == 0) dir = line[1] - line[0];
                else if (i == n - 1) dir = line[i] - line[i - 1];
                else dir = (line[i + 1] - line[i]).normalized + (line[i] - line[i - 1]).normalized;
                dir = dir.sqrMagnitude < 1e-6f ? Vector2.up : dir.normalized;
                float m = 1f;
                if (i > 0 && i < n - 1)
                {
                    var d0 = (line[i] - line[i - 1]).normalized;
                    m = 1f / Mathf.Max(0.5f, Vector2.Dot(d0, dir));
                }
                res.Add(Perp(dir) * m);
            }
            return res;
        }

        /// <summary>Offsets an open polyline to the right (positive) / left (negative) using mitred joins.</summary>
        static List<Vector2> OffsetLine(List<Vector2> line, float offset)
        {
            var off = MitreOffsets(line); var res = new List<Vector2>(line.Count);
            for (int i = 0; i < line.Count; i++) res.Add(line[i] + off[i] * offset);
            return res;
        }

        static bool ValidRing(List<Vector2> ring, float minArea = 0.5f) => ring != null && ring.Count >= 3 && Mathf.Abs(MeshUtil.SignedArea(ring)) > minArea;

        static Rect BBox(IList<Vector2> pts)
        {
            float x0 = float.MaxValue, z0 = float.MaxValue, x1 = float.MinValue, z1 = float.MinValue;
            foreach (var p in pts) { x0 = Mathf.Min(x0, p.x); z0 = Mathf.Min(z0, p.y); x1 = Mathf.Max(x1, p.x); z1 = Mathf.Max(z1, p.y); }
            return Rect.MinMaxRect(x0, z0, x1, z1);
        }

        static bool InsideWithHoles(Vector2 p, List<Vector2> outer, List<List<Vector2>> holes)
        {
            if (!MeshUtil.PointInPolygon(p, outer)) return false;
            if (holes != null) foreach (var h in holes) if (h.Count >= 3 && MeshUtil.PointInPolygon(p, h)) return false;
            return true;
        }

        /// <summary>Random point inside polygon (rejection sampling); returns false if none found.</summary>
        static bool RandomPointIn(System.Random r, List<Vector2> outer, List<List<Vector2>> holes, Rect bb, int tries, out Vector2 p)
        {
            for (int i = 0; i < tries; i++)
            {
                p = new Vector2(R(r, bb.xMin, bb.xMax), R(r, bb.yMin, bb.yMax));
                if (InsideWithHoles(p, outer, holes)) return true;
            }
            p = Vector2.zero; return false;
        }

        /// <summary>Splits a polyline into the runs that lie outside the road corridors (excluding one road), sampling every 'step' metres and refining transitions.</summary>
        static List<List<Vector2>> SplitByCorridor(List<Vector2> line, int excludeRoad, float margin, float step = 1f)
        {
            var runs = new List<List<Vector2>>();
            if (line.Count < 2) return runs;
            bool Inside(Vector2 q) => Roads.Inside(q, margin, excludeRoad);
            var cur = new List<Vector2>();
            bool state = Inside(line[0]);
            if (!state) cur.Add(line[0]);
            Vector2 prev = line[0];
            void Finish() { if (cur.Count >= 2 && Length(cur) > 0.25f) runs.Add(cur); cur = new List<Vector2>(); }
            for (int i = 0; i + 1 < line.Count; i++)
            {
                var a = line[i]; var b = line[i + 1]; float len = (b - a).magnitude; if (len < 1e-5f) continue;
                int k = Mathf.Max(1, Mathf.CeilToInt(len / step));
                for (int j = 1; j <= k; j++)
                {
                    var q = Vector2.Lerp(a, b, j / (float)k);
                    bool st = Inside(q);
                    if (st != state)
                    {
                        // bisect transition between prev and q
                        Vector2 lo = prev, hi = q;
                        for (int it = 0; it < 6; it++) { var mid = (lo + hi) * 0.5f; if (Inside(mid) == state) lo = mid; else hi = mid; }
                        var x = (lo + hi) * 0.5f;
                        if (st) { cur.Add(x); Finish(); } else { cur = new List<Vector2> { x }; }
                        state = st;
                    }
                    if (j == k && !state) cur.Add(q);
                    prev = q;
                }
            }
            Finish();
            return runs;
        }

        // ------------------------------------------------------------------ mesh helpers (per-tile)
        /// <summary>Up-facing planar quad from 4 corners in cyclic order; winding fixed automatically.</summary>
        static void FlatQuadUp(MeshUtil.Builder b, int sub, Vector3 a, Vector3 c1, Vector3 c2, Vector3 d, Vector2 uva, Vector2 uvb, Vector2 uvc, Vector2 uvd)
        {
            var n = Vector3.Cross(c1 - a, c2 - a);
            if (n.y >= 0) b.AddQuad(sub, a, c1, c2, d, uva, uvb, uvc, uvd);
            else b.AddQuad(sub, a, d, c2, c1, uva, uvd, uvc, uvb);
        }
        static void FlatQuadUp(MeshUtil.Builder b, int sub, Vector3 a, Vector3 c1, Vector3 c2, Vector3 d, float uvScale = 1f)
            => FlatQuadUp(b, sub, a, c1, c2, d, new Vector2(a.x, a.z) * uvScale, new Vector2(c1.x, c1.z) * uvScale, new Vector2(c2.x, c2.z) * uvScale, new Vector2(d.x, d.z) * uvScale);

        /// <summary>Vertical wall between a and c (map coords) from y0 to y1 facing 'want' (map-view direction). UV u along, v height.</summary>
        static void WallQuad(MeshUtil.Builder b, int sub, Vector2 a, Vector2 c, float y0, float y1, Vector2 want, float u0 = 0f, float uvScale = 1f)
        {
            float len = (c - a).magnitude; if (len < 1e-5f || y1 - y0 < 1e-5f) return;
            Vector3 A0 = XZ(a, y0), C0 = XZ(c, y0), C1 = XZ(c, y1), A1 = XZ(a, y1);
            var n = Vector3.Cross(C0 - A0, A1 - A0);
            var w3 = new Vector3(want.x, 0, want.y);
            var uvA0 = new Vector2(u0, y0) * uvScale; var uvC0 = new Vector2(u0 + len, y0) * uvScale;
            var uvC1 = new Vector2(u0 + len, y1) * uvScale; var uvA1 = new Vector2(u0, y1) * uvScale;
            if (Vector3.Dot(n, w3) >= 0) b.AddQuad(sub, A0, C0, C1, A1, uvA0, uvC0, uvC1, uvA1);
            else b.AddQuad(sub, A0, A1, C1, C0, uvA0, uvA1, uvC1, uvC0);
        }

        /// <summary>Convex polygon fan, up-facing, UV = world xz * uvScale.</summary>
        static void AddFan(MeshUtil.Builder b, int sub, List<Vector2> poly, float y, float uvScale = 1f)
        {
            if (poly.Count < 3) return;
            if (MeshUtil.SignedArea(poly) < 0) poly.Reverse();
            int start = b.VertexCount;
            foreach (var p in poly) b.Add(XZ(p, y), Vector3.up, p * uvScale);
            for (int i = 1; i + 1 < poly.Count; i++)
            {
                if (Cross(poly[i] - poly[0], poly[i + 1] - poly[0]) < 1e-4f) continue; // skip degenerate slivers
                b.Tri(sub, start, start + i + 1, start + i); // CCW map view -> up-facing needs reversed order
            }
        }

        /// <summary>Vertical walls along a closed ring; outward = true faces away from the ring interior.</summary>
        static void AddRingWalls(MeshUtil.Builder b, int sub, List<Vector2> ring, float y0, float y1, float uvScale, bool outward)
        {
            ring = MeshUtil.Clean(ring); if (ring.Count < 3) return;
            bool ccw = MeshUtil.SignedArea(ring) > 0; float u = 0; int n = ring.Count;
            for (int i = 0; i < n; i++)
            {
                var a = ring[i]; var c = ring[(i + 1) % n]; float len = (c - a).magnitude; if (len < 1e-5f) continue;
                var dir = (c - a) / len;
                var outN = ccw ? Perp(dir) : -Perp(dir);
                WallQuad(b, sub, a, c, y0, y1, outward ? outN : -outN, u, uvScale);
                u += len;
            }
        }

        /// <summary>Flat strip between two rings with identical vertex counts (e.g. a ring and its OffsetRing).</summary>
        static void AddRingStrip(MeshUtil.Builder b, int sub, List<Vector2> ringA, List<Vector2> ringB, float y, float uvScale = 1f)
        {
            int n = Mathf.Min(ringA.Count, ringB.Count); if (n < 3) return;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                FlatQuadUp(b, sub, XZ(ringA[i], y), XZ(ringA[j], y), XZ(ringB[j], y), XZ(ringB[i], y), uvScale);
            }
        }

        /// <summary>Flat strip along an open polyline (per-segment quads, each placed in the tile of its midpoint). UV u across in metres, v along.</summary>
        static void AddStrip(List<Vector2> line, float halfW, float y, string mat, bool collide = true, float uvScale = 1f)
        {
            if (line.Count < 2) return;
            var L = OffsetLine(line, -halfW); var Rr = OffsetLine(line, halfW); float dist = 0;
            for (int i = 0; i + 1 < line.Count; i++)
            {
                float len = (line[i + 1] - line[i]).magnitude; if (len < 1e-5f) continue;
                var t = T((line[i] + line[i + 1]) * 0.5f, mat, collide);
                FlatQuadUp(t.B, t.Sub, XZ(L[i], y), XZ(L[i + 1], y), XZ(Rr[i + 1], y), XZ(Rr[i], y),
                    new Vector2(0, dist) * uvScale, new Vector2(0, dist + len) * uvScale, new Vector2(2 * halfW, dist + len) * uvScale, new Vector2(2 * halfW, dist) * uvScale);
                dist += len;
            }
        }

        /// <summary>Box-section strip (top + both side walls + end caps) along an open polyline.</summary>
        static void AddBoxStrip(List<Vector2> line, float halfW, float y0, float y1, string mat, bool collide = true, float uvScale = 1f)
        {
            if (line.Count < 2) return;
            var L = OffsetLine(line, -halfW); var Rr = OffsetLine(line, halfW); float dist = 0; int n = line.Count;
            for (int i = 0; i + 1 < n; i++)
            {
                var d = line[i + 1] - line[i]; float len = d.magnitude; if (len < 1e-5f) continue; d /= len;
                var t = T((line[i] + line[i + 1]) * 0.5f, mat, collide);
                FlatQuadUp(t.B, t.Sub, XZ(L[i], y1), XZ(L[i + 1], y1), XZ(Rr[i + 1], y1), XZ(Rr[i], y1),
                    new Vector2(0, dist) * uvScale, new Vector2(0, dist + len) * uvScale, new Vector2(2 * halfW, dist + len) * uvScale, new Vector2(2 * halfW, dist) * uvScale);
                WallQuad(t.B, t.Sub, Rr[i], Rr[i + 1], y0, y1, Perp(d), dist, uvScale);
                WallQuad(t.B, t.Sub, L[i], L[i + 1], y0, y1, -Perp(d), dist, uvScale);
                if (i == 0) WallQuad(t.B, t.Sub, L[0], Rr[0], y0, y1, -d, 0, uvScale);
                if (i == n - 2) WallQuad(t.B, t.Sub, L[n - 1], Rr[n - 1], y0, y1, d, 0, uvScale);
                dist += len;
            }
        }

        /// <summary>Box-section ring (top + outer and inner walls) centred on a closed ring.</summary>
        static void AddBoxRing(List<Vector2> ring, float halfW, float y0, float y1, string mat, bool collide = true, float uvScale = 1f)
        {
            ring = MeshUtil.Clean(ring); if (ring.Count < 3) return;
            var inner = MeshUtil.OffsetRing(ring, -halfW); var outer = MeshUtil.OffsetRing(ring, halfW);
            int n = ring.Count; if (inner.Count != n || outer.Count != n) return;
            bool ccw = MeshUtil.SignedArea(ring) > 0; float u = 0;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n; var d = ring[j] - ring[i]; float len = d.magnitude; if (len < 1e-5f) continue; d /= len;
                var outN = ccw ? Perp(d) : -Perp(d);
                var t = T((ring[i] + ring[j]) * 0.5f, mat, collide);
                FlatQuadUp(t.B, t.Sub, XZ(inner[i], y1), XZ(inner[j], y1), XZ(outer[j], y1), XZ(outer[i], y1), uvScale);
                WallQuad(t.B, t.Sub, outer[i], outer[j], y0, y1, outN, u, uvScale);
                WallQuad(t.B, t.Sub, inner[i], inner[j], y0, y1, -outN, u, uvScale);
                u += len;
            }
        }

        /// <summary>Oriented box (top + 4 walls) with centre c (map), long axis along dir. Placed in the tile of c.</summary>
        static void AddOBox(Vector2 c, Vector2 dir, float len, float wid, float y0, float y1, string mat, bool collide = true, bool bottom = false)
        {
            dir = dir.sqrMagnitude < 1e-8f ? Vector2.up : dir.normalized; var n = Perp(dir);
            var p0 = c - dir * (len * 0.5f) - n * (wid * 0.5f); var p1 = c + dir * (len * 0.5f) - n * (wid * 0.5f);
            var p2 = c + dir * (len * 0.5f) + n * (wid * 0.5f); var p3 = c - dir * (len * 0.5f) + n * (wid * 0.5f);
            var t = T(c, mat, collide);
            FlatQuadUp(t.B, t.Sub, XZ(p0, y1), XZ(p1, y1), XZ(p2, y1), XZ(p3, y1), 1f);
            WallQuad(t.B, t.Sub, p0, p1, y0, y1, -n);
            WallQuad(t.B, t.Sub, p1, p2, y0, y1, dir);
            WallQuad(t.B, t.Sub, p2, p3, y0, y1, n);
            WallQuad(t.B, t.Sub, p3, p0, y0, y1, -dir);
            if (bottom)
            {
                // down-facing: build up-facing then it is flipped by passing reversed order through AddQuad directly
                var nn = Vector3.Cross(XZ(p1, y0) - XZ(p0, y0), XZ(p2, y0) - XZ(p0, y0));
                if (nn.y <= 0) t.B.AddQuad(t.Sub, XZ(p0, y0), XZ(p1, y0), XZ(p2, y0), XZ(p3, y0), p0, p1, p2, p3);
                else t.B.AddQuad(t.Sub, XZ(p0, y0), XZ(p3, y0), XZ(p2, y0), XZ(p1, y0), p0, p3, p2, p1);
            }
        }

        /// <summary>Polygon (with holes) placed in the tile of its centroid.</summary>
        static void AddPolygonTiled(List<Vector2> outer, List<List<Vector2>> holes, float y, string mat, bool collide = true, float uvScale = 1f)
        {
            var o = MeshUtil.Clean(outer); if (o.Count < 3) return;
            var t = T(MeshUtil.Centroid(o), mat, collide);
            t.B.AddPolygon(t.Sub, o, holes, y, uvScale);
        }

        // ------------------------------------------------------------------ spatial index for polygons (buildings)
        class PolyIndex
        {
            readonly List<List<Vector2>> rings; readonly List<Rect> boxes;
            readonly Dictionary<long, List<int>> grid = new Dictionary<long, List<int>>(); const float Cell = 50f;
            static long Key(int cx, int cz) => (long)(cx + 100000) * 1000000L + (cz + 100000);
            public PolyIndex(List<List<Vector2>> polys)
            {
                rings = polys; boxes = new List<Rect>(polys.Count);
                for (int i = 0; i < polys.Count; i++)
                {
                    var bb = BBox(polys[i]); boxes.Add(bb);
                    for (int cx = Mathf.FloorToInt(bb.xMin / Cell); cx <= Mathf.FloorToInt(bb.xMax / Cell); cx++)
                        for (int cz = Mathf.FloorToInt(bb.yMin / Cell); cz <= Mathf.FloorToInt(bb.yMax / Cell); cz++)
                        {
                            long k = Key(cx, cz); if (!grid.TryGetValue(k, out var l)) { l = new List<int>(); grid[k] = l; } l.Add(i);
                        }
                }
            }
            public bool Contains(Vector2 p, float margin = 0f)
            {
                long k = Key(Mathf.FloorToInt(p.x / Cell), Mathf.FloorToInt(p.y / Cell));
                if (!grid.TryGetValue(k, out var l)) return false;
                foreach (int i in l)
                {
                    var bb = boxes[i];
                    if (p.x < bb.xMin - margin || p.x > bb.xMax + margin || p.y < bb.yMin - margin || p.y > bb.yMax + margin) continue;
                    if (MeshUtil.PointInPolygon(p, rings[i])) return true;
                    if (margin > 0f && DistToRing(p, rings[i]) < margin) return true;
                }
                return false;
            }
            static float DistToRing(Vector2 p, List<Vector2> r)
            {
                float best = float.MaxValue;
                for (int i = 0; i < r.Count; i++) best = Mathf.Min(best, DistToSeg(p, r[i], r[(i + 1) % r.Count]));
                return best;
            }
        }

        static float DistToSeg(Vector2 p, Vector2 a, Vector2 b, out float t)
        {
            var d = b - a; float l2 = d.sqrMagnitude;
            t = l2 < 1e-9f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, d) / l2);
            return (p - (a + d * t)).magnitude;
        }
        static float DistToSeg(Vector2 p, Vector2 a, Vector2 b) => DistToSeg(p, a, b, out _);
    }
}
