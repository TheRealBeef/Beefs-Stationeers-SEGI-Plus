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
    private GUIStyle _purpleBoxStyle;
    private bool _stylesInitialized = false;
    private bool _showAdvanced = false;

    #if SEGI_PROFILER
        private GUIStyle _profilerBoxStyle;
        private Vector2 _profilerResultsScroll = Vector2.zero;
    #endif


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
            var newQualityLevel = Mathf.RoundToInt(GUILayout.HorizontalSlider(SEGIPlugin.QualityLevel.Value, 0, 4));
            if (newQualityLevel != SEGIPlugin.QualityLevel.Value) SEGIPlugin.QualityLevel.Value = newQualityLevel;

            if (SEGIPlugin.QualityLevel.Value == 4)
            {
                GUILayout.TextArea("Consumes additional 6.1gb of VRAM. Really better for screenshots than gameplay", GUI.skin.box);
            }

            if (SEGIPlugin.QualityLevel.Value >= 2)
            {
                GUILayout.Space(5);
                var currentDense = SEGIPlugin.DenseVoxelMode.Value;
                var newDense = GUILayout.Toggle(currentDense,
                    $"High Density Mode ({(currentDense ? "ON" : "OFF")})");
                if (newDense != currentDense) SEGIPlugin.DenseVoxelMode.Value = newDense;
                GUILayout.TextArea("Twice the GI detail but half the range.", GUI.skin.box);
            }
            else if (SEGIPlugin.DenseVoxelMode.Value)
            {
                SEGIPlugin.DenseVoxelMode.Value = false;
            }

            GUILayout.EndVertical();

            GUILayout.Space(3);

            GUILayout.BeginVertical(_redBoxStyle);
            GUILayout.Label("=== Performance ===", GUI.skin.box, GUILayout.ExpandWidth(true));
            var currentLightweight = SEGIPlugin.LightweightMode.Value;
            var newLightweight = GUILayout.Toggle(currentLightweight, $"**Lightweight Mode** ({currentLightweight})");
            if (newLightweight != currentLightweight) SEGIPlugin.LightweightMode.Value = newLightweight;
            GUILayout.TextArea("Only renders emissive objects during voxelization. Faster but causes light leakage.\nYou probably will want to lower the main GI Gain with this enabled", GUI.skin.box);

            GUILayout.Space(10);
            var currentForwardBias = SEGIPlugin.ForwardOriginBias.Value;
            var newForwardBias = GUILayout.Toggle(currentForwardBias, $"Forward Origin Bias ({(currentForwardBias ? "ON" : "OFF")})");
            if (newForwardBias != currentForwardBias) SEGIPlugin.ForwardOriginBias.Value = newForwardBias;
            GUILayout.TextArea("Pushes the voxel volume 25% forward in the direction you're looking.", GUI.skin.box);

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
            GUILayout.Label($"Emissive Light Gain: {SEGIPlugin.EmissiveLightGain.Value:F2}" +
                            (SEGIPlugin.UseGainMultiplier.Value ? $" (Applied: {SEGIPlugin.EmissiveLightGain.Value * giMultiplier:F2})" : ""));
            var newEmissiveLightGain = GUILayout.HorizontalSlider(SEGIPlugin.EmissiveLightGain.Value, 0.0f, 10.0f);
            if (!Mathf.Approximately(newEmissiveLightGain, SEGIPlugin.EmissiveLightGain.Value))
                SEGIPlugin.EmissiveLightGain.Value = newEmissiveLightGain;
            GUILayout.TextArea("Multiplier for emissive light contribution during voxelization.\nHigher values = brighter emissive lights in the scene.", GUI.skin.box);
            GUILayout.Space(5);
            var currentMultiplier = SEGIPlugin.UseGainMultiplier.Value;
            var newMultiplier = GUILayout.Toggle(currentMultiplier, $"x10 Gain Multiplier ({currentMultiplier})");
            if (newMultiplier != currentMultiplier) SEGIPlugin.UseGainMultiplier.Value = newMultiplier;
            GUILayout.Space(5);
            GUILayout.Label($"Secondary Bounce Gain: {SEGIPlugin.SecondaryBounceGain.Value:F2}");
            var newSecondaryBounce = GUILayout.HorizontalSlider(SEGIPlugin.SecondaryBounceGain.Value, 0.0f, 0.75f);
            if (!Mathf.Approximately(newSecondaryBounce, SEGIPlugin.SecondaryBounceGain.Value))
                SEGIPlugin.SecondaryBounceGain.Value = newSecondaryBounce;
            GUILayout.TextArea("Controls secondary light bounces. Higher values = more light bouncing into shadowed areas.", GUI.skin.box);
            GUILayout.Space(5);
            var currentBubble = SEGIPlugin.EmissiveBubbleEnabled.Value;
            var newBubble = GUILayout.Toggle(currentBubble,
                $"Emissive Exclusion Bubble ({(currentBubble ? "ON" : "OFF")})");
            if (newBubble != currentBubble) SEGIPlugin.EmissiveBubbleEnabled.Value = newBubble;
            GUILayout.TextArea("Prevent held items and suit from contributing to GI.", GUI.skin.box);
            GUILayout.EndVertical();

            GUILayout.Space(3);

            GUILayout.BeginVertical(_purpleBoxStyle);
            if (GUILayout.Button($"=== Advanced === {(_showAdvanced ? "[-]" : "[+]")}", GUI.skin.box, GUILayout.ExpandWidth(true)))
                _showAdvanced = !_showAdvanced;
            if (_showAdvanced)
            {
                GUILayout.Label($"Occlusion Strength: {SEGIPlugin.OcclusionStrengthOffset.Value:+0.00;-0.00;0.00}");
                GUILayout.BeginHorizontal();
                var newOccStrOffset = GUILayout.HorizontalSlider(SEGIPlugin.OcclusionStrengthOffset.Value, -0.35f, 0.75f);
                if (GUILayout.Button("Reset", GUILayout.Width(50)))
                    newOccStrOffset = 0f;
                GUILayout.EndHorizontal();
                if (!Mathf.Approximately(newOccStrOffset, SEGIPlugin.OcclusionStrengthOffset.Value))
                    SEGIPlugin.OcclusionStrengthOffset.Value = newOccStrOffset;
                GUILayout.TextArea("How strongly geometry stops GI. Higher reduces light leaking through walls but also darkens scene.", GUI.skin.box);
                GUILayout.Space(5);
                GUILayout.Label($"Cone Trace Bias: {SEGIPlugin.ConeTraceBiasOffset.Value:+0.00;-0.00;0.00}");
                GUILayout.BeginHorizontal();
                var newBiasOffset = GUILayout.HorizontalSlider(SEGIPlugin.ConeTraceBiasOffset.Value, -0.3f, 0.6f);
                if (GUILayout.Button("Reset", GUILayout.Width(50)))
                    newBiasOffset = 0f;
                GUILayout.EndHorizontal();
                if (!Mathf.Approximately(newBiasOffset, SEGIPlugin.ConeTraceBiasOffset.Value))
                    SEGIPlugin.ConeTraceBiasOffset.Value = newBiasOffset;
                GUILayout.TextArea("How far from surfaces GI probes sample, Lower = more self-occlusion. Higher = more light leakage.", GUI.skin.box);
            }
            GUILayout.EndVertical();

            #if SEGI_PROFILER
                GUILayout.Space(10);
                DrawDebugOverridesSection();
                GUILayout.Space(10);
                DrawProfilerSection();
                GUILayout.Space(10);
            #endif

            GUILayout.Label("Press F11 to toggle this menu", GUILayout.ExpandWidth(true));
            GUILayout.Label("All changes are saved automatically", GUILayout.ExpandWidth(true));
        }

        GUILayout.EndScrollView();
        GUILayout.EndVertical();

        GUI.DragWindow();
    }

    #if SEGI_PROFILER
        private GUIStyle _debugBoxStyle;

        private void DrawDebugOverridesSection()
        {
            if (_debugBoxStyle == null)
                _debugBoxStyle = MakeStyle(new Color(0.1f, 0.35f, 0.4f, 0.8f), Color.white);

            GUILayout.BeginVertical(_debugBoxStyle);
            GUILayout.Label("=== Debug Overrides (Dev Only) ===", GUI.skin.box, GUILayout.ExpandWidth(true));
            GUILayout.Label("Null = use hardcoded default. Drag slider to override.", GUI.skin.box);

            GUILayout.Space(5);
            GUILayout.Label("— Temporal —");
            DebugOverrides.TemporalBlendWeight = DrawNullableFloat("Blend Weight", DebugOverrides.TemporalBlendWeight, 0.01f, 0.005f, 0.25f);
            DebugOverrides.DisocclusionSensitivity = DrawNullableFloat("Disocclusion Sens.", DebugOverrides.DisocclusionSensitivity, 5.0f, 1f, 20f);
            DebugOverrides.MotionBlendMax = DrawNullableFloat("Motion Blend Max", DebugOverrides.MotionBlendMax, 0.25f, 0.1f, 0.5f);

            GUILayout.Space(5);
            GUILayout.Label("— Cone Tracing —");
            DebugOverrides.ConeLength = DrawNullableFloat("Length", DebugOverrides.ConeLength, ConfigData.ConeLength, 0.5f, 3.0f);
            DebugOverrides.ConeWidth = DrawNullableFloat("Width", DebugOverrides.ConeWidth, ConfigData.ConeWidth, 1.0f, 12.0f);
            DebugOverrides.ConeTraceBias = DrawNullableFloat("Cone Trace Bias", DebugOverrides.ConeTraceBias, ConfigData.ConeTraceBias, 0.0f, 2.0f);

            GUILayout.Space(5);
            GUILayout.Label("— Visual Tuning —");
            DebugOverrides.OcclusionStrength = DrawNullableFloat("Occlusion Strength", DebugOverrides.OcclusionStrength, 0.86f, 0f, 2f);
            DebugOverrides.NearOcclusionStrength = DrawNullableFloat("Near Occlusion", DebugOverrides.NearOcclusionStrength, ConfigData.NearOcclusionStrength, 0f, 2f);

            GUILayout.Space(5);
            GUILayout.Label("— Sun Shadows —");
            DebugOverrides.SunShadowSoftness = DrawNullableFloat("Shadow Softness", DebugOverrides.SunShadowSoftness, 150.0f, 50f, 1000f);
            DebugOverrides.SunShadowResolution = DrawNullableInt("Shadow Resolution", DebugOverrides.SunShadowResolution, 256, 64, 1024);

            GUILayout.Space(5);
            GUILayout.Label("— Voxelization —");
            DebugOverrides.EmissiveTemporalBlend = DrawNullableFloat("Emissive Temporal Blend", DebugOverrides.EmissiveTemporalBlend, 0.6f, 0.05f, 1.0f);
            DebugOverrides.ForwardOriginBias = DrawNullableBool("Forward Origin Bias (25%)", DebugOverrides.ForwardOriginBias);

            GUILayout.Space(5);
            if (GUILayout.Button("Reset All to Defaults", GUILayout.Height(25)))
            {
                DebugOverrides.TemporalBlendWeight = null;
                DebugOverrides.DisocclusionSensitivity = null;
                DebugOverrides.MotionBlendMax = null;
                DebugOverrides.ConeLength = null;
                DebugOverrides.ConeWidth = null;
                DebugOverrides.ConeTraceBias = null;
                DebugOverrides.OcclusionStrength = null;
                DebugOverrides.NearOcclusionStrength = null;
                DebugOverrides.SunShadowSoftness = null;
                DebugOverrides.SunShadowResolution = null;
                DebugOverrides.EmissiveTemporalBlend = null;
                DebugOverrides.ForwardOriginBias = null;
            }

            GUILayout.EndVertical();
        }

        private float? DrawNullableFloat(string label, float? current, float defaultVal, float min, float max)
        {
            GUILayout.BeginHorizontal();
            bool active = current.HasValue;
            float displayVal = current ?? defaultVal;
            GUILayout.Label($"{label}: {displayVal:F3}{(active ? "" : " (default)")}", GUILayout.Width(260));
            float newVal = GUILayout.HorizontalSlider(displayVal, min, max);
            bool reset = active && GUILayout.Button("×", GUILayout.Width(22));
            GUILayout.EndHorizontal();
            if (reset) return null;
            if (!Mathf.Approximately(newVal, displayVal)) return newVal;
            return current;
        }

        private int? DrawNullableInt(string label, int? current, int defaultVal, int min, int max)
        {
            GUILayout.BeginHorizontal();
            bool active = current.HasValue;
            int displayVal = current ?? defaultVal;
            GUILayout.Label($"{label}: {displayVal}{(active ? "" : " (default)")}", GUILayout.Width(260));
            int newVal = Mathf.RoundToInt(GUILayout.HorizontalSlider(displayVal, min, max));
            bool reset = active && GUILayout.Button("×", GUILayout.Width(22));
            GUILayout.EndHorizontal();
            if (reset) return null;
            if (newVal != displayVal) return newVal;
            return current;
        }

        private bool? DrawNullableBool(string label, bool? current)
        {
            GUILayout.BeginHorizontal();
            bool active = current.HasValue;
            string state = active ? (current.Value ? "ON" : "OFF") : "default";
            GUILayout.Label($"{label}: {state}", GUILayout.Width(260));
            bool clicked = GUILayout.Button(active ? (current.Value ? "ON" : "OFF") : "—", GUILayout.Width(50));
            GUILayout.EndHorizontal();
            if (clicked)
            {
                if (!active) return false;
                if (!current.Value) return true;
                return null; // cycle: null → false → true → null
            }
            return current;
        }

        private void DrawProfilerSection()
        {
            if (_profilerBoxStyle == null)
                _profilerBoxStyle = MakeStyle(new Color(0.4f, 0.1f, 0.5f, 0.8f), Color.white);

            GUILayout.BeginVertical(_profilerBoxStyle);
            GUILayout.Label("=== Profiler ===", GUI.skin.box, GUILayout.ExpandWidth(true));

            var segi = Camera.main != null ? Camera.main.GetComponent<SEGIStationeers>() : null;
            if (segi == null)
            {
                GUILayout.Label("SEGI component not found on camera.");
                GUILayout.EndVertical();
                return;
            }

            var profiler = segi.Profiler;

            switch (profiler.State)
            {
                case SEGIProfiler.ProfileState.Idle:
                    GUILayout.TextArea("Profiler",GUI.skin.box);
                    GUILayout.Space(3);
                    if (GUILayout.Button("Start Profiling", GUILayout.Height(35)))
                    {
                        profiler.StartProfiling();
                    }
                    if (profiler.HasResults)
                    {
                        GUILayout.Space(3);
                        GUILayout.Label("Results:");
                        DrawProfilerResults(profiler);
                    }
                    break;

                case SEGIProfiler.ProfileState.Running:
                    GUILayout.Label(profiler.StatusMessage);

                    float progress = profiler.GetProgress();
                    Rect progressRect = GUILayoutUtility.GetRect(GUIContent.none, GUI.skin.box,
                        GUILayout.Height(24), GUILayout.ExpandWidth(true));

                    GUI.Box(progressRect, "");

                    Color oldBg = GUI.backgroundColor;
                    GUI.backgroundColor = new Color(0.2f, 0.7f, 0.3f, 1f);
                    GUI.Box(new Rect(progressRect.x, progressRect.y,
                        progressRect.width * progress, progressRect.height), "");
                    GUI.backgroundColor = oldBg;

                    float stepWidth = progressRect.width / profiler.StepCount;
                    for (int i = 1; i < profiler.StepCount; i++)
                    {
                        float x = progressRect.x + stepWidth * i;
                        GUI.DrawTexture(new Rect(x, progressRect.y, 1, progressRect.height),
                            Texture2D.whiteTexture);
                    }

                    GUI.Label(progressRect,
                        $"Step {profiler.CurrentStepIndex + 1}/{profiler.StepCount}  —  " +
                        $"{progress * 100f:F0}%  ({profiler.TotalFrameCount - (int)(profiler.TotalFrameCount * progress)} frames left)",
                        new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter });

                    GUILayout.Space(3);
                    if (GUILayout.Button("Cancel"))
                    {
                        profiler.Cancel();
                    }
                    break;

                case SEGIProfiler.ProfileState.Complete:
                    GUILayout.Label("Profiling complete!");
                    DrawProfilerResults(profiler);
                    GUILayout.Space(3);
                    if (GUILayout.Button("Run Again", GUILayout.Height(30)))
                    {
                        profiler.StartProfiling();
                    }
                    break;
            }

            GUILayout.EndVertical();
        }

        private void DrawProfilerResults(SEGIProfiler profiler)
        {
            string results = profiler.GetResultsTable();

            _profilerResultsScroll = GUILayout.BeginScrollView(_profilerResultsScroll,
                GUILayout.Height(350), GUILayout.ExpandWidth(true));

            GUIStyle monoStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                wordWrap = false,
                richText = false
            };
            try
            {
                var font = Font.CreateDynamicFontFromOSFont("Consolas", 12);
                if (font != null) monoStyle.font = font;
            }
            catch { }

            GUILayout.Label(results, monoStyle);
            GUILayout.EndScrollView();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Copy"))
            {
                GUIUtility.systemCopyBuffer = results;
            }
            GUILayout.EndHorizontal();
        }
    #endif

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
        _purpleBoxStyle = MakeStyle(new Color(0.35f, 0.1f, 0.45f, 0.8f), Color.white);

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