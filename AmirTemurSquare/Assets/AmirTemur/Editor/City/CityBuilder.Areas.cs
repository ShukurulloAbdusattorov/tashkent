using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace AmirTemur.Editor
{
    public static partial class CityBuilder
    {
        static readonly List<Vector2> WoodTreeSpots = new List<Vector2>();   // filled by BuildAreas, consumed by BuildProps
        static readonly List<List<Vector2>> GrassRings = new List<List<Vector2>>(); // grass/wood outer rings (for shrubs)
        static readonly List<CityData.Area> PlazaAreas = new List<CityData.Area>();

        // ------------------------------------------------------------------ areas
        static void BuildAreas()
        {
            WoodTreeSpots.Clear(); GrassRings.Clear(); PlazaAreas.Clear();
            int nGrass = 0, nPark = 0, nPlaza = 0, nFount = 0, nPitch = 0, nJets = 0;
            foreach (var a in D.areas)
            {
                try
                {
                    var ring = MeshUtil.Clean(a.outer);
                    if (!ValidRing(ring, 1f)) continue;
                    var holes = new List<List<Vector2>>();
                    foreach (var h in a.inners) { var hc = MeshUtil.Clean(h); if (ValidRing(hc, 0.5f)) holes.Add(hc); }
                    switch (a.kind)
                    {
                        case "grass":
                        case "wood":
                            AddPolygonTiled(ring, holes, GrassY, "grass", true, 1f);
                            AddBoxRing(MeshUtil.OffsetRing(ring, 0.1f), 0.1f, 0f, 0.15f, "kerb", true, 1f);
                            GrassRings.Add(ring);
                            if (a.kind == "wood") ScatterWoodTrees(ring, holes, a.id);
                            nGrass++;
                            break;
                        case "parking":
                            {
                                var ext = MeshUtil.OffsetRing(ring, KerbW); if (!ValidRing(ext, 1f)) ext = ring;
                                AddPolygonTiled(ext, holes, ParkingY, "asphalt_worn", true, 1f);
                                AddBoxRing(MeshUtil.OffsetRing(ring, KerbW * 0.5f), KerbW * 0.5f, ParkingY, KerbTopY, "kerb", true, 1f);
                                AddParkingBays(ring, a.id);
                                nPark++;
                            }
                            break;
                        case "plaza":
                            AddPolygonTiled(ring, holes, PlazaY, "paving_granite", true, 1f);
                            { var t = T(MeshUtil.Centroid(ring), "paving_granite"); AddRingWalls(t.B, t.Sub, ring, -0.02f, PlazaY, 1f, true); }
                            PlazaAreas.Add(a);
                            nPlaza++;
                            break;
                        case "pitch":
                            AddPolygonTiled(ring, holes, PlazaY, "asphalt_worn", true, 1f);
                            nPitch++;
                            break;
                        case "fountain":
                        case "water":
                            nJets += BuildBasin(ring, a.kind == "fountain", a.id);
                            nFount++;
                            break;
                        default:
                            AddPolygonTiled(ring, holes, PlazaY, "paving_plaza", true, 1f);
                            break;
                    }
                }
                catch (Exception e) { Debug.LogWarning($"[CityBuilder] area {a.id} ({a.kind}) failed: {e.Message}"); }
            }
            Count("grassAreas", nGrass); Count("parkings", nPark); Count("plazas", nPlaza); Count("pitches", nPitch); Count("basins", nFount); Count("fountainJets", nJets);
        }

        static void ScatterWoodTrees(List<Vector2> ring, List<List<Vector2>> holes, long seed)
        {
            float area = Mathf.Abs(MeshUtil.SignedArea(ring));
            int n = Mathf.Min(400, Mathf.RoundToInt(area / 60f)); if (n <= 0) return;
            var r = new System.Random((int)(seed & 0x7fffffff));
            var bb = BBox(ring); var placed = new List<Vector2>();
            int tries = n * 30;
            while (placed.Count < n && tries-- > 0)
            {
                var p = new Vector2(R(r, bb.xMin, bb.xMax), R(r, bb.yMin, bb.yMax));
                if (!InsideWithHoles(p, ring, holes)) continue;
                bool ok = true;
                foreach (var q in placed) if ((q - p).sqrMagnitude < 16f) { ok = false; break; }
                if (!ok) continue;
                if (Roads.Inside(p, 1.5f) || BuildingIndex.Contains(p, 1f)) continue;
                placed.Add(p);
            }
            WoodTreeSpots.AddRange(placed);
        }

        static void AddParkingBays(List<Vector2> ring, long seed)
        {
            // bays perpendicular to the longest edge, 2.5 m pitch, 5 m deep, only where fully inside the polygon
            int best = 0; float bestLen = 0; int n = ring.Count;
            for (int i = 0; i < n; i++) { float l = (ring[(i + 1) % n] - ring[i]).magnitude; if (l > bestLen) { bestLen = l; best = i; } }
            if (bestLen < 6f) return;
            var a = ring[best]; var b = ring[(best + 1) % n]; var d = (b - a) / bestLen;
            var inward = MeshUtil.SignedArea(ring) > 0 ? -Perp(d) : Perp(d);
            float y = ParkingY + 0.005f; int made = 0;
            for (float s = 1.5f; s < bestLen - 1.5f; s += 2.5f)
            {
                var p0 = a + d * s + inward * 0.4f; var p1 = p0 + inward * 5f;
                if (!MeshUtil.PointInPolygon(p0, ring) || !MeshUtil.PointInPolygon(p1, ring)) continue;
                AddStrip(new List<Vector2> { p0, p1 }, 0.06f, y, "road_marking_white", false, 1f);
                if (++made > 80) break;
            }
        }

        /// <summary>Fountain / pond basin: rim wall, tiled floor, water surface and particle jets. Returns jet count.</summary>
        static int BuildBasin(List<Vector2> ring, bool jets, long id)
        {
            var floorRing = MeshUtil.OffsetRing(ring, 0.35f); if (!ValidRing(floorRing, 1f)) floorRing = ring;
            AddPolygonTiled(floorRing, null, -0.3f, "tiles_blue", true, 1f);
            AddBoxRing(MeshUtil.OffsetRing(ring, 0.175f), 0.175f, -0.3f, 0.45f, "marble_white", true, 1f);
            AddPolygonTiled(ring, null, 0.25f, "water", false, 0.25f);
            if (!jets) return 0;

            var c = MeshUtil.Centroid(ring);
            var r = new System.Random((int)(id & 0x7fffffff));
            var parent = new GameObject($"Fountain_{id}").transform; parent.SetParent(FxRoot, false);
            int count = 0;
            bool big = c.magnitude < 90f;
            if (big) { MakeJet(parent, XZ(c, 0.2f), 0.35f, 14f, 500f, 2.2f, 4f); count++; }
            int n = r.Next(3, 8);
            var inset = MeshUtil.OffsetRing(ring, -1f); if (!ValidRing(inset, 1f)) inset = ring;
            var bb = BBox(inset);
            for (int i = 0; i < n; i++)
            {
                Vector2 p;
                if (!RandomPointIn(r, inset, null, bb, 30, out p)) p = c;
                MakeJet(parent, XZ(p, 0.2f), 0.15f, R(r, 6f, 9f), 120f, 1.5f, 5f); count++;
            }
            return count;
        }

        static Material _jetMat;
        static Material JetMaterial()
        {
            if (_jetMat != null) return _jetMat;
            string path = $"{VariantFolder}/fx_water_jet.mat";
            _jetMat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (_jetMat == null)
            {
                var sh = Shader.Find("HDRP/Unlit") ?? Shader.Find("HDRP/Lit");
                _jetMat = new Material(sh) { name = "fx_water_jet" };
                if (_jetMat.HasProperty("_UnlitColor")) _jetMat.SetColor("_UnlitColor", new Color(0.8f, 0.92f, 1f, 0.55f));
                if (_jetMat.HasProperty("_BaseColor")) _jetMat.SetColor("_BaseColor", new Color(0.8f, 0.92f, 1f, 0.55f));
                _jetMat.SetFloat("_SurfaceType", 1f); _jetMat.SetFloat("_BlendMode", 0f); _jetMat.SetFloat("_ZWrite", 0f);
                _jetMat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                HDMaterial.ValidateMaterial(_jetMat);
                MeshUtil.EnsureFolder(VariantFolder);
                AssetDatabase.CreateAsset(_jetMat, path);
            }
            return _jetMat;
        }

        static void MakeJet(Transform parent, Vector3 pos, float size, float speed, float rate, float life, float coneAngle)
        {
            var go = new GameObject("FountainJet");
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(-90f, 0f, 0f); // emit along +Y
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = life;
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.9f, speed * 1.1f);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.8f, size * 1.2f);
            main.gravityModifier = 1f;
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.85f, 0.95f, 1f, 0.7f), new Color(0.6f, 0.8f, 1f, 0.5f));
            main.maxParticles = Mathf.CeilToInt(rate * life * 1.5f);
            main.loop = true; main.playOnAwake = true;
            var em = ps.emission; em.enabled = true; em.rateOverTime = rate;
            var sh = ps.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = coneAngle; sh.radius = size * 0.4f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.7f, 0.85f, 1f), 1f) },
                      new[] { new GradientAlphaKey(0.8f, 0f), new GradientAlphaKey(0.6f, 0.7f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);
            var rend = go.GetComponent<ParticleSystemRenderer>();
            rend.renderMode = ParticleSystemRenderMode.Billboard;
            rend.sharedMaterial = JetMaterial();
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // ------------------------------------------------------------------ buildings
        /// <summary>How many storeys one repeat of each facade texture spans (textures are square; u is scaled identically).</summary>
        static readonly Dictionary<string, float> FacadeFloorsPerRepeat = new Dictionary<string, float>
        {
            ["facade_office"] = 8f,   // Facade001: 8 rows of windows
            ["facade_classic"] = 7f,  // Facade006: 7 rows
            ["facade_glass"] = 5f,    // Facade018: 5 rows
            ["facade_soviet"] = 8f,   // Facade020: 8 rows
        };

        static string PickFacade(CityData.Building b, int levels, float height, System.Random r)
        {
            string type = (b.type ?? "yes").ToLowerInvariant();
            if (b.material == "brick") return "brick";
            if ((type == "commercial" || type == "office") && height > 30f) return "facade_glass";
            if (levels >= 10) return "facade_office";
            if (type == "university" || type == "civic" || type == "museum") return "facade_classic";
            if (type == "yes" && levels <= 2 && r.NextDouble() < 0.1) return "brick";
            if (levels <= 3) return "facade_classic";
            return "facade_soviet";
        }

        static void BuildBuildings()
        {
            int n = 0, skipped = 0;
            // parts whose centroid lies within a skipped landmark footprint are skipped too (the Landmarks module models those)
            var skipRings = new List<List<Vector2>>();
            foreach (var b in D.buildings) if (SkipBuildingIds.Contains(b.id)) skipRings.Add(b.outer);

            foreach (var b in D.buildings)
            {
                if (SkipBuildingIds.Contains(b.id)) { skipped++; continue; }
                try
                {
                    var ring = MeshUtil.Clean(b.outer);
                    if (!ValidRing(ring, 4f)) continue;
                    if (b.part)
                    {
                        var c = MeshUtil.Centroid(ring); bool inSkip = false;
                        foreach (var sr in skipRings) if (MeshUtil.PointInPolygon(c, sr)) { inSkip = true; break; }
                        if (inSkip) { skipped++; continue; }
                    }
                    BuildBuilding(b, ring);
                    n++;
                }
                catch (Exception e) { Debug.LogWarning($"[CityBuilder] building {b.id} ({b.name}) failed: {e.Message}"); }
            }
            Count("buildings", n); Count("buildingsSkipped", skipped);
        }

        static void BuildBuilding(CityData.Building b, List<Vector2> ring)
        {
            var r = new System.Random((int)(b.id & 0x7fffffff));
            int levels = b.levels > 0 ? b.levels : Mathf.Max(1, Mathf.RoundToInt(Mathf.Max(3f, b.height) / 3.2f));
            float height = b.height > 0.5f ? b.height : levels * 3.2f;
            float y0 = Mathf.Max(0f, b.minHeight), y1 = Mathf.Max(y0 + 2.5f, height);
            float levelHeight = Mathf.Clamp((y1 - y0) / Mathf.Max(1, levels), 2.4f, 6f);
            if (b.minHeight > 0f && b.levels > 0 && b.height > b.minHeight) levelHeight = Mathf.Clamp(b.height / b.levels, 2.4f, 6f);

            string facade = PickFacade(b, levels, height, r);
            float uvScale = FacadeFloorsPerRepeat.TryGetValue(facade, out var floors) ? 1f / (levelHeight * floors) : 1f;
            string facadeMat = facade;
            if (ParseColour(b.colour, out var tint))
                facadeMat = RegisterVariant($"{facade}#{ColorUtility.ToHtmlStringRGB(tint).ToLowerInvariant()}", facade, tint, 0.5f);

            var holes = new List<List<Vector2>>();
            foreach (var h in b.inners) { var hc = MeshUtil.Clean(h); if (ValidRing(hc, 0.5f)) holes.Add(hc); }

            var bld = new MeshUtil.Builder();
            const int SubFacade = 0, SubPlinth = 1, SubRoof = 2, SubConcrete = 3;
            float area = Mathf.Abs(MeshUtil.SignedArea(ring));

            // walls (UV in floors: one texture repeat = 'floors' storeys)
            bld.AddWalls(SubFacade, ring, y0, y1, uvScale);
            foreach (var h in holes) AddRingWalls(bld, SubFacade, h, y0, y1, uvScale, false);

            // plinth band (ground floor only)
            if (y0 < 0.01f)
            {
                var pr = MeshUtil.OffsetRing(ring, 0.06f);
                if (pr.Count == ring.Count)
                {
                    AddRingWalls(bld, SubPlinth, pr, -0.05f, 0.6f, 1f, true);
                    AddRingStrip(bld, SubPlinth, ring, pr, 0.6f, 1f);
                }
            }

            // roof
            bld.AddPolygon(SubRoof, ring, holes, y1, 1f);

            // parapet
            if (area > 30f)
            {
                AddRingWalls(bld, SubConcrete, ring, y1, y1 + 0.5f, 1f, true);
                var ir = MeshUtil.OffsetRing(ring, -0.3f);
                bool irOk = ir.Count == ring.Count && Mathf.Sign(MeshUtil.SignedArea(ir)) == Mathf.Sign(MeshUtil.SignedArea(ring)) && Mathf.Abs(MeshUtil.SignedArea(ir)) < area;
                if (irOk)
                {
                    AddRingWalls(bld, SubConcrete, ir, y1, y1 + 0.5f, 1f, false);
                    AddRingStrip(bld, SubConcrete, ir, ring, y1 + 0.5f, 1f);
                }
                else bld.AddPolygon(SubConcrete, ring, null, y1 + 0.5f, 1f);
            }

            // rooftop boxes (HVAC / stair heads)
            if (height > 12f && area > 60f)
            {
                var inset = MeshUtil.OffsetRing(ring, -2.5f);
                if (ValidRing(inset, 6f) && Mathf.Abs(MeshUtil.SignedArea(inset)) < area)
                {
                    var bb = BBox(inset); int boxes = r.Next(2, 6);
                    for (int k = 0; k < boxes; k++)
                    {
                        if (!RandomPointIn(r, inset, holes, bb, 20, out var p)) break;
                        float sx = R(r, 1.5f, 4f), sz = R(r, 1.5f, 4f), h = R(r, 1.2f, 3f);
                        bld.AddBox(SubConcrete, new Vector3(p.x, y1 + h * 0.5f, p.y), new Vector3(sx, h, sz), 1f);
                    }
                }
            }

            string name = $"Bld_{b.id}" + (string.IsNullOrEmpty(b.name) ? "" : "_" + Sanitize(b.name));
            var mesh = bld.ToMesh(name);
            MeshUtil.SaveMeshAsset(mesh, $"{GenFolder}/Buildings/Bld_{b.id}.asset");
            var mats = new[] { Mat(facadeMat), Mat("concrete_rough"), Mat("roof_flat"), Mat("concrete_smooth") };
            if (mesh.subMeshCount < mats.Length) { var mm = new Material[mesh.subMeshCount]; Array.Copy(mats, mm, mm.Length); mats = mm; }
            MeshUtil.MakeStatic(name, mesh, mats, TileRoot(TileName(MeshUtil.Centroid(ring))), true);
        }

        static string Sanitize(string s)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var ch in s) sb.Append(char.IsLetterOrDigit(ch) ? ch : '_');
            return sb.Length > 40 ? sb.ToString(0, 40) : sb.ToString();
        }
    }
}
