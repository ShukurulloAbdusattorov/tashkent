using UnityEngine;
using UnityEngine.InputSystem;

namespace AmirTemur
{
    /// <summary>Tiny IMGUI overlay: a controls hint that fades out after a few seconds and an FPS counter toggled with F3.</summary>
    [DisallowMultipleComponent]
    public class PlayerHUD : MonoBehaviour
    {
        [SerializeField] float hintDuration = 8f;
        [SerializeField] float hintFadeTime = 1.5f;
        [SerializeField] bool showFps = false;
        [SerializeField] int fontSize = 15;

        [TextArea(3, 8)]
        [SerializeField] string controlsText =
            "WASD / left stick  move (soft push = walk)\n" +
            "Mouse / right stick  look\n" +
            "Shift / L3  sprint     Space / A  jump\n" +
            "1-4  time of day       Esc  release cursor     F3  FPS";

        float startTime;
        float fpsAccum;
        int fpsFrames;
        float fpsValue;
        float fpsTimer;
        GUIStyle style;
        GUIStyle boxStyle;
        ThirdPersonController controller;

        void Start()
        {
            startTime = Time.unscaledTime;
            controller = GetComponent<ThirdPersonController>();
            if (controller == null) controller = FindFirstObjectByType<ThirdPersonController>();
        }

        void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb != null && kb.f3Key.wasPressedThisFrame) showFps = !showFps;

            fpsAccum += Time.unscaledDeltaTime;
            fpsFrames++;
            fpsTimer += Time.unscaledDeltaTime;
            if (fpsTimer >= 0.5f)
            {
                fpsValue = fpsFrames / Mathf.Max(1e-5f, fpsAccum);
                fpsAccum = 0f; fpsFrames = 0; fpsTimer = 0f;
            }
        }

        void OnGUI()
        {
            if (style == null)
            {
                style = new GUIStyle(GUI.skin.label) { fontSize = fontSize, richText = false };
                style.normal.textColor = Color.white;
                boxStyle = new GUIStyle(GUI.skin.box);
            }

            float elapsed = Time.unscaledTime - startTime;
            float alpha = 1f;
            if (elapsed > hintDuration) alpha = 1f - Mathf.Clamp01((elapsed - hintDuration) / Mathf.Max(0.01f, hintFadeTime));

            if (alpha > 0.001f)
            {
                Color prev = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, alpha);
                var content = new GUIContent(controlsText);
                Vector2 size = style.CalcSize(content);
                var rect = new Rect(16f, Screen.height - size.y - 28f, size.x + 20f, size.y + 12f);
                GUI.color = new Color(0f, 0f, 0f, 0.45f * alpha);
                GUI.Box(rect, GUIContent.none, boxStyle);
                GUI.color = new Color(1f, 1f, 1f, alpha);
                GUI.Label(new Rect(rect.x + 10f, rect.y + 6f, size.x + 4f, size.y + 4f), content, style);
                GUI.color = prev;
            }

            if (showFps)
            {
                string text = fpsValue.ToString("0") + " fps   " + (1000f / Mathf.Max(1f, fpsValue)).ToString("0.0") + " ms";
                if (controller != null)
                {
                    text += "\n" + controller.CurrentSpeed.ToString("0.0") + " m/s" +
                            (controller.IsGrounded ? "" : (controller.IsFalling ? "  falling" : "  airborne")) +
                            (controller.UsingRootMotion ? "" : "  [kinematic]");
                }
                var content = new GUIContent(text);
                Vector2 size = style.CalcSize(content);
                var rect = new Rect(Screen.width - size.x - 30f, 12f, size.x + 20f, size.y + 12f);
                Color prev = GUI.color;
                GUI.color = new Color(0f, 0f, 0f, 0.45f);
                GUI.Box(rect, GUIContent.none, boxStyle);
                GUI.color = Color.white;
                GUI.Label(new Rect(rect.x + 10f, rect.y + 6f, size.x + 4f, size.y + 4f), content, style);
                GUI.color = prev;
            }
        }
    }
}
