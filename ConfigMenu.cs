using UnityEngine;

namespace BeefsSEGIPlus;

public class ConfigMenu : MonoBehaviour
{
    private bool _showConfig = false;
    private Vector2 _scrollPosition = Vector2.zero;
    private Rect _windowRect;
    private bool _windowRectInitialized = false;
    private int _lastScreenHeight = 0;
    private int _lastScreenWidth = 0;
    private float _guiScale = 1.0f;
    private GUIStyle _orangeBoxStyle;
    private GUIStyle _blueBoxStyle;
    private GUIStyle _redBoxStyle;
    private GUIStyle _greenBoxStyle;
    private bool _stylesInitialized = false;

    private void Update()
    {
        bool inGameWorld = IsInGameWorlExclMainMenu();

        if (_showConfig && !inGameWorld)
        {
            _showConfig = false;
            return;
        }

        if (!inGameWorld) return;

        if (_showConfig && Input.GetKeyDown(KeyCode.Escape))
        {
            _showConfig = false;
            return;
        }

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

        if (_windowRectInitialized && (Screen.height != _lastScreenHeight || Screen.width != _lastScreenWidth))
        {
            _windowRectInitialized = false;
            _lastScreenHeight = Screen.height;
            _lastScreenWidth = Screen.width;
        }
    }

