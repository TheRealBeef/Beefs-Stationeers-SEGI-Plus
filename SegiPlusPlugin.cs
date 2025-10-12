using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

namespace BeefsSEGIPlus
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class SEGIPlugin : BaseUnityPlugin
    {
        public static SEGIPlugin Instance;
        public static ManualLogSource Log;

        private float popupDelay = 1.5f;
        public static ConfigEntry<bool> Update1_2_0_Popup;

        private class UpdatePopupItem
        {
            public string Key;
            public string Title;
            public string Changelog;
            public ConfigEntry<bool> Config;
        }

        private readonly List<UpdatePopupItem> allPopups = new List<UpdatePopupItem>();
        private Queue<UpdatePopupItem> popupQueue;
        private UpdatePopupItem currentPopup;

        private Rect popupRect;
        private Vector2 scrollPos;
        private bool showPopup = false;
        private bool pendingShow = false;
        private float guiScale = 1.0f;
        private int lastScreenHeight = 0;
        private int lastScreenWidth = 0;
        private bool popupRectInitialized = false;

        public static ConfigEntry<bool> Enabled;
        public static ConfigEntry<int> QualityLevel;
        public static ConfigEntry<float> SecondaryBounceGain;
        public static ConfigEntry<float> NearLightGain;
        public static ConfigEntry<float> GIGain;
        public static ConfigEntry<float> DayAmbientBrightness;
        public static ConfigEntry<float> NightAmbientBrightness;
        public static ConfigEntry<bool> LightweightMode;
        public static ConfigEntry<int> TargetFramerate;
        public static ConfigEntry<bool> AdaptivePerformance;
        public static ConfigEntry<int> AdaptiveStrategy;
        public static ConfigEntry<bool> UseGainMultiplier;


        private static SEGIStationeers SegiStationeersInstance { get; set; }

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            BindAllConfigs();
            Log.LogInfo($"Plugin {PluginInfo.PLUGIN_NAME} is loaded!");

            Update1_2_0_Popup = AddUpdatePopup(
                "Update1_2_0_Popup",
                "SEGI Plus was Updated to v1.2.0!",
                "Changelog v1.2.0:\n " +
                "- Added first pass of adaptive framerate control that works with the quality setting to try and improve performance\n" +
                "- This can be used at any quality setting and with or without lightweight mode\n\n" +
                "## IMPORTANT ##\n" +
                "- This isn't automatically enabled as it's yet experimental - you can enable this in settings\n\n" +
                "Press F11 in-game to access the configuration menu or use the workshop button on the left and click on the mod to adjust settings!\n\n" +
                "Changelog v1.2.1:\n" +
                "- Widened adaptive framerate slider choices\n" +
                "- Automatically remove/mark read the major update popup if go into world\n" +
                "- Added an x10 multiplier option if you want to play around with silly gain values\n" +
                "- Darkened background of F11 menu slightly",
                defaultSeen: false);

            popupQueue = new Queue<UpdatePopupItem>();
            foreach (var p in allPopups)
            {
                if (!p.Config.Value)
                {
                    popupQueue.Enqueue(p);
                }
            }

            pendingShow = popupQueue.Count > 0;
        }



        private void Start()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded; ;
            StartCoroutine(InitializeSEGICoroutine());
            gameObject.AddComponent<ConfigMenu>();
        }

        private void Update()
        {
            if (SegiStationeersInstance != null)
            {
                SegiStationeersInstance.enabled = Enabled.Value;
                if (SegiStationeersInstance.sun == null)
                {
                    try
                    {
                        Light worldSun = WorldManager.Instance?.WorldSun?.TargetLight;
                        if (worldSun != null)
                        {
                            SegiStationeersInstance.sun = worldSun;
                            Log.LogInfo($"SEGI Plus sun light: {worldSun.name}");

                            if (showPopup && currentPopup != null)
                            {
                                currentPopup.Config.Value = true;
                                Config.Save();
                                if (popupQueue.Count > 0)
                                {
                                    currentPopup = popupQueue.Dequeue();
                                    scrollPos = Vector2.zero;
                                    popupRectInitialized = false;
                                }
                                else
                                {
                                    currentPopup = null;
                                    showPopup = false;
                                    pendingShow = false;
                                }
                            }
                        }
                    }
                    catch {}
                }
            }
            if (SegiStationeersInstance == null && Camera.main != null)
            {
                try
                {
                    InitializeSEGI();
                }
                catch (Exception ex)
                {
                    Log.LogError($"Failed to initialize SEGI Plus: {ex.Message}");
                }
            }

            if (pendingShow && currentPopup == null && popupQueue != null && popupQueue.Count > 0 &&
                IsInGameWorld())
            {
                if (popupDelay > 0f)
                {
                    popupDelay -= Time.deltaTime;
                    return;
                }
                currentPopup = popupQueue.Dequeue();
                StartShowingPopup(currentPopup);
            }

            if (showPopup && !IsInGameWorld())
            {
                showPopup = false;
            }

            if (popupRectInitialized && (Screen.height != lastScreenHeight || Screen.width != lastScreenWidth))
            {
                popupRectInitialized = false;
                lastScreenHeight = Screen.height;
                lastScreenWidth = Screen.width;
            }
        }

        private void OnDestroy()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void BindAllConfigs()
        {
            Enabled = Config.Bind("General", "Enable (There is also F11 config menu in-game)", true, "Enable SEGI Plus global illumination. There is an F11 config menu in-game too");
            UseGainMultiplier = Config.Bind("General", "Use x10 Gain Multiplier (applies to Global/Near/Secondary gains)", false, "Multiply all gain values by 10. Why? I dunno, but you can");
            NearLightGain = Config.Bind("Gain Knobs", "Near Light Gain", 0.0f,
                new ConfigDescription("Near light gain", new AcceptableValueRange<float>(0f, 2f)));
            GIGain = Config.Bind("Gain Knobs", "Global Illumination Gain", 3.0f,
                new ConfigDescription("Global illumination gain", new AcceptableValueRange<float>(0f, 8f)));
            SecondaryBounceGain = Config.Bind("Gain Knobs", "Secondary Bounce Gain", 0.0f,
                new ConfigDescription("Secondary bounce gain", new AcceptableValueRange<float>(0f, 2.0f)));
            DayAmbientBrightness = Config.Bind("Lighting", "Day Ambient Brightness", 0.05f,
                new ConfigDescription("Ambient light brightness during day", new AcceptableValueRange<float>(0.001f, 0.25f)));
            NightAmbientBrightness = Config.Bind("Lighting", "Night Ambient Brightness", 0.0f,
                new ConfigDescription("Ambient light brightness during night", new AcceptableValueRange<float>(0.000f, 0.01f)));
            QualityLevel = Config.Bind("Performance", "Quality Level - 0 for Low, 3 for Extreme.", 1,
                new ConfigDescription("Quality (0=Low, 1=Medium, 2=High, 3=Extreme)",
                    new AcceptableValueRange<int>(0, 3)));
            LightweightMode = Config.Bind("Performance", "**Lightweight Mode**", false,
                "If you enable this it cull most objects except the emissive ones during voxelization and runs *way* faster, at the cost of light leakage. Can be combined with any quality setting.");
            AdaptivePerformance = Config.Bind("Performance", "Adaptive Performance", false,
                "Automatically adjusts settings to maintain framerate");
            AdaptiveStrategy = Config.Bind("Performance", "Adaptive Strategy", 0,
                new ConfigDescription("Adaptive Strategy (0=Balanced, 1=Reduce distance first, 2=Reduce quality first, 3= Skip frames first)",
                    new AcceptableValueRange<int>(0, 3)));
            TargetFramerate = Config.Bind("Performance", "Target Framerate", 60,
                new ConfigDescription("The system will try to adjust SEGI Plus to stay around this framerate",
                    new AcceptableValueRange<int>(15, 240)));
        }

        private IEnumerator InitializeSEGICoroutine()
        {
            while (Camera.main == null)
            {
                yield return new WaitForSeconds(0.1f);
            }
            yield return new WaitForSeconds(1f);
            try
            {
                InitializeSEGI();
            }
            catch (Exception ex)
            {
                Log.LogError($"Failed to initialize SEGI: {ex.Message}");
            }
        }

        private void InitializeSEGI()
        {
            if (Camera.main == null)
            {
                Log.LogWarning("No main camera found for SEGI initialization");
                return;
            }
            if (!IsInGameWorld())
            {
                return;
            }
            if (SegiStationeersInstance != null)
            {
                Log.LogWarning("SEGI Plus running");
                return;
            }
            SegiStationeersInstance = Camera.main.gameObject.AddComponent<SEGIStationeers>();
            DontDestroyOnLoad(SegiStationeersInstance.gameObject);
            Log.LogInfo("SEGI Plus initialized");
        }

        private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene,
            UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            Log.LogInfo($"Scene loaded: '{scene.name}' (mode: {mode})");
            if (SegiStationeersInstance != null && !IsInGameWorld())
            {
                Log.LogInfo("Cleaning up");
                DestroyImmediate(SegiStationeersInstance.gameObject);
                SegiStationeersInstance = null;
            }
            else if (SegiStationeersInstance == null && IsInGameWorld())
            {
                Log.LogInfo("Entered world");
                Log.LogInfo($": '{scene.name}' (mode: {mode})");
                StartCoroutine(InitializeSEGICoroutine());
            }
        }

        public bool IsInGameWorld()
        {
            string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            string lowerSceneName = sceneName.ToLower();
            bool isMenu = lowerSceneName.Contains("menu") || lowerSceneName.Contains("splash");
            bool isGameWorld = !isMenu;
            return isGameWorld;
        }

        private ConfigEntry<bool> AddUpdatePopup(string key, string title, string changelog, bool defaultSeen = false)
        {
            var cfg = Config.Bind("X - NO TOUCH - Internal", key, defaultSeen, new ConfigDescription(""));
            var item = new UpdatePopupItem
            {
                Key = key,
                Title = title,
                Changelog = changelog,
                Config = cfg
            };
            allPopups.Add(item);
            return cfg;
        }

        private void StartShowingPopup(UpdatePopupItem item)
        {
            popupRectInitialized = false;
            scrollPos = Vector2.zero;
            showPopup = true;
        }

        private void InitializePopupRect()
        {
            if (popupRectInitialized) return;

            float screenHeight = Screen.height;
            float screenWidth = Screen.width;
            float baseHeight = 1080f;

            guiScale = Mathf.Max(1.0f, screenHeight / baseHeight);

            float baseWidth = 520f;
            float basePopupHeight = 320f;

            float scaledWidth = baseWidth * guiScale;
            float scaledHeight = basePopupHeight * guiScale;

            popupRect = new Rect((screenWidth - scaledWidth) / 2f, (screenHeight - scaledHeight) / 2f, scaledWidth, scaledHeight);
            lastScreenHeight = (int)screenHeight;
            lastScreenWidth = (int)screenWidth;
            popupRectInitialized = true;
        }
        private void OnGUI()
        {
            Color oldColor = GUI.color;
            if (!showPopup || currentPopup == null) return;
            if (!IsInGameWorld()) return;

            if (!popupRectInitialized)
                InitializePopupRect();

            Matrix4x4 oldMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(guiScale, guiScale, 1.0f));

            Rect scaledRect = new Rect(
                popupRect.x / guiScale,
                popupRect.y / guiScale,
                popupRect.width / guiScale,
                popupRect.height / guiScale
            );

            GUI.color = Color.white;
            GUI.backgroundColor = Color.red;
            scaledRect = GUI.ModalWindow(987987987, scaledRect, DrawPopupWindow, currentPopup.Title);

            popupRect = new Rect(
                scaledRect.x * guiScale,
                scaledRect.y * guiScale,
                scaledRect.width * guiScale,
                scaledRect.height * guiScale
            );

            GUI.matrix = oldMatrix;
            GUI.color = oldColor;
        }

        private void DrawPopupWindow(int id)
        {
            GUILayout.BeginVertical();
            var wrapStyle = new GUIStyle(GUI.skin.label) { wordWrap = true };

            float scaledHeight = (popupRect.height / guiScale) - 80;
            scrollPos = GUILayout.BeginScrollView(scrollPos, GUILayout.Height(scaledHeight));
            GUILayout.Label(currentPopup.Changelog, wrapStyle, GUILayout.ExpandWidth(true));
            GUILayout.EndScrollView();

            GUILayout.Space(8);

            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("OK - Don't show this update again", GUILayout.Height(30), GUILayout.Width(250)))
            {
                currentPopup.Config.Value = true;
                Config.Save();
                if (popupQueue.Count > 0)
                {
                    currentPopup = popupQueue.Dequeue();
                    scrollPos = Vector2.zero;
                    popupRectInitialized = false;
                    showPopup = true;
                }
                else
                {
                    currentPopup = null;
                    showPopup = false;
                    pendingShow = false;
                }
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
            GUI.DragWindow();
        }

    }
    internal static class ConfigData
    {
        private static int CurrentQualityLevel => SEGIPlugin.QualityLevel?.Value ?? 1;
        private static readonly SEGIStationeers.VoxelResolution[] VoxelResolutions =
            [SEGIStationeers.VoxelResolution.Medium, SEGIStationeers.VoxelResolution.Medium, SEGIStationeers.VoxelResolution.High, SEGIStationeers.VoxelResolution.High];
        private static readonly bool[] HalfResolutionLevels = [true, false, false, false];
        private static readonly bool[] VoxelAntiAliasingLevels = [false, false, true, true];
        private static readonly float[] VoxelSpaceSizes = [16.0f, 16.0f, 32.0f, 32.0f];
        private static readonly float[] ShadowSpaceSizes = [12.0f, 12.0f, 24.0f, 24.0f];
        private static readonly bool[] UseBilateralFilteringLevels = [true, true, true, true];
        private static readonly bool[] GaussianMipFilterLevels = [false, false, false, false];
        private static readonly bool[] InfiniteBouncesLevels = [false, false, false, false];
        private static readonly int[] ConesLevels = [4, 6, 8, 12];
        private static readonly int[] ConeTraceStepsLevels = [6, 8, 10, 14];
        private static readonly float[] ConeLengths = [1.0f, 1.0f, 1.0f, 1.0f];
        private static readonly float[] ConeWidths = [6.0f, 6.0f, 6.0f, 6.0f];
        public static SEGIStationeers.VoxelResolution VoxelResolution => VoxelResolutions[CurrentQualityLevel];
        public static bool HalfResolution => HalfResolutionLevels[CurrentQualityLevel];
        public static bool VoxelAntiAliasing => VoxelAntiAliasingLevels[CurrentQualityLevel];
        public static float VoxelSpaceSize => VoxelSpaceSizes[CurrentQualityLevel];
        public static float ShadowSpaceSize => ShadowSpaceSizes[CurrentQualityLevel];
        public static bool UseBilateralFiltering => UseBilateralFilteringLevels[CurrentQualityLevel];
        public static bool GaussianMipFilter => GaussianMipFilterLevels[CurrentQualityLevel];
        public static bool InfiniteBounces => InfiniteBouncesLevels[CurrentQualityLevel];
        public static int Cones => ConesLevels[CurrentQualityLevel];
        public static int ConeTraceSteps => ConeTraceStepsLevels[CurrentQualityLevel];
        public static float ConeLength => ConeLengths[CurrentQualityLevel];
        public static float ConeWidth => ConeWidths[CurrentQualityLevel];
        public static float ConeTraceBias => 0.65f;
        public static float TemporalBlendWeight => 0.01f;
        public static float GIGain
        {
            get
            {
                float baseValue = SEGIPlugin.GIGain?.Value ?? 0.6f;
                bool useMultiplier = SEGIPlugin.UseGainMultiplier?.Value ?? false;
                return useMultiplier ? baseValue * 10f : baseValue;
            }
        }

        public static float NearLightGain
        {
            get
            {
                float baseValue = SEGIPlugin.NearLightGain?.Value ?? 1.2f;
                bool useMultiplier = SEGIPlugin.UseGainMultiplier?.Value ?? false;
                return useMultiplier ? baseValue * 10f : baseValue;
            }
        }

        public static float SecondaryBounceGain
        {
            get
            {
                float baseValue = SEGIPlugin.SecondaryBounceGain?.Value ?? 0.4f;
                bool useMultiplier = SEGIPlugin.UseGainMultiplier?.Value ?? false;
                return useMultiplier ? baseValue * 10f : baseValue;
            }
        }
        public static float OcclusionStrength => 0.86f;
        public static float NearOcclusionStrength => 0.42f;
        public static float OcclusionPower => 1.0f;
        public static int InnerOcclusionLayers => 1;
        public static int SecondaryCones => 4;
        public static float SecondaryOcclusionStrength => 1.25f;
        public static float FarOcclusionStrength => 0.75f;
        public static float FarthestOcclusionStrength => 0.95f;
        public static float DayAmbientBrightness => SEGIPlugin.DayAmbientBrightness?.Value ?? 0.15f;
        public static float NightAmbientBrightness => SEGIPlugin.NightAmbientBrightness?.Value ?? 0.0f;
        public static bool LightweightMode => SEGIPlugin.LightweightMode?.Value ?? false;
        public static bool StochasticSampling => true;
        public static int TargetFramerate => SEGIPlugin.TargetFramerate?.Value ?? 75;

        // Unused
        public static bool Enabled => SEGIPlugin.Enabled?.Value ?? false;
        public static int ReflectionSteps => 0;
        public static float ReflectionOcclusionPower => 0.0f;

        public static string GetQualityName()
        {
            return CurrentQualityLevel switch
            {
                0 => "Low",
                1 => "Medium",
                2 => "High",
                3 => "Extreme",
                _ => "Unknown"
            };
        }
        public static float AdaptiveMinVoxelSpaceSize
        {
            get
            {
                int quality = SEGIPlugin.QualityLevel?.Value ?? 1;
                return VoxelSpaceSizes[quality] * 0.5f; // Start at 50% of max
            }
        }
        public static int AdaptiveStrategy => SEGIPlugin.AdaptiveStrategy?.Value ?? 0;
        public static bool AdaptivePerformance => SEGIPlugin.AdaptivePerformance?.Value ?? false;
        public static float AdaptiveScaleDownThreshold => 1.15f;  // Scale down if frame time >this
        public static float AdaptiveScaleUpThreshold => 0.85f;    // Scale up only if frame time <this
        public static float AdaptiveRate => 0.05f;
        public static bool AdaptiveMaxHalfResolution => HalfResolutionLevels[CurrentQualityLevel];
        public static bool AdaptiveMaxVoxelAA => VoxelAntiAliasingLevels[CurrentQualityLevel];
        public static bool AdaptiveMaxBilateralFiltering => UseBilateralFilteringLevels[CurrentQualityLevel];
        public static int AdaptiveMinVoxelRes => 64;
        public static int AdaptiveMinCones => 4;
        public static int AdaptiveMinConeTraceSteps => 6;

        private static readonly float[] AdaptiveMinVoxelSpaceSizeMultipliers = { 0.5f, 0.3f, 0.7f, 0.5f };
        public static float GetAdaptiveMinVoxelSpaceSize(int strategy)
        {
            return VoxelSpaceSizes[CurrentQualityLevel] * AdaptiveMinVoxelSpaceSizeMultipliers[strategy];
        }

        private static readonly float[] AdaptiveVoxelSpaceScaleThresholds = { 0.9f, 1.0f, 0.35f, 0.7f };
        private static readonly float[] AdaptiveVoxelSpaceScaleRanges = { 0.75f, 0.6f, 0.25f, 0.4f };
        public static float GetAdaptiveVoxelSpaceScaleThreshold(int strategy) => AdaptiveVoxelSpaceScaleThresholds[strategy];
        public static float GetAdaptiveVoxelSpaceScaleRange(int strategy) => AdaptiveVoxelSpaceScaleRanges[strategy];

        private static readonly float[] AdaptiveResolutionScaleThresholds = { 0.9f, 0.25f, 0.6f, 0.4f };
        private static readonly float[] AdaptiveResolutionScaleRanges = { 0.75f, 0.15f, 0.3f, 0.25f };
        public static float GetAdaptiveResolutionScaleThreshold(int strategy) => AdaptiveResolutionScaleThresholds[strategy];
        public static float GetAdaptiveResolutionScaleRange(int strategy) => AdaptiveResolutionScaleRanges[strategy];

        private static readonly float[] AdaptiveConesStepsScaleThresholds = { 1.0f, 0.4f, 1.0f, 0.2f };
        private static readonly float[] AdaptiveConesStepsScaleRanges = { 1.0f, 0.2f, 0.5f, 0.2f };
        public static float GetAdaptiveConesStepsScaleThreshold(int strategy) => AdaptiveConesStepsScaleThresholds[strategy];
        public static float GetAdaptiveConesStepsScaleRange(int strategy) => AdaptiveConesStepsScaleRanges[strategy];

        // public static float AdaptiveHalfResOnThreshold => 0.65f;   // Turn ON half res below this
        // public static float AdaptiveHalfResOffThreshold => 0.75f;  // Turn OFF half res above this
        private static readonly float[] AdaptiveHalfResOnThresholds = { 0.65f, 0.20f, 0.50f, 0.30f };
        private static readonly float[] AdaptiveHalfResOffThresholds = { 0.75f, 0.25f, 0.55f, 0.35f };
        public static float GetAdaptiveHalfResOnThreshold(int strategy) => AdaptiveHalfResOnThresholds[strategy];
        public static float GetAdaptiveHalfResOffThreshold(int strategy) => AdaptiveHalfResOffThresholds[strategy];

        // public static float AdaptiveVoxelAAOffThreshold => 0.55f;  // Turn OFF voxel AA below this
        // public static float AdaptiveVoxelAAOnThreshold => 0.65f;   // Turn ON voxel AA above this
        private static readonly float[] AdaptiveVoxelAAOffThresholds = { 0.55f, 0.30f, 0.60f, 0.20f };
        private static readonly float[] AdaptiveVoxelAAOnThresholds = { 0.65f, 0.40f, 0.70f, 0.30f };
        public static float GetAdaptiveVoxelAAOffThreshold(int strategy) => AdaptiveVoxelAAOffThresholds[strategy];
        public static float GetAdaptiveVoxelAAOnThreshold(int strategy) => AdaptiveVoxelAAOnThresholds[strategy];

        // public static float AdaptiveBilateralOffThreshold => 0.65f; // Turn OFF filtering below this
        // public static float AdaptiveBilateralOnThreshold => 0.75f;  // Turn ON filtering above this
        private static readonly float[] AdaptiveBilateralOffThresholds = { 0.65f, 0.30f, 0.60f, 0.20f };
        private static readonly float[] AdaptiveBilateralOnThresholds = { 0.75f, 0.40f, 0.70f, 0.30f };
        public static float GetAdaptiveBilateralOffThreshold(int strategy) => AdaptiveBilateralOffThresholds[strategy];
        public static float GetAdaptiveBilateralOnThreshold(int strategy) => AdaptiveBilateralOnThresholds[strategy];

        // public static float AdaptiveVoxelInterval3Threshold => 0.05f; // Use interval=3 below this
        // public static float AdaptiveVoxelInterval2Threshold => 0.15f; // Use interval=2 below this
        // public static float AdaptiveVoxelInterval1Threshold => 0.25f; // Use interval=1 above this
        private static readonly float[] AdaptiveVoxelInterval3Thresholds = { 0.05f, 0.15f, 0.15f, 0.70f };
        private static readonly float[] AdaptiveVoxelInterval2Thresholds = { 0.15f, 0.20f, 0.20f, 0.85f };
        private static readonly float[] AdaptiveVoxelInterval1Thresholds = { 0.25f, 0.30f, 0.30f, 0.95f };
        public static float GetAdaptiveVoxelInterval3Threshold(int strategy) => AdaptiveVoxelInterval3Thresholds[strategy];
        public static float GetAdaptiveVoxelInterval2Threshold(int strategy) => AdaptiveVoxelInterval2Thresholds[strategy];
        public static float GetAdaptiveVoxelInterval1Threshold(int strategy) => AdaptiveVoxelInterval1Thresholds[strategy];
    }
}