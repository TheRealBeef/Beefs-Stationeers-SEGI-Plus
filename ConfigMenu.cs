using UnityEngine;

namespace BeefsSEGIPlus;

public class ConfigMenu : MonoBehaviour
{
    private bool _showConfig = false;
    private Vector2 _scrollPosition = Vector2.zero;
    private Rect _windowRect = new(20, 20, 550, 700);

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.F11))
        {
            _showConfig = !_showConfig;
            if (_showConfig)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }
    }

    private void OnGUI()
    {
        if (_showConfig)
            _windowRect = GUILayout.Window(0, _windowRect, ConfigWindow, "SEGI Plus Configuration (F11 to close)");
    }

    private void ConfigWindow(int windowID)
    {
        GUILayout.BeginVertical();
        _scrollPosition = GUILayout.BeginScrollView(_scrollPosition, GUILayout.Width(530), GUILayout.Height(650));

        GUILayout.Label("=== SEGI Plus Global Settings ===", GUI.skin.box, GUILayout.ExpandWidth(true));

        var currentEnabled = SEGIPlugin.Enabled.Value;
        var newEnabled = GUILayout.Toggle(currentEnabled, $"Enable SEGI Plus ({currentEnabled})");
        if (newEnabled != currentEnabled) SEGIPlugin.Enabled.Value = newEnabled;

        if (SEGIPlugin.Enabled.Value)
        {
            GUILayout.Space(10);
            GUILayout.Label("=== Quality Level ===", GUI.skin.box, GUILayout.ExpandWidth(true));

            GUILayout.Label($"Quality Level: {SEGIPlugin.QualityLevel.Value} ({ConfigData.GetQualityName()})");
            var newQualityLevel = Mathf.RoundToInt(GUILayout.HorizontalSlider(SEGIPlugin.QualityLevel.Value, 0, 3));
            if (newQualityLevel != SEGIPlugin.QualityLevel.Value) SEGIPlugin.QualityLevel.Value = newQualityLevel;

            GUILayout.Label(ConfigData.GetQualityName(), GUI.skin.box, GUILayout.ExpandWidth(true));

            GUILayout.Space(10);
            GUILayout.Label("=== Day/Night Cycle ===", GUI.skin.box, GUILayout.ExpandWidth(true));

            GUILayout.Label($"Day Ambient Brightness: {SEGIPlugin.DayAmbientBrightness.Value:F3}");
            var newDayBrightness = GUILayout.HorizontalSlider(SEGIPlugin.DayAmbientBrightness.Value, 0.001f, 0.25f);
            if (!Mathf.Approximately(newDayBrightness, SEGIPlugin.DayAmbientBrightness.Value))
                SEGIPlugin.DayAmbientBrightness.Value = newDayBrightness;

            GUILayout.Label($"Night Ambient Brightness: {SEGIPlugin.NightAmbientBrightness.Value:F4}");
            var newNightBrightness = GUILayout.HorizontalSlider(SEGIPlugin.NightAmbientBrightness.Value, 0.000f, 0.01f);
            if (!Mathf.Approximately(newNightBrightness, SEGIPlugin.NightAmbientBrightness.Value))
                SEGIPlugin.NightAmbientBrightness.Value = newNightBrightness;

            GUILayout.Space(10);
            GUILayout.Label("=== Performance ===", GUI.skin.box, GUILayout.ExpandWidth(true));

            var currentLightweight = SEGIPlugin.LightweightMode.Value;
            var newLightweight = GUILayout.Toggle(currentLightweight, $"**Lightweight Mode** ({currentLightweight})");
            if (newLightweight != currentLightweight) SEGIPlugin.LightweightMode.Value = newLightweight;
            GUILayout.Label("Culls most objects except emissive ones during voxelization", GUI.skin.box,
                GUILayout.ExpandWidth(true));

            GUILayout.Space(10);
            GUILayout.Label("=== Gain Controls ===", GUI.skin.box, GUILayout.ExpandWidth(true));

            GUILayout.Label($"Global Illumination Gain: {SEGIPlugin.GIGain.Value:F2}");
            var newGiGain = GUILayout.HorizontalSlider(SEGIPlugin.GIGain.Value, 0.0f, 8.0f);
            if (!Mathf.Approximately(newGiGain, SEGIPlugin.GIGain.Value)) SEGIPlugin.GIGain.Value = newGiGain;

            GUILayout.Label($"Near Light Gain: {SEGIPlugin.NearLightGain.Value:F2}");
            var newNearLightGain = GUILayout.HorizontalSlider(SEGIPlugin.NearLightGain.Value, 0.0f, 2.0f);
            if (!Mathf.Approximately(newNearLightGain, SEGIPlugin.NearLightGain.Value))
                SEGIPlugin.NearLightGain.Value = newNearLightGain;

            GUILayout.Label($"Secondary Bounce Gain: {SEGIPlugin.SecondaryBounceGain.Value:F2}");
            var newSecondaryBounce = GUILayout.HorizontalSlider(SEGIPlugin.SecondaryBounceGain.Value, 0.0f, 8.0f);
            if (!Mathf.Approximately(newSecondaryBounce, SEGIPlugin.SecondaryBounceGain.Value))
                SEGIPlugin.SecondaryBounceGain.Value = newSecondaryBounce;

            GUILayout.Space(10);
            GUILayout.Label("Press F11 to toggle this menu", GUILayout.ExpandWidth(true));
            GUILayout.Label("All changes are saved automatically", GUILayout.ExpandWidth(true));
        }

        GUILayout.EndScrollView();
        GUILayout.EndVertical();

        GUI.DragWindow();
    }

    private void OnDestroy()
    {
        if (_showConfig)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }
}