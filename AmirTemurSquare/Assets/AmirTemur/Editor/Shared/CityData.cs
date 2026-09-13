using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace AmirTemur.Editor
{
    /// <summary>Typed view over Assets/AmirTemur/Data/city.json (local metres, origin = monument, x east, z north).</summary>
    public class CityData
    {
        public class Building { public long id; public string name = "", type = "yes", roof = "flat", colour = "", material = ""; public float height, minHeight; public int levels; public bool part; public List<Vector2> outer = new(); public List<List<Vector2>> inners = new(); }
        public class Road { public long id; public string cls = "", name = "", surface = "asphalt"; public float width; public int lanes; public bool oneway, bridge, tunnel; public int layer; public List<Vector2> pts = new(); }
        public class Path { public long id; public string cls = "", surface = ""; public float width; public int layer; public bool tunnel; public List<Vector2> pts = new(); }
        public class Area { public long id; public string kind = "", name = ""; public int layer; public List<Vector2> outer = new(); public List<List<Vector2>> inners = new(); }
        public class Tree { public Vector2 pos; public string leafType = "", genus = ""; }
        public class Bench { public Vector2 pos; public float yaw; public bool backrest; }
        public class Line { public long id; public float height; public List<Vector2> pts = new(); }
        public class Poi { public string kind = "", name = ""; public Vector2 pos; public Dictionary<string, string> tags = new(); }
        public class Stop { public Vector2 pos; public string name = ""; }
        public class Landmark { public long id; public Vector2 center; public float height; }

        public List<Building> buildings = new();
        public List<Road> roads = new();
        public List<Path> paths = new();
        public List<Area> areas = new();
        public List<Tree> trees = new();
        public List<Bench> benches = new();
        public List<Vector2> bins = new(), lamps = new(), crossings = new(), signals = new();
        public List<Line> kerbs = new(), fences = new(), hedges = new(), walls = new();
        public List<Poi> pois = new();
        public List<Stop> stops = new();
        public Dictionary<string, Landmark> landmarks = new();

        public const string DefaultPath = "Assets/AmirTemur/Data/city.json";
        static CityData _cached;
        public static CityData Load(string path = DefaultPath)
        {
            if (_cached != null) return _cached;
            var full = System.IO.Path.Combine(Directory.GetCurrentDirectory(), path);
            var j = JObject.Parse(File.ReadAllText(full));
            var d = new CityData();
            foreach (var b in j["buildings"]) d.buildings.Add(new Building { id = (long)b["id"], name = S(b, "name"), type = S(b, "type", "yes"), roof = S(b, "roof", "flat"), colour = S(b, "colour"), material = S(b, "material"), height = F(b, "height"), minHeight = F(b, "min_height"), levels = (int)F(b, "levels"), part = b["part"] != null && (bool)b["part"], outer = Ring(b["outer"]), inners = Rings(b["inners"]) });
            foreach (var r in j["roads"]) d.roads.Add(new Road { id = (long)r["id"], cls = S(r, "cls"), name = S(r, "name"), surface = S(r, "surface", "asphalt"), width = F(r, "width"), lanes = (int)F(r, "lanes"), oneway = B(r, "oneway"), bridge = B(r, "bridge"), tunnel = B(r, "tunnel"), layer = (int)F(r, "layer"), pts = Ring(r["pts"]) });
            foreach (var p in j["paths"]) d.paths.Add(new Path { id = (long)p["id"], cls = S(p, "cls"), surface = S(p, "surface"), width = F(p, "width"), layer = (int)F(p, "layer"), tunnel = B(p, "tunnel"), pts = Ring(p["pts"]) });
            foreach (var a in j["areas"]) d.areas.Add(new Area { id = (long)a["id"], kind = S(a, "kind"), name = S(a, "name"), layer = (int)F(a, "layer"), outer = Ring(a["outer"]), inners = Rings(a["inners"]) });
            foreach (var t in j["trees"]) d.trees.Add(new Tree { pos = new Vector2((float)t[0], (float)t[1]), leafType = t.Count() > 2 ? (string)t[2] : "", genus = t.Count() > 3 ? (string)t[3] : "" });
            foreach (var b in j["benches"]) d.benches.Add(new Bench { pos = new Vector2(F(b, "x"), F(b, "z")), yaw = F(b, "yaw"), backrest = B(b, "backrest") });
            foreach (var b in j["bins"]) d.bins.Add(new Vector2(F(b, "x"), F(b, "z")));
            foreach (var b in j["lamps"]) d.lamps.Add(new Vector2(F(b, "x"), F(b, "z")));
            foreach (var c in j["crossings"]) d.crossings.Add(new Vector2((float)c[0], (float)c[1]));
            foreach (var c in j["signals"]) d.signals.Add(new Vector2((float)c[0], (float)c[1]));
            foreach (var k in j["kerbs"]) d.kerbs.Add(new Line { id = (long)k["id"], height = F(k, "height"), pts = Ring(k["pts"]) });
            foreach (var k in j["fences"]) d.fences.Add(new Line { id = (long)k["id"], height = F(k, "height"), pts = Ring(k["pts"]) });
            foreach (var k in j["hedges"]) d.hedges.Add(new Line { id = (long)k["id"], height = F(k, "height"), pts = Ring(k["pts"]) });
            foreach (var k in j["walls"]) d.walls.Add(new Line { id = (long)k["id"], height = F(k, "height"), pts = Ring(k["pts"]) });
            foreach (var p in j["pois"])
            {
                var poi = new Poi { kind = S(p, "kind"), name = S(p, "name"), pos = new Vector2(F(p, "x"), F(p, "z")) };
                if (p["tags"] is JObject to) foreach (var kv in to) poi.tags[kv.Key] = kv.Value.ToString();
                d.pois.Add(poi);
            }
            foreach (var s in j["stops"]) d.stops.Add(new Stop { pos = new Vector2(F(s, "x"), F(s, "z")), name = S(s, "name") });
            foreach (var kv in (JObject)j["landmarks"])
            {
                var o = (JObject)kv.Value; var c = o["center"];
                d.landmarks[kv.Key] = new Landmark { id = o["id"] != null ? (long)o["id"] : 0, center = new Vector2((float)c[0], (float)c[1]), height = F(o, "height") };
            }
            _cached = d;
            return d;
        }
        public static void ClearCache() => _cached = null;

        static string S(JToken t, string k, string def = "") => t[k] == null || t[k].Type == JTokenType.Null ? def : (string)t[k];
        static float F(JToken t, string k) => t[k] == null || t[k].Type == JTokenType.Null ? 0f : (float)t[k];
        static bool B(JToken t, string k) => t[k] != null && t[k].Type == JTokenType.Boolean && (bool)t[k];
        static List<Vector2> Ring(JToken arr)
        {
            var l = new List<Vector2>(); if (arr == null) return l;
            foreach (var p in arr) l.Add(new Vector2((float)p[0], (float)p[1]));
            return l;
        }
        static List<List<Vector2>> Rings(JToken arr)
        {
            var l = new List<List<Vector2>>(); if (arr == null) return l;
            foreach (var r in arr) l.Add(Ring(r));
            return l;
        }

        public Building FindBuilding(long id) => buildings.Find(b => b.id == id);
    }
}