    private void OnGUI()
    {
        if (_showConfig)
        {
            if (!_windowRectInitialized)
                InitializeWindowRect();

            if (!_stylesInitialized)
                InitializeStyles();

            Matrix4x4 oldMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(_guiScale, _guiScale, 1.0f));

            Rect scaledWindowRect = new Rect(
                _windowRect.x / _guiScale,
                _windowRect.y / _guiScale,
                _windowRect.width / _guiScale,
                _windowRect.height / _guiScale
            );

            Color oldColor = GUI.color;
            GUI.color = new Color(0.05f, 0.05f, 0.05f, 1f);
            GUI.Box(new Rect(scaledWindowRect.x - 2, scaledWindowRect.y - 2, scaledWindowRect.width + 4, scaledWindowRect.height + 4), "");
            GUI.color = oldColor;

            scaledWindowRect = GUILayout.Window(0, scaledWindowRect, ConfigWindow, "SEGI Plus Configuration (F11 to close)");
            _windowRect = new Rect(
                scaledWindowRect.x * _guiScale,
                scaledWindowRect.y * _guiScale,
                scaledWindowRect.width * _guiScale,
                scaledWindowRect.height * _guiScale
            );

            GUI.matrix = oldMatrix;
        }
    }

    private bool IsInGameWorlExclMainMenu()
    {
        if (!(SEGIPlugin.Instance?.IsInGameWorld() ?? false))
            return false;
        try
        {
            Light worldSun = WorldManager.Instance?.WorldSun?.TargetLight;
            return worldSun != null;
        }
        catch
        {
            return false;
        }
    }

    private void InitializeWindowRect()
    {
        if (_windowRectInitialized) return;
        float screenHeight = Screen.height;
        float screenWidth = Screen.width;
        float baseHeight = 1440f;
        float baseWindowHeight = 950f;
        float baseWindowWidth = 650f;
        _guiScale = Mathf.Max(1.0f, screenHeight / baseHeight);
        float scaledWidth = baseWindowWidth * _guiScale;
        float scaledHeight = baseWindowHeight * _guiScale;
        float maxHeight = screenHeight * 0.7f;
        float maxWidth = screenWidth * 0.5f;
        float windowHeight = Mathf.Min(scaledHeight, maxHeight);
        float windowWidth = Mathf.Min(scaledWidth, maxWidth);
        _windowRect = new Rect(20, 20, windowWidth, windowHeight);
        _windowRectInitialized = true;
    }

    private void ConfigWindow(int windowID)
    {
        GUILayout.BeginVertical();

        float scrollViewHeight = (_windowRect.height / _guiScale) - 50f;
        float scrollViewWidth = (_windowRect.width / _guiScale) - 20f;
        _scrollPosition = GUILayout.BeginScrollView(_scrollPosition,
            GUILayout.Width(scrollViewWidth),
            GUILayout.Height(scrollViewHeight));

        GUILayout.Label("=== SEGI Plus Global Settings ===", GUI.skin.box, GUILayout.ExpandWidth(true));

        var currentEnabled = SEGIPlugin.Enabled.Value;
        var newEnabled = GUILayout.Toggle(currentEnabled, $"Enable SEGI Plus ({currentEnabled})");
        if (newEnabled != currentEnabled) SEGIPlugin.Enabled.Value = newEnabled;

        if (SEGIPlugin.Enabled.Value)
        {
            GUILayout.BeginVertical(_orangeBoxStyle);
            GUILayout.Label("=== Quality Level ===", GUI.skin.box, GUILayout.ExpandWidth(true));
            GUILayout.Label($"Quality Level: {SEGIPlugin.QualityLevel.Value} ({ConfigData.GetQualityName()})");
            var newQualityLevel = Mathf.RoundToInt(GUILayout.HorizontalSlider(SEGIPlugin.QualityLevel.Value, 0, 3));
            if (newQualityLevel != SEGIPlugin.QualityLevel.Value) SEGIPlugin.QualityLevel.Value = newQualityLevel;
            GUILayout.EndVertical();

            GUILayout.Space(3);

            GUILayout.BeginVertical(_blueBoxStyle);
            GUILayout.Label("=== Day/Night Cycle ===", GUI.skin.box, GUILayout.ExpandWidth(true));
            GUILayout.Label($"Day Ambient Brightness: {SEGIPlugin.DayAmbientBrightness.Value:F3}");
            var newDayBrightness = GUILayout.HorizontalSlider(SEGIPlugin.DayAmbientBrightness.Value, 0.000f, 0.25f);
            if (!Mathf.Approximately(newDayBrightness, SEGIPlugin.DayAmbientBrightness.Value))
                SEGIPlugin.DayAmbientBrightness.Value = newDayBrightness;
            GUILayout.TextArea("Controls how bright ambient lighting is during daytime (sun above horizon).\nHigher values make shadowed areas brighter.", GUI.skin.box);

            GUILayout.Label($"Night Ambient Brightness: {SEGIPlugin.NightAmbientBrightness.Value:F4}");
            var newNightBrightness = GUILayout.HorizontalSlider(SEGIPlugin.NightAmbientBrightness.Value, 0.000f, 0.01f);
            if (!Mathf.Approximately(newNightBrightness, SEGIPlugin.NightAmbientBrightness.Value))
                SEGIPlugin.NightAmbientBrightness.Value = newNightBrightness;
            GUILayout.TextArea("Controls ambient lighting at night (sun below horizon). Keep low for realistic darkness.\nHigher values make shadowed areas brighter.", GUI.skin.box);
            GUILayout.EndVertical();

            GUILayout.Space(3);

            GUILayout.BeginVertical(_redBoxStyle);
            GUILayout.Label("=== Performance ===", GUI.skin.box, GUILayout.ExpandWidth(true));
            var currentLightweight = SEGIPlugin.LightweightMode.Value;
            var newLightweight = GUILayout.Toggle(currentLightweight, $"**Lightweight Mode** ({currentLightweight})");
            if (newLightweight != currentLightweight) SEGIPlugin.LightweightMode.Value = newLightweight;
            GUILayout.TextArea("Only renders emissive objects during voxelization. Faster but causes light leakage.\nYou probably will want to lower the main GI Gain with this enabled", GUI.skin.box);

            GUILayout.Space(10);
            GUILayout.Label("=== Adaptive Performance ===", GUI.skin.box, GUILayout.ExpandWidth(true));

            var currentAdaptive = SEGIPlugin.AdaptivePerformance.Value;
            var newAdaptive = GUILayout.Toggle(currentAdaptive, $"Enable Adaptive Performance ({currentAdaptive})");
            if (newAdaptive != currentAdaptive) SEGIPlugin.AdaptivePerformance.Value = newAdaptive;
            GUILayout.TextArea("Automatically adjusts settings to maintain framerate", GUI.skin.box);
            if (SEGIPlugin.AdaptivePerformance.Value)
            {
                GUILayout.Space(5);
                GUILayout.Label($"Target Framerate: {SEGIPlugin.TargetFramerate.Value} FPS");
                var newTargetFPS = Mathf.RoundToInt(GUILayout.HorizontalSlider(SEGIPlugin.TargetFramerate.Value, 15, 240));
                if (newTargetFPS != SEGIPlugin.TargetFramerate.Value) SEGIPlugin.TargetFramerate.Value = newTargetFPS;
                GUILayout.TextArea("The system will try to adjust SEGI Plus to stay around this framerate", GUI.skin.box);

                GUILayout.Space(5);
                GUILayout.Label($"Adaptive Strategy: {SEGIPlugin.AdaptiveStrategy.Value} ({GetStratName(SEGIPlugin.AdaptiveStrategy.Value)})");
                var newStrategy = Mathf.RoundToInt(GUILayout.HorizontalSlider(SEGIPlugin.AdaptiveStrategy.Value, 0, 1));
                if (newStrategy != SEGIPlugin.AdaptiveStrategy.Value) SEGIPlugin.AdaptiveStrategy.Value = newStrategy;
                GUILayout.TextArea(GetStratDescription(SEGIPlugin.AdaptiveStrategy.Value), GUI.skin.box);

                if (SEGIPlugin.AdaptiveStrategy.Value == 1)
                {
                    GUILayout.Space(5);
                    GUILayout.Label($"Min Distance: {SEGIPlugin.AdaptiveMinDistancePercent.Value:F0}% of max");
                    var newMinDistPercent = GUILayout.HorizontalSlider(SEGIPlugin.AdaptiveMinDistancePercent.Value, 10f, 100f);
                    if (!Mathf.Approximately(newMinDistPercent, SEGIPlugin.AdaptiveMinDistancePercent.Value))
                        SEGIPlugin.AdaptiveMinDistancePercent.Value = newMinDistPercent;
                    GUILayout.TextArea("What's the minimum distance we can use? Lower % = closer but also faster", GUI.skin.box);
                }
            }
            GUILayout.EndVertical();

            GUILayout.Space(3);

            GUILayout.BeginVertical(_greenBoxStyle);
            GUILayout.Label("=== Gain Controls ===", GUI.skin.box, GUILayout.ExpandWidth(true));
            float giMultiplier = SEGIPlugin.UseGainMultiplier.Value ? 10f : 1f;
            GUILayout.Label($"Global Illumination Gain: {SEGIPlugin.GIGain.Value:F2}" +
                            (SEGIPlugin.UseGainMultiplier.Value ? $" (Applied: {SEGIPlugin.GIGain.Value * giMultiplier:F2})" : ""));
            var newGiGain = GUILayout.HorizontalSlider(SEGIPlugin.GIGain.Value, 0.0f, 8.0f);
            if (!Mathf.Approximately(newGiGain, SEGIPlugin.GIGain.Value)) SEGIPlugin.GIGain.Value = newGiGain;
            GUILayout.TextArea("Master brightness control for all global illumination effects.\nIncrease if lighting seems too dim.", GUI.skin.box);
            GUILayout.Label($"Near Light Gain: {SEGIPlugin.NearLightGain.Value:F2}" +
                            (SEGIPlugin.UseGainMultiplier.Value ? $" (Applied: {SEGIPlugin.NearLightGain.Value * giMultiplier:F2})" : ""));
            var newNearLightGain = GUILayout.HorizontalSlider(SEGIPlugin.NearLightGain.Value, 0.0f, 2.0f);
            if (!Mathf.Approximately(newNearLightGain, SEGIPlugin.NearLightGain.Value))
                SEGIPlugin.NearLightGain.Value = newNearLightGain;
            GUILayout.TextArea("Brightens lighting effects close to the camera.\nUseful for interior lighting.", GUI.skin.box);
            GUILayout.Label($"Secondary Bounce Gain: {SEGIPlugin.SecondaryBounceGain.Value:F2}" +
                            (SEGIPlugin.UseGainMultiplier.Value ? $" (Applied: {SEGIPlugin.SecondaryBounceGain.Value * giMultiplier:F2})" : ""));
            var newSecondaryBounce = GUILayout.HorizontalSlider(SEGIPlugin.SecondaryBounceGain.Value, 0.0f, 2.0f);
            if (!Mathf.Approximately(newSecondaryBounce, SEGIPlugin.SecondaryBounceGain.Value))
                SEGIPlugin.SecondaryBounceGain.Value = newSecondaryBounce;
            GUILayout.TextArea("Controls secondary light bounces. Higher values = more light bouncing.\nIt adds more light in total so interacts with the day/night ambient brightness settings", GUI.skin.box);
            GUILayout.Space(10);
            var currentMultiplier = SEGIPlugin.UseGainMultiplier.Value;
            var newMultiplier = GUILayout.Toggle(currentMultiplier, $"x10 Gain Multiplier ({currentMultiplier})");
            if (newMultiplier != currentMultiplier) SEGIPlugin.UseGainMultiplier.Value = newMultiplier;
            GUILayout.EndVertical();

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
        if (_showConfig && (SEGIPlugin.Instance?.IsInGameWorld() ?? false))
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    private void InitializeStyles()
    {
        if (_stylesInitialized) return;

        _orangeBoxStyle = MakeStyle(new Color(0.65f, 0.27f, 0f, 0.8f), Color.white);
        _blueBoxStyle = MakeStyle(new Color(0f, 0.18f, 0.6f, 0.8f), Color.white);
        _redBoxStyle = MakeStyle(new Color(0.55f, 0.01f, 0f, 0.8f), Color.white);
        _greenBoxStyle = MakeStyle(new Color(0f, 0.5f, 0.05f, 0.8f), Color.white);

        _stylesInitialized = true;
    }

    private GUIStyle MakeStyle(Color backgroundColor, Color textColor)
    {
        var style = new GUIStyle(GUI.skin.box);
        style.normal.background = WhyDoIhaveToCreateADamnTextureForThisToWorkGuh(backgroundColor);
        style.normal.textColor = textColor;
        style.fontStyle = FontStyle.Bold;
        style.alignment = TextAnchor.MiddleCenter;
        return style;
    }

    private Texture2D WhyDoIhaveToCreateADamnTextureForThisToWorkGuh(Color color)
    {
        Color[] pixels = new Color[4];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = color;

        Texture2D texture = new Texture2D(2, 2);
        texture.SetPixels(pixels);
        texture.Apply();
        return texture;
    }

    private string GetStratName(int strategy)
    {
        return strategy switch
        {
            0 => "Balanced",
            1 => "Reduce Distance First",
            _ => "Unknown"
        };
    }

    private string GetStratDescription(int strategy)
    {
        return strategy switch
        {
            0 => "Balanced: Scales all settings proportionally as in experimental",
            1 => "Reduce Distance First: Keeps quality and reduces distance of global illumination first",
            _ => "Unknown"
        };
    }
}