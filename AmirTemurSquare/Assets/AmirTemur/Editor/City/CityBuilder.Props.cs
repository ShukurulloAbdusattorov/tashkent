using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace AmirTemur.Editor
{
    public static partial class CityBuilder
    {
        // ------------------------------------------------------------------ prop placement (prefab or procedural fallback)
        static GameObject Prop(string key, Vector2 pos, float y, float yawDeg, float scale)
        {
            var parent = PropTileRoot(pos);
            var go = PrefabLibrary.Place(key, XZ(pos, y), yawDeg, scale, parent);
            if (go == null) { go = FallbackProp(key, parent); if (go == null) return null; go.transform.SetPositionAndRotation(XZ(pos, y), Quaternion.Euler(0, yawDeg, 0)); go.transform.localScale = Vector3.one * scale; Count("fallbackProps"); }
            Count("prop:" + key);
            return go;
        }

        static GameObject Prim(PrimitiveType type, Transform parent, Vector3 localPos, Vector3 localScale, string mat, bool collider = false)
        {
            var go = GameObject.CreatePrimitive(type);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos; go.transform.localScale = localScale;
            go.GetComponent<MeshRenderer>().sharedMaterial = Mat(mat);
            if (!collider) { var c = go.GetComponent<Collider>(); if (c != null) UnityEngine.Object.DestroyImmediate(c); }
            return go;
        }

        /// <summary>Simple procedural stand-ins used when a prefab is missing.</summary>
        static GameObject FallbackProp(string key, Transform parent)
        {
            var go = new GameObject(key + "_fallback");
            go.transform.SetParent(parent, false);
            switch (key)
            {
                case "tree_jacaranda": case "tree_small": case "tree_island": case "fir_sapling":
                    {
                        float h = key == "tree_small" ? 5f : key == "fir_sapling" ? 4f : 8f;
                        Prim(PrimitiveType.Cylinder, go.transform, new Vector3(0, h * 0.3f, 0), new Vector3(0.35f, h * 0.3f, 0.35f), "wood", true);
                        Prim(PrimitiveType.Sphere, go.transform, new Vector3(0, h * 0.7f, 0), Vector3.one * h * 0.55f, "grass#foliage");
                        Prim(PrimitiveType.Sphere, go.transform, new Vector3(h * 0.12f, h * 0.9f, h * 0.08f), Vector3.one * h * 0.4f, "grass#foliage");
                        break;
                    }
                case "shrub_02": case "shrub_03": case "shrub_04":
                    Prim(PrimitiveType.Sphere, go.transform, new Vector3(0, 0.45f, 0), new Vector3(1.1f, 0.9f, 1.1f), "grass#foliage");
                    break;
                case "lamp_01": case "lamp_02":
                    Prim(PrimitiveType.Cylinder, go.transform, new Vector3(0, 2.4f, 0), new Vector3(0.12f, 2.4f, 0.12f), "metal_dark", true);
                    Prim(PrimitiveType.Cube, go.transform, new Vector3(0, 4.6f, 0.3f), new Vector3(0.4f, 0.2f, 0.8f), "window_lit");
                    break;
                case "bench_wood": case "bench_modern":
                    Prim(PrimitiveType.Cube, go.transform, new Vector3(0, 0.42f, 0), new Vector3(1.8f, 0.06f, 0.5f), "wood", true);
                    Prim(PrimitiveType.Cube, go.transform, new Vector3(0, 0.7f, -0.22f), new Vector3(1.8f, 0.5f, 0.05f), "wood");
                    Prim(PrimitiveType.Cube, go.transform, new Vector3(-0.75f, 0.2f, 0), new Vector3(0.08f, 0.4f, 0.45f), "metal_dark");
                    Prim(PrimitiveType.Cube, go.transform, new Vector3(0.75f, 0.2f, 0), new Vector3(0.08f, 0.4f, 0.45f), "metal_dark");
                    break;
                case "trash_can":
                    Prim(PrimitiveType.Cylinder, go.transform, new Vector3(0, 0.45f, 0), new Vector3(0.45f, 0.45f, 0.45f), "metal_dark", true);
                    break;
                case "hydrant":
                    Prim(PrimitiveType.Cylinder, go.transform, new Vector3(0, 0.35f, 0), new Vector3(0.22f, 0.35f, 0.22f), "metal_painted#red", true);
                    Prim(PrimitiveType.Sphere, go.transform, new Vector3(0, 0.72f, 0), Vector3.one * 0.26f, "metal_painted#red");
                    break;
                case "manhole":
                    Prim(PrimitiveType.Cylinder, go.transform, new Vector3(0, 0.01f, 0), new Vector3(0.65f, 0.01f, 0.65f), "metal_dark");
                    break;
                case "planter":
                    Prim(PrimitiveType.Cube, go.transform, new Vector3(0, 0.3f, 0), new Vector3(1.4f, 0.6f, 0.6f), "concrete_smooth", true);
                    Prim(PrimitiveType.Sphere, go.transform, new Vector3(0, 0.8f, 0), new Vector3(1.2f, 0.7f, 0.5f), "grass#foliage");
                    break;
                default:
                    UnityEngine.Object.DestroyImmediate(go); return null;
            }
            var flags = StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic | StaticEditorFlags.ContributeGI;
            GameObjectUtility.SetStaticEditorFlags(go, flags);
            foreach (Transform ch in go.transform) GameObjectUtility.SetStaticEditorFlags(ch.gameObject, flags);
            return go;
        }

        static bool Blocked(Vector2 p, float roadMargin = 0.3f, float buildingMargin = 0.5f)
            => Roads.Inside(p, roadMargin) || BuildingIndex.Contains(p, buildingMargin);

        // ------------------------------------------------------------------ props
        static void BuildProps()
        {
            // trees from data
            foreach (var tr in D.trees)
            {
                try
                {
                    string key;
                    string leaf = (tr.leafType ?? "").ToLowerInvariant();
                    float scale = R(0.85f, 1.25f);
                    if (leaf == "row") key = "tree_small";
                    else if (leaf == "needleleaved") { key = "fir_sapling"; scale = R(1.4f, 2.2f); }
                    else { double u = Rng.NextDouble(); key = u < 0.6 ? "tree_jacaranda" : u < 0.85 ? "tree_small" : "tree_island"; }
                    Prop(key, tr.pos, 0f, R(0f, 360f), scale);
                }
                catch (Exception e) { Debug.LogWarning($"[CityBuilder] tree at {tr.pos} failed: {e.Message}"); }
            }
            // trees inside wood polygons
            foreach (var p in WoodTreeSpots)
            {
                try
                {
                    double u = Rng.NextDouble(); string key = u < 0.6 ? "tree_jacaranda" : u < 0.85 ? "tree_small" : "tree_island";
                    Prop(key, p, GrassY - 0.02f, R(0f, 360f), R(0.85f, 1.25f));
                }
                catch (Exception e) { Debug.LogWarning($"[CityBuilder] wood tree at {p} failed: {e.Message}"); }
            }
            // benches, bins
            foreach (var b in D.benches) { try { Prop(Rng.NextDouble() < 0.7 ? "bench_wood" : "bench_modern", b.pos, 0f, b.yaw, 1f); } catch (Exception e) { Debug.LogWarning($"[CityBuilder] bench failed: {e.Message}"); } }
            foreach (var b in D.bins) { try { Prop("trash_can", b, 0f, R(0f, 360f), 1f); } catch (Exception e) { Debug.LogWarning($"[CityBuilder] bin failed: {e.Message}"); } }

            // hydrants at ~3% of road vertices, manholes 1 per 60 m
            for (int ri = 0; ri < D.roads.Count; ri++)
            {
                var line = Roads.Lines[ri]; if (line == null || D.roads[ri].bridge) continue;
                float hw = Roads.RoadHalfW[ri];
                try
                {
                    for (int i = 0; i < line.Count; i++)
                    {
                        if (Rng.NextDouble() >= 0.03) continue;
                        Vector2 d = i + 1 < line.Count ? (line[i + 1] - line[i]).normalized : (line[i] - line[i - 1]).normalized;
                        float side = Rng.NextDouble() < 0.5 ? -1f : 1f;
                        var p = line[i] + Perp(d) * side * (hw + KerbW + 0.8f);
                        if (Blocked(p)) continue;
                        Prop("hydrant", p, 0f, R(0f, 360f), 1f);
                    }
                    float len = Length(line);
                    for (float s = R(10f, 50f); s < len; s += 60f)
                    {
                        var p = PointAt(line, s, out var d);
                        var q = p + Perp(d) * R(-(hw - 1f), hw - 1f);
                        if (Roads.Inside(q, -0.6f, ri)) continue; // avoid junction overlaps
                        Prop("manhole", q, RoadSurfaceY(D.roads[ri]) + 0.004f, R(0f, 360f), 1f);
                    }
                }
                catch (Exception e) { Debug.LogWarning($"[CityBuilder] road props {D.roads[ri].id} failed: {e.Message}"); }
            }

            // shrubs along grass rims (1 per ~12 m), capped
            int shrubs = 0; string[] shrubKeys = { "shrub_02", "shrub_03", "shrub_04" };
            foreach (var ring in GrassRings)
            {
                if (shrubs > 2500) break;
                try
                {
                    var inner = MeshUtil.OffsetRing(ring, -0.8f); if (!ValidRing(inner, 2f)) continue;
                    var closed = new List<Vector2>(inner) { inner[0] };
                    float len = Length(closed); int perRing = 0;
                    for (float s = R(2f, 10f); s < len && perRing < 60; s += R(9f, 15f))
                    {
                        var p = PointAt(closed, s, out _);
                        if (!MeshUtil.PointInPolygon(p, ring) || Blocked(p, 0.5f, 0.5f)) continue;
                        Prop(shrubKeys[RI(0, 2)], p, GrassY - 0.02f, R(0f, 360f), R(0.8f, 1.3f));
                        shrubs++; perRing++;
                    }
                }
                catch (Exception e) { Debug.LogWarning($"[CityBuilder] shrubs failed: {e.Message}"); }
            }

            // planters at 8 random plaza spots
            if (PlazaAreas.Count > 0)
            {
                int made = 0, tries = 0;
                while (made < 8 && tries++ < 200)
                {
                    var a = PlazaAreas[RI(0, PlazaAreas.Count - 1)];
                    var ring = MeshUtil.Clean(a.outer); if (!ValidRing(ring, 20f)) continue;
                    if (!RandomPointIn(Rng, ring, a.inners, BBox(ring), 10, out var p)) continue;
                    if (Blocked(p, 1f, 1f)) continue;
                    Prop("planter", p, PlazaY, R(0f, 360f), 1f); made++;
                }
            }
        }

        // ------------------------------------------------------------------ lamps + lights
        static void BuildLamps()
        {
            int n = 0;
            foreach (var l in D.lamps) { try { if (Lamp("lamp_02", l, 0f, R(0f, 360f))) n++; } catch (Exception e) { Debug.LogWarning($"[CityBuilder] lamp failed: {e.Message}"); } }

            // every ~28 m along non-service roads, both sides, at kerb + 0.6 m
            for (int ri = 0; ri < D.roads.Count; ri++)
            {
                var r = D.roads[ri]; var line = Roads.Lines[ri];
                if (line == null || r.bridge || r.cls == "service") continue;
                try
                {
                    float hw = Roads.RoadHalfW[ri]; float len = Length(line);
                    for (float s = 14f; s < len - 4f; s += 28f)
                    {
                        var p = PointAt(line, s, out var d);
                        for (int side = -1; side <= 1; side += 2)
                        {
                            var q = p + Perp(d) * side * (hw + KerbW + 0.6f);
                            if (Roads.Inside(q, 0.3f, ri) || BuildingIndex.Contains(q, 0.8f)) continue;
                            var face = -Perp(d) * side; // look towards the road
                            if (Lamp("lamp_02", q, 0f, YawDeg(face))) n++;
                        }
                    }
                }
                catch (Exception e) { Debug.LogWarning($"[CityBuilder] road lamps {r.id} failed: {e.Message}"); }
            }

            // park footways near the square: every ~22 m, alternating sides
            foreach (var p in D.paths)
            {
                if (p.tunnel || p.cls != "footway") continue;
                try
                {
                    var line = CleanLine(p.pts); if (line.Count < 2) continue;
                    bool near = true; foreach (var v in line) if (v.magnitude > 250f) { near = false; break; }
                    if (!near) continue;
                    float hw = (p.width > 0.5f ? p.width : 2f) * 0.5f; float len = Length(line); int k = 0;
                    for (float s = 11f; s < len - 3f; s += 22f, k++)
                    {
                        var pt = PointAt(line, s, out var d); float side = (k % 2 == 0) ? 1f : -1f;
                        var q = pt + Perp(d) * side * (hw + 0.5f);
                        if (Blocked(q, 0.5f, 0.5f)) continue;
                        if (Lamp("lamp_01", q, 0f, YawDeg(-Perp(d) * side))) n++;
                    }
                }
                catch (Exception e) { Debug.LogWarning($"[CityBuilder] path lamps {p.id} failed: {e.Message}"); }
            }
            Count("lamps", n);
        }

        static bool Lamp(string key, Vector2 pos, float y, float yaw)
        {
            var go = Prop(key, pos, y, yaw, 1f);
            if (go == null) return false;
            AddStreetLight(go.transform, 4.5f);
            return true;
        }

        /// <summary>Warm HDRP spot light at the lamp head; the object is named "StreetLight" so a runtime day/night script can toggle it.</summary>
        static void AddStreetLight(Transform lamp, float headHeight)
        {
            var go = new GameObject("StreetLight");
            go.transform.SetParent(lamp, false);
            go.transform.localPosition = new Vector3(0f, headHeight, 0.3f);
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // point down
            var light = go.AddComponent<Light>();
            light.type = LightType.Spot;
            light.spotAngle = 130f;
            light.innerSpotAngle = 60f;
            light.range = 18f;
            light.shadows = LightShadows.None;
            var hd = go.AddComponent<HDAdditionalLightData>();
            HDAdditionalLightData.InitDefaultHDAdditionalLightData(hd);
            light.lightUnit = UnityEngine.Rendering.LightUnit.Lumen;
            light.intensity = 1500f;
            light.useColorTemperature = true;
            light.colorTemperature = 3000f;
            light.color = Color.white;
            light.range = 18f;
            light.shadows = LightShadows.None;
            hd.affectsVolumetric = false;
            hd.EnableShadows(false);
            hd.SetRange(18f);
            Count("streetLights");
        }

        // ------------------------------------------------------------------ bus stops + subway entrances
        static void BuildStopsAndSubways()
        {
            int nStops = 0, nSubway = 0;
            foreach (var s in D.stops)
            {
                try
                {
                    Vector2 d = Vector2.right, away = Vector2.up;
                    int si = Roads.Nearest(s.pos, 40f, out _, out float t);
                    if (si >= 0)
                    {
                        var sg = Roads.Segs[si]; d = sg.dir;
                        var proj = sg.a + (sg.b - sg.a) * t; var v = s.pos - proj;
                        away = Vector2.Dot(v, Perp(d)) >= 0 ? Perp(d) : -Perp(d);
                    }
                    var c = s.pos;
                    // roof slab 4 x 1.5 at 2.6 m, two rear posts, glass back panel, small bench
                    AddOBox(c + away * 0.3f, d, 4f, 1.5f, 2.5f, 2.62f, "metal_dark", true, true);
                    for (int k = -1; k <= 1; k += 2) AddOBox(c + away * 0.98f + d * (k * 1.9f), d, 0.08f, 0.08f, 0f, 2.5f, "metal_dark", true);
                    AddOBox(c + away * 0.98f, d, 3.7f, 0.03f, 0.3f, 2.3f, "glass", false);
                    AddOBox(c + away * 0.7f, d, 2.4f, 0.4f, 0.4f, 0.46f, "wood", true);
                    nStops++;
                }
                catch (Exception e) { Debug.LogWarning($"[CityBuilder] stop '{s.name}' failed: {e.Message}"); }
            }

            foreach (var poi in D.pois)
            {
                if (poi.kind != "subway_entrance") continue;
                try
                {
                    Vector2 d = Vector2.up;
                    int si = Roads.Nearest(poi.pos, 60f, out _, out _);
                    if (si >= 0) d = Roads.Segs[si].dir;
                    var n = Perp(d); var c = poi.pos;
                    // 6 x 3 m glass pavilion with dark frame, open on one short side
                    for (int kx = -1; kx <= 1; kx += 2) for (int kz = -1; kz <= 1; kz += 2)
                        AddOBox(c + d * (kx * 2.95f) + n * (kz * 1.45f), d, 0.15f, 0.15f, 0f, 3f, "metal_dark", true);
                    AddOBox(c, d, 6.4f, 3.4f, 3f, 3.2f, "metal_dark", true, true);
                    AddOBox(c + n * 1.5f, d, 6f, 0.03f, 0.1f, 3f, "glass", false);
                    AddOBox(c - n * 1.5f, d, 6f, 0.03f, 0.1f, 3f, "glass", false);
                    AddOBox(c - d * 3f, n, 3f, 0.03f, 0.1f, 3f, "glass", false);
                    AddOBox(c, d, 0.6f, 0.6f, 3.2f, 3.6f, "metal_dark", false);
                    AddOBox(c, d, 1.6f, 0.3f, 3.6f, 6.1f, "window_lit#metroM", false);
                    nSubway++;
                }
                catch (Exception e) { Debug.LogWarning($"[CityBuilder] subway entrance '{poi.name}' failed: {e.Message}"); }
            }
            Count("busStops", nStops); Count("subwayEntrances", nSubway);
        }
    }
}
