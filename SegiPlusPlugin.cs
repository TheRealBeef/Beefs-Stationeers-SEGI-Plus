using System;
using System.Collections;
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

        public static ConfigEntry<bool> Enabled;
        public static ConfigEntry<int> QualityLevel;
        public static ConfigEntry<float> SecondaryBounceGain;
        public static ConfigEntry<float> NearLightGain;
        public static ConfigEntry<float> GIGain;
        public static ConfigEntry<float> DayAmbientBrightness;
        public static ConfigEntry<float> NightAmbientBrightness;
        public static ConfigEntry<bool> LightweightMode;

        private static SEGIStationeers SegiStationeersInstance { get; set; }

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            BindAllConfigs();
            Log.LogInfo($"Plugin {PluginInfo.PLUGIN_NAME} is loaded!");
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
        }

        private void OnDestroy()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void BindAllConfigs()
        {
            Enabled = Config.Bind("General", "Enabled", true, "Enable SEGI Plus global illumination. There is an F11 config menu in-game too");
            NearLightGain = Config.Bind("Gain Knobs", "Near Light Gain", 0.0f,
                new ConfigDescription("Near light gain", new AcceptableValueRange<float>(0f, 2f)));
            GIGain = Config.Bind("Gain Knobs", "Global Illumination Gain", 3.0f,
                new ConfigDescription("Global illumination gain", new AcceptableValueRange<float>(0f, 8f)));
            SecondaryBounceGain = Config.Bind("Gain Knobs", "Secondary Bounce Gain", 0.0f,
                new ConfigDescription("Secondary bounce gain", new AcceptableValueRange<float>(0f, 2.0f)));
            DayAmbientBrightness = Config.Bind("Lighting", "Day Ambient Brightness", 0.05f,
                new ConfigDescription("Ambient light brightness during day", new AcceptableValueRange<float>(0.001f, 0.25f)));
            NightAmbientBrightness = Config.Bind("Lighting", "Night Ambient Brightness", 0.0f,
                new ConfigDescription("Ambient light brightness during night", new AcceptableValueRange<float>(0.000f,0.01f)));
            QualityLevel = Config.Bind("Performance", "Quality Level - 0 for Low, 3 for Extreme.", 1,
                new ConfigDescription("Quality (0=Low, 1=Medium, 2=High, 3=Extreme)",
                    new AcceptableValueRange<int>(0, 3)));
            LightweightMode = Config.Bind("Performance", "**Lightweight Mode**", false,
                "If you enable this it cull most objects except the emissive ones during voxelization and runs *way* faster, at the cost of light leakage. Can be combined with any quality setting.");
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
        public static float TemporalBlendWeight => 0.05f;
        public static float GIGain => SEGIPlugin.GIGain?.Value ?? 0.6f;
        public static float NearLightGain => SEGIPlugin.NearLightGain?.Value ?? 1.2f;
        public static float SecondaryBounceGain => SEGIPlugin.SecondaryBounceGain?.Value ?? 0.4f;
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
    }
}