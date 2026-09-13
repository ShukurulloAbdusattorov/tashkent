using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AmirTemur.Editor
{
    /// <summary>Equestrian monument of Amir Temur (1994) at the origin: 3 circular granite steps, a stepped reddish-brown granite pedestal
    /// with bronze inscription plaques, and the bronze horse (Poly Haven horse_statue_01, scaled) with a procedurally built rider.</summary>
    public static class AmirTemurMonument
    {
        const float StepRise = 0.16f, StepTread = 0.45f, PlatformRadius = 9.0f;
        const float HorseHeight = 5.2f;   // metres, statue scale (rider + horse ~ 6 m over the pedestal)

        public static void Build(CityData city, Transform parent)
        {
            var c = Vector3.zero;
            if (city.landmarks.TryGetValue("monument", out var lm)) c = new Vector3(lm.center.x, 0, lm.center.y);
            var go = new GameObject("AmirTemurMonument"); go.transform.SetParent(parent, false);

            var part = new Part();
            int tread = part.Sub("paving_granite"), riser = part.Sub("granite_brown"), gr = part.Sub("granite_brown"), br = part.Sub("bronze_statue");

            // --- platform: 3 concentric steps up to a paved circular platform (r = 9 m)
            LK.RoundSteps(part.B, tread, riser, c, PlatformRadius, 3, StepRise, StepTread, 128);
            float top = 3 * StepRise;

            // --- pedestal (long axis east-west; the horse faces west)
            var f = Frame.Along(c + Vector3.up * top, 0f);
            LK.FBox(part.B, gr, f, new Vector3(0, 0.25f, 0), new Vector3(7.4f, 0.5f, 5.4f));
            LK.FBox(part.B, gr, f, new Vector3(0, 0.75f, 0), new Vector3(6.4f, 0.5f, 4.4f));
            LK.FBox(part.B, gr, f, new Vector3(0, 1.15f, 0), new Vector3(5.6f, 0.3f, 3.7f));
            const float mainY0 = 1.3f, mainH = 4.3f;
            LK.FBox(part.B, gr, f, new Vector3(0, mainY0 + mainH * 0.5f, 0), new Vector3(5.0f, mainH, 3.2f));
            // slight chamfered crown block and top slab
            LK.FBox(part.B, gr, f, new Vector3(0, mainY0 + mainH + 0.15f, 0), new Vector3(5.4f, 0.3f, 3.6f));
            LK.FBox(part.B, gr, f, new Vector3(0, mainY0 + mainH + 0.42f, 0), new Vector3(5.1f, 0.24f, 3.3f));
            float pedTop = top + mainY0 + mainH + 0.54f;

            // bronze inscription plaques ("Kuch adolatdadir") on all four faces + thin bronze band
            float py = mainY0 + mainH * 0.55f;
            LK.FBox(part.B, br, f, new Vector3(2.53f, py, 0), new Vector3(0.07f, 1.5f, 2.3f));
            LK.FBox(part.B, br, f, new Vector3(-2.53f, py, 0), new Vector3(0.07f, 1.5f, 2.3f));
            LK.FBox(part.B, br, f, new Vector3(0, py, 1.63f), new Vector3(3.4f, 1.5f, 0.07f));
            LK.FBox(part.B, br, f, new Vector3(0, py, -1.63f), new Vector3(3.4f, 1.5f, 0.07f));
            // raised letter rows (thin bars) to catch light on the plaques
            for (int i = 0; i < 3; i++)
            {
                float ly = py + 0.42f - i * 0.42f;
                LK.FBox(part.B, br, f, new Vector3(2.58f, ly, 0), new Vector3(0.03f, 0.18f, 1.7f));
                LK.FBox(part.B, br, f, new Vector3(-2.58f, ly, 0), new Vector3(0.03f, 0.18f, 1.7f));
            }
            // low polished plinth kerb ring around the platform edge (0.35 m) keeps the flower beds off the paving
            LK.Cylinder(part.B, riser, c + Vector3.up * top, PlatformRadius + 0.05f, PlatformRadius + 0.05f, 0.12f, 128, true, false);
            part.Flush("Monument_Pedestal", go.transform);

            // --- statue
            var statueBase = c + Vector3.up * pedTop;
            var forward = Vector3.left; // faces west, down the square's main axis
            if (!BuildPrefabHorse(go.transform, statueBase, forward, out var seat, out float k))
            {
                Debug.LogWarning("[Landmarks] horse_statue prefab missing; building the horse from primitives");
                BuildFallbackHorse(go.transform, statueBase, forward, out seat, out k);
            }
            BuildRider(go.transform, seat, forward, k);
        }

        // ------------------------------------------------------------------ horse from the Poly Haven prefab
        static bool BuildPrefabHorse(Transform parent, Vector3 basePos, Vector3 forward, out Vector3 seat, out float k)
        {
            seat = basePos; k = 1.8f;
            var prefab = PrefabLibrary.Get("horse_statue");
            bool rawModel = false;
            if (prefab == null)
            {
                foreach (var p in new[] { "Assets/AmirTemur/Art/Models/horse_statue_01/horse_statue_01.fbx", "Assets/AmirTemur/Art/horse_statue_01.fbx" })
                {
                    prefab = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                    if (prefab != null) { rawModel = true; break; }
                }
            }
            if (prefab == null)
            {
                foreach (var guid in AssetDatabase.FindAssets("horse_statue_01 t:Model"))
                {
                    prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                    if (prefab != null) { rawModel = true; break; }
                }
            }
            if (prefab == null) return false;

            var inst = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            if (inst == null) inst = Object.Instantiate(prefab);
            if (inst == null) return false;
            inst.name = "HorseStatue";
            inst.transform.SetParent(parent, false);
            inst.transform.localPosition = Vector3.zero; inst.transform.localRotation = Quaternion.identity; inst.transform.localScale = Vector3.one;

            // measure the model in its own space
            var filters = inst.GetComponentsInChildren<MeshFilter>();
            if (filters.Length == 0) { Object.DestroyImmediate(inst); return false; }
            bool any = false; var lb = new Bounds();
            var toInst = inst.transform.worldToLocalMatrix;
            foreach (var mf in filters)
            {
                if (mf.sharedMesh == null) continue;
                var m = toInst * mf.transform.localToWorldMatrix; var mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3((i & 1) == 0 ? mb.min.x : mb.max.x, (i & 2) == 0 ? mb.min.y : mb.max.y, (i & 4) == 0 ? mb.min.z : mb.max.z);
                    var w = m.MultiplyPoint3x4(corner);
                    if (!any) { lb = new Bounds(w, Vector3.zero); any = true; } else lb.Encapsulate(w);
                }
            }
            if (!any || lb.size.y < 1e-4f) { Object.DestroyImmediate(inst); return false; }

            float height = lb.size.y, length = Mathf.Max(lb.size.x, lb.size.z);
            float scale = Mathf.Min(HorseHeight / height, 4.6f / Mathf.Max(0.01f, length));
            // Poly Haven horse_statue_01 is a rearing horse whose hind legs stand at local +x: it faces local -x.
            // (If the model were replaced by one facing +z the rider would still sit at the bounds centre.)
            Vector3 modelForward = lb.size.x >= lb.size.z ? -Vector3.right : Vector3.forward;
            var rot = Quaternion.AngleAxis(Vector3.SignedAngle(modelForward, forward, Vector3.up), Vector3.up);
            var offset = new Vector3(lb.center.x, lb.min.y, lb.center.z);
            inst.transform.rotation = rot;
            inst.transform.localScale = Vector3.one * scale;
            inst.transform.position = basePos - rot * (offset * scale);

            foreach (var t in inst.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic | StaticEditorFlags.ContributeGI);
            foreach (var mf in filters)
            {
                if (mf.sharedMesh == null || mf.GetComponent<Collider>() != null) continue;
                var mc = mf.gameObject.AddComponent<MeshCollider>(); mc.sharedMesh = mf.sharedMesh;
            }
            foreach (var r in inst.GetComponentsInChildren<Renderer>())
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                // the Poly Haven statue ships as white stone; the real monument is bronze
                { var mats = r.sharedMaterials; for (int i = 0; i < mats.Length; i++) mats[i] = MaterialLibrary.Get("bronze_statue"); r.sharedMaterials = mats; }
            }

            // saddle: horizontal centre of the model, ~64% of its height (measured on the figurine's back profile)
            var seatLocal = new Vector3(lb.center.x, lb.min.y + 0.64f * height, lb.center.z);
            seat = inst.transform.TransformPoint(seatLocal);
            k = Mathf.Clamp(height * scale / 2.8f, 1.4f, 2.4f); // statue-to-life factor (rearing horse ~2.8 m at the head)
            return true;
        }

        // ------------------------------------------------------------------ fallback horse from primitives
        static void BuildFallbackHorse(Transform parent, Vector3 basePos, Vector3 forward, out Vector3 seat, out float k)
        {
            k = HorseHeight / 2.8f;
            var part = new Part(); int br = part.Sub("bronze_statue");
            var f = new Frame(basePos, forward, Vector3.Cross(Vector3.up, forward));
            float kk = k;
            Vector3 P(float x, float y, float z) => f.P(x * kk, (0.3f + y) * kk, z * kk);
            var b = part.B;
            // bronze base plate
            LK.FBox(b, br, f, new Vector3(0, 0.15f * kk, 0), new Vector3(4.0f * kk, 0.3f * kk, 2.0f * kk));
            // body (rearing: hips low at the back, chest high at the front)
            var hips = P(-0.9f, 1.45f, 0); var chest = P(0.9f, 2.55f, 0);
            LK.Capsule(b, br, hips, chest - hips, 0.42f * kk, (chest - hips).magnitude, 24, 6);
            // neck + head
            LK.Limb(b, br, P(0.75f, 2.65f, 0), P(1.6f, 3.85f, 0), 0.42f * kk, 0.3f * kk);
            LK.Limb(b, br, P(1.55f, 3.95f, 0), P(2.35f, 3.7f, 0), 0.3f * kk, 0.26f * kk);
            LK.Limb(b, br, P(1.5f, 3.95f, 0.08f), P(1.45f, 4.25f, 0.09f), 0.07f * kk); // ears
            LK.Limb(b, br, P(1.5f, 3.95f, -0.08f), P(1.45f, 4.25f, -0.09f), 0.07f * kk);
            // mane ridge
            LK.Limb(b, br, P(0.7f, 2.9f, 0), P(1.45f, 4.0f, 0), 0.12f * kk, 0.3f * kk);
            for (int s = -1; s <= 1; s += 2)
            {
                float z = 0.32f * s;
                // hind legs on the plate
                LK.Limb(b, br, P(-0.8f, 1.5f, z * 0.9f), P(-1.05f, 0.85f, z), 0.2f * kk);
                LK.Limb(b, br, P(-1.05f, 0.85f, z), P(-0.75f, 0.05f, z), 0.15f * kk);
                LK.FBox(b, br, f, new Vector3(-0.72f * kk, (0.3f + 0.06f) * kk, z * kk), new Vector3(0.28f * kk, 0.12f * kk, 0.2f * kk));
                // fore legs raised and folded
                LK.Limb(b, br, P(0.75f, 2.25f, z * 0.9f), P(1.35f, 1.85f, z), 0.18f * kk);
                LK.Limb(b, br, P(1.35f, 1.85f, z), P(1.05f, 1.35f, z), 0.13f * kk);
            }
            // tail
            LK.Limb(b, br, P(-1.3f, 1.75f, 0), P(-1.95f, 0.5f, 0), 0.22f * kk, 0.16f * kk);
            part.Flush("Monument_HorseFallback", parent, true);
            seat = P(0.0f, 2.45f, 0);
        }

        // ------------------------------------------------------------------ rider
        /// <summary>Timur seated, right arm raised forward, cloak flowing behind. Sizes are life-size metres multiplied by k.</summary>
        static void BuildRider(Transform parent, Vector3 seat, Vector3 forward, float k)
        {
            var part = new Part(); int br = part.Sub("bronze_statue"); var b = part.B;
            var right = Vector3.Cross(Vector3.up, forward).normalized;
            var f = new Frame(seat, forward, right);
            Vector3 P(float x, float y, float z) => f.P(x * k, y * k, z * k);
            void KBox(Vector3 lc, Vector3 s) => LK.FBox(b, br, f, lc * k, s * k);

            // saddle + pelvis
            KBox(new Vector3(0.05f, 0.03f, 0), new Vector3(0.75f, 0.14f, 0.62f));
            KBox(new Vector3(-0.28f, 0.16f, 0), new Vector3(0.14f, 0.28f, 0.5f)); // cantle
            KBox(new Vector3(0.0f, 0.2f, 0), new Vector3(0.36f, 0.3f, 0.44f));
            // torso (lower + chest), leaning slightly back
            KBox(new Vector3(-0.02f, 0.5f, 0), new Vector3(0.32f, 0.36f, 0.42f));
            KBox(new Vector3(-0.04f, 0.82f, 0), new Vector3(0.36f, 0.36f, 0.52f));
            KBox(new Vector3(0.0f, 0.36f, 0), new Vector3(0.38f, 0.08f, 0.46f)); // belt
            // rounded shoulders
            LK.Sphere(b, br, P(-0.04f, 0.98f, 0.27f), 0.13f * k, 14, 8);
            LK.Sphere(b, br, P(-0.04f, 0.98f, -0.27f), 0.13f * k, 14, 8);
            // neck + head + helmet/crown
            LK.Cylinder(b, br, P(-0.03f, 1.0f, 0), 0.07f * k, 0.07f * k, 0.12f * k, 12, false, false);
            LK.Sphere(b, br, P(-0.02f, 1.24f, 0), 0.13f * k, 18, 12);
            LK.Limb(b, br, P(0.06f, 1.14f, 0), P(0.12f, 1.02f, 0), 0.09f * k, 0.12f * k); // beard
            var helmet = new List<Vector2> { new Vector2(0.15f * k, 0), new Vector2(0.16f * k, 0.06f * k), new Vector2(0.13f * k, 0.16f * k), new Vector2(0.06f * k, 0.25f * k), new Vector2(0.0f, 0.3f * k) };
            LK.Lathe(b, br, P(-0.02f, 1.27f, 0), Vector3.up, helmet, 18);
            LK.Cylinder(b, br, P(-0.02f, 1.55f, 0), 0.025f * k, 0.01f * k, 0.12f * k, 8, true, false); // crown spike
            // cloak: trapezoid hanging from the shoulders down over the horse's croup (profile in the sideways/up plane, extruded backwards)
            var g = f.Rot90(-0.2f * k, 0, 0);
            var cloak = new List<Vector2> { new Vector2(-0.3f * k, 0.98f * k), new Vector2(0.3f * k, 0.98f * k), new Vector2(0.55f * k, -0.35f * k), new Vector2(-0.55f * k, -0.35f * k) };
            LK.Prism(b, br, g, cloak, 0, 0.14f * k);
            // legs along the flanks
            for (int s = -1; s <= 1; s += 2)
            {
                float z = s * 0.2f;
                LK.Limb(b, br, P(0.05f, 0.15f, z), P(0.45f, -0.15f, z * 1.6f), 0.17f * k, 0.15f * k);   // thigh
                LK.Limb(b, br, P(0.45f, -0.15f, z * 1.7f), P(0.4f, -0.72f, z * 1.75f), 0.12f * k);     // shin
                KBox(new Vector3(0.48f, -0.78f, z * 1.75f), new Vector3(0.3f, 0.1f, 0.12f));              // boot
                KBox(new Vector3(0.44f, -0.76f, z * 1.75f), new Vector3(0.14f, 0.26f, 0.16f));           // stirrup
            }
            // right arm raised forward (statue's gesture), left hand on the reins
            LK.Limb(b, br, P(-0.02f, 0.96f, 0.32f), P(0.3f, 1.12f, 0.4f), 0.12f * k);
            LK.Limb(b, br, P(0.3f, 1.12f, 0.4f), P(0.6f, 1.5f, 0.32f), 0.1f * k);
            LK.Sphere(b, br, P(0.63f, 1.55f, 0.31f), 0.07f * k, 10, 6);
            LK.Limb(b, br, P(-0.02f, 0.96f, -0.32f), P(0.12f, 0.6f, -0.36f), 0.12f * k);
            LK.Limb(b, br, P(0.12f, 0.6f, -0.36f), P(0.42f, 0.5f, -0.2f), 0.1f * k);
            LK.Sphere(b, br, P(0.45f, 0.5f, -0.18f), 0.07f * k, 10, 6);
            // reins from the left hand forward-down to the horse's head
            LK.Limb(b, br, P(0.45f, 0.5f, -0.15f), P(1.3f, 0.9f, -0.12f), 0.03f * k);
            LK.Limb(b, br, P(0.45f, 0.5f, 0.15f), P(1.3f, 0.9f, 0.12f), 0.03f * k);

            part.Flush("Monument_Rider", parent, false);
        }
    }
}
