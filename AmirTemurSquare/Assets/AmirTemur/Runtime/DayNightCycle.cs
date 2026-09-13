using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AmirTemur
{
    /// <summary>
    /// Rotates the sun (directional light) from a time of day using simple solar geometry for Tashkent,
    /// drives its intensity (lux) and colour temperature, and switches every "StreetLight" light on when the
    /// sun is below <see cref="streetLightElevation"/>. Keys 1-4 jump to 08:00 / 13:00 / 18:30 / 22:00.
    /// </summary>
    [DisallowMultipleComponent]
    public class DayNightCycle : MonoBehaviour
    {
        [Header("Sun")]
        [SerializeField] Light sun;
        [Range(0f, 24f)] public float hour = 16.5f;
        [Tooltip("Real minutes for a full 24 h cycle. 0 = frozen.")]
        public float dayLengthMinutes = 0f;
        [Tooltip("Apply the time of day immediately at Start (otherwise the scene's authored sun is kept until the cycle runs or a key is pressed).")]
        [SerializeField] bool applyOnStart = true;

        [Header("Solar geometry")]
        [SerializeField] float latitudeDeg = 41.31f;          // Tashkent
        [Range(-23.44f, 23.44f)]
        [SerializeField] float declinationDeg = 12f;           // ~mid April / late August
        [Tooltip("Local clock hour of solar noon (Tashkent, UTC+5, 69.3 E).")]
        [SerializeField] float solarNoonHour = 12.4f;

        [Header("Light")]
        [SerializeField] float dayLux = 100000f;
        [SerializeField] float nightLux = 0f;
        [SerializeField] float horizonTemperature = 2200f;
        [SerializeField] float noonTemperature = 5800f;
        [Tooltip("Sun elevation (deg) below which street lights switch on.")]
        [SerializeField] float streetLightElevation = 3f;

        readonly List<Light> streetLights = new List<Light>();
        bool streetLightsOn;
        bool streetLightsInitialised;
        float lastAppliedHour = float.NaN;

        public float SunElevationDeg { get; private set; }
        public float SunAzimuthDeg { get; private set; }
        public Light Sun { get => sun; set => sun = value; }

        void Start()
        {
            if (sun == null)
            {
                Light[] lights = FindObjectsByType<Light>(FindObjectsSortMode.None);
                foreach (Light l in lights)
                {
                    if (l.type == LightType.Directional) { sun = l; break; }
                }
            }

            streetLights.Clear();
            foreach (Light l in FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (l.type == LightType.Directional) continue;
                if (l.gameObject.name == "StreetLight" || l.gameObject.name.StartsWith("StreetLight")) streetLights.Add(l);
            }

            if (applyOnStart) Apply();
        }

        void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.digit1Key.wasPressedThisFrame) SetHour(8f);
                else if (kb.digit2Key.wasPressedThisFrame) SetHour(13f);
                else if (kb.digit3Key.wasPressedThisFrame) SetHour(18.5f);
                else if (kb.digit4Key.wasPressedThisFrame) SetHour(22f);
            }

            if (dayLengthMinutes > 0f)
            {
                hour += Time.deltaTime / (dayLengthMinutes * 60f) * 24f;
                if (hour >= 24f) hour -= 24f;
                Apply();
            }
            else if (!float.IsNaN(lastAppliedHour) && !Mathf.Approximately(lastAppliedHour, hour))
            {
                Apply(); // inspector change
            }
        }

        public void SetHour(float h)
        {
            hour = Mathf.Repeat(h, 24f);
            Apply();
        }

        /// <summary>Computes the sun direction for the current hour and applies rotation, intensity, colour and street lights.</summary>
        public void Apply()
        {
            lastAppliedHour = hour;
            ComputeSun(hour, out float elevation, out float azimuth);
            SunElevationDeg = elevation;
            SunAzimuthDeg = azimuth;

            if (sun != null)
            {
                float elRad = elevation * Mathf.Deg2Rad, azRad = azimuth * Mathf.Deg2Rad;
                // Direction toward the sun (x east, z north, y up); the light shines along -sunDir.
                var sunDir = new Vector3(Mathf.Sin(azRad) * Mathf.Cos(elRad), Mathf.Sin(elRad), Mathf.Cos(azRad) * Mathf.Cos(elRad));
                sun.transform.rotation = Quaternion.LookRotation(-sunDir, Vector3.up);

                // Intensity: HDRP directional lights are natively in lux (Light.intensity, Unity 6 / HDRP 17).
                float dayFactor = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-3f, 12f, elevation));
                sun.intensity = Mathf.Lerp(nightLux, dayLux, dayFactor);

                sun.useColorTemperature = true;
                float warm = Mathf.InverseLerp(0f, 35f, elevation);
                sun.colorTemperature = Mathf.Lerp(horizonTemperature, noonTemperature, Mathf.Sqrt(Mathf.Clamp01(warm)));
                sun.color = Color.white;
            }

            bool wantLights = elevation < streetLightElevation;
            if (!streetLightsInitialised || wantLights != streetLightsOn)
            {
                streetLightsInitialised = true;
                streetLightsOn = wantLights;
                foreach (Light l in streetLights)
                {
                    if (l != null) l.enabled = wantLights;
                }
            }
        }

        void ComputeSun(float localHour, out float elevationDeg, out float azimuthDeg)
        {
            float lat = latitudeDeg * Mathf.Deg2Rad;
            float dec = declinationDeg * Mathf.Deg2Rad;
            float hourAngle = (localHour - solarNoonHour) * 15f * Mathf.Deg2Rad; // negative in the morning

            float sinEl = Mathf.Sin(lat) * Mathf.Sin(dec) + Mathf.Cos(lat) * Mathf.Cos(dec) * Mathf.Cos(hourAngle);
            float el = Mathf.Asin(Mathf.Clamp(sinEl, -1f, 1f));

            // Azimuth measured from north, clockwise (east = 90).
            float cosEl = Mathf.Cos(el);
            float cosAz = cosEl > 1e-4f ? (Mathf.Sin(dec) - Mathf.Sin(lat) * sinEl) / (Mathf.Cos(lat) * cosEl) : 1f;
            float az = Mathf.Acos(Mathf.Clamp(cosAz, -1f, 1f));
            if (hourAngle > 0f) az = 2f * Mathf.PI - az; // afternoon: sun in the west

            elevationDeg = el * Mathf.Rad2Deg;
            azimuthDeg = az * Mathf.Rad2Deg;
        }
    }
}
