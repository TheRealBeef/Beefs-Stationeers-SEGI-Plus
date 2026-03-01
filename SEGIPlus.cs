using UnityEngine;
using UnityEngine.Rendering;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Linq;
using Assets.Scripts.Objects.Entities;
using Assets.Scripts.Objects;
using CharacterCustomisation;

namespace BeefsSEGIPlus;

[ExecuteInEditMode]
[ImageEffectAllowedInSceneView]
[RequireComponent(typeof(Camera))]
[AddComponentMenu("Image Effects/Sonic Ether/SEGI")]
public class SEGIStationeers : MonoBehaviour
{
    private const float SpatialCullUpdateInterval = 0.1f; //s
    private const int mipLevels = 6;

    #if SEGI_PROFILER
        public readonly SEGIProfiler Profiler = new();
    #else
        private readonly struct _ProfilerNoop
        {
            public bool ShouldSkip(string _) => false;
            public bool IsActive => false;
            public bool ForceSunDepthEveryFrame => false;
            public bool ForceScrollEveryFrame => false;
            public void EndFrame() { }
        }
        private readonly _ProfilerNoop Profiler = default;
    #endif


    private bool initalized = false;
    private bool notReadyToRender = false;
    private bool _previousLightweightMode = false;

    private int sunShadowResolution = 256;
    private int frameCounter;
    private int voxelFlipFlop;
    private int prevSunShadowResolution;

    private float shadowSpaceDepthRatio = 10.0f;

    // private float TargetFrameTime => 1.0f / ConfigData.TargetFramerate;
    private float TargetFrameTime => effectiveTargetFrameTime;
    private float currentAdaptiveScale = 1.0f;
    private float frameTimeAverage = 0.016f;
    private float _bucketSum = 0f;
    private float _bucketMax = 0f;
    private int _bucketCount = 0;
    private float _bucketTimer = 0f;
    private const float BucketDuration = 1.0f;
    private Queue<float> _bucketAverages = new Queue<float>();
    private const int MaxBuckets = 15;
    private float adaptiveLongTermAcc = 0f;
    private float adaptiveLongTermTimer = 0f;
    private const float AdaptiveLongTermInterval = 15.0f;
    private const float AdaptiveLongTermThreshold = 0.95f;
    private int adaptiveVoxelResolution;
    private float adaptiveVoxelSpaceSize;
    private float adaptiveShadowSpaceSize;
    private int adaptiveCones;
    private int adaptiveConeTraceSteps;
    private bool adaptiveHalfResolution;
    private bool adaptiveVoxelAA;
    private int adaptiveMaxVoxelRes;
    private float adaptiveMaxVoxelSpaceSize;
    private float adaptiveMaxShadowSpaceSize;
    private int adaptiveMaxCones;
    private int adaptiveMaxConeTraceSteps;
    private float _lastAdaptiveChangeTime = -999f;
    private const float AdaptiveChangeCooldown = 15.0f;
    private int _prevQualityLevel = -1;
    private bool _prevDenseVoxelMode = false;
    private int _prevAdaptiveStrategy = -1;
    private int _prevTargetFramerate = -1;
    private bool _prevAdaptivePerformance = false;
    private float performanceMarginMultiplier = 1.0f;
    private float effectiveTargetFrameTime;
    private bool isFrameCapped = false;
    private int cachedFrameCap = -1;
    private float lastFrameCapCheck = 0f;
    private const float FrameCapCheckInterval = 1.0f;
    private float _cachedGIGain = -999f;
    private float _cachedEmissiveLightGain = -999f;
    private Vector4 _cachedEmissiveBubbleParams = new Vector4(0, 0, 0, -999f);
    private float _cachedSecondaryBounceGain = -999f;
    private float _cachedTraceLength = -999f;
    private float _cachedConeWidth = -999f;
    private float _cachedOcclusionStrength = -999f;
    private float _cachedOcclusionPower = -999f;
    private float _cachedConeTraceBias = -999f;
    private float _cachedNearOcclusionStrength = -999f;
    private float _cachedFarOcclusionStrength = -999f;
    private float _cachedFarthestOcclusionStrength = -999f;
    private int _cachedInnerOcclusionLayers = -999;
    private int _cachedAdaptiveCones = -999;
    private int _cachedAdaptiveConeTraceSteps = -999;
    private int _cachedAdaptiveHalfRes = -999;

    private const int ProbeSpacing = 3;
    private ComputeShader coneTraceCompute;
    private int _probeTraceKernel;
    private int _probeInterpolateKernel;

    private ComputeShader temporalBlendCompute;
    private int _temporalBlendKernel;
    private RenderTexture probeIrradiance;


    private static readonly string ModDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
    private static AssetBundle _bundle;
    public static AssetBundle Bundle =>
        _bundle ??= AssetBundle.LoadFromFile(Path.Combine(ModDirectory, "Content", "segi.asset"));

    private static AssetBundle _segibeefedit;
    public static AssetBundle SegiBeefEdit => _segibeefedit ??= AssetBundle.LoadFromFile(Path.Combine(ModDirectory, "Content",
        "segibeefedit.asset"));

    private static readonly HashSet<string> ExcludedObjectNames = new()
    {
        "StructureWeatherStation",
        "StructureAdvancedFurnace",
        "ItemEvaSuit",
        "BODY_RENDERER",
        "VisorFrost",
        "GlassFrost"
    };

    private static readonly HashSet<string> IncludedObjectNames = new()
    {
        "OnOffNoShadow",
        "SwitchOnOff",
        "SwitchMode",
        "StructureCircuitHousing",
        "StructureCircuitHousingCompact",
        // "VentFlowIndicator",
        // "HeatingIndicator",
        // "Bulb",
        "StructureFlashingLight",
        // "light",
        "Switch"
    };

    private static readonly HashSet<string> EmissiveKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "Emissive"
    };

    private static readonly HashSet<string> LoggedExclusions = new();

    public bool sphericalSkylight;
    public bool visualizeSunDepthTexture;
    public bool visualizeGI;
    public bool visualizeVoxels;
    public bool updateGI = true;
    public bool bypassRendering = false;
    public LayerMask giCullingMask = int.MaxValue;
    public Light sun;
    public Color skyColor;
    public Transform followTransform;

    [Serializable]
    [Flags]
    public enum VoxelResolution
    {
        Medium = 128,
        High = 256
    }

    private float VoxelScaleFactor => (float)ConfigData.VoxelResolution / 256.0f;
    private Material material;
    private Camera attachedCamera;
    private Transform shadowCameraTransform;
    private Camera shadowCamera;
    private GameObject shadowCameraGameObject;
    private Texture2D[] blueNoise;
    private Shader sunDepthShader;
    private RenderTexture sunDepthTexture;
    private RenderTexture sunDepthTextureBack;
    private RenderTexture previousGIResult;
    private RenderTexture previousCameraDepth;
    private RenderTexture _previousGIResultBack;
    private RenderTexture _previousCameraDepthBack;
    private Matrix4x4 _prevProjectionInverse;
    private Matrix4x4 _prevCameraToWorld;
    private Vector3 _prevCameraPosition;
    private float _smoothedCameraSpeed;
    private RenderTexture[] integerVolume4 = new RenderTexture[4];
    private RenderTexture[] volumeTextures;
    private RenderTexture _combinedVolume;
    private RenderTexture volumeTextureB;
    private RenderTexture activeVolume;
    private RenderTexture previousActiveVolume;
    private RenderTexture dummyVoxelTextureAAScaled;
    private RenderTexture dummyVoxelTextureFixed;
    private Shader voxelizationShaderBeefEdit;
    private ComputeShader clearCompute;
    private ComputeShader transferIntsCompute;
    private ComputeShader mipFilterCompute;
    private ComputeShader mergeVolumesCompute;
    private ComputeShader sliceClearCompute;
    private ComputeShader atrousFilterCompute;
    private int _atrousKernel;
    private ComputeShader sunBakeCompute;
    private int _sunBakeKernel;
    private Camera voxelCamera;
    private GameObject voxelCameraGameObject;
    private GameObject leftViewPoint;
    private GameObject topViewPoint;
    private Vector3 voxelSpaceOrigin;
    private Vector3 previousVoxelSpaceOrigin;
    private Vector3 voxelSpaceOriginDelta;

    private RenderTexture[] geomCacheVolume4 = new RenderTexture[4];
    private RenderTexture[] geomCacheCopy4 = new RenderTexture[4];
    private bool _cacheNeedsFullRefresh = true;
    private bool _hybridCacheReady = false;
    private int3Offset _geomCacheWrapOffset;
    private RenderTexture _persistGI1;
    private RenderTexture _persistGI2;
    private RenderTexture _persistDepth;
    private RenderTexture _persistNormal;
    private RenderTexture _persistGI3; // half-res
    private RenderTexture _persistGI4;// half-res
    private int _prevGIRenderRes = -1;

    private const int DEFAULT_GEOM_BATCH_COUNT = 4;
    private const float FORWARD_ORIGIN_BIAS = 0.25f; // origin 25% forward
    private int _geomBatchCount = DEFAULT_GEOM_BATCH_COUNT;
    private struct RendererWeight
    {
        public Renderer renderer;
        public int triangles;
    }
    private List<RendererWeight> _weightedRenderers = new();
    private List<RendererWeight> _geomBuildBuffer = new();
    private List<Renderer>[] _geomBatches = new List<Renderer>[DEFAULT_GEOM_BATCH_COUNT];
    private int[] _batchWeights = new int[DEFAULT_GEOM_BATCH_COUNT];
    private int _currentBuildBatch = 0;
    private bool _buildCycleActive = false;
    private Vector3 _combinedVolumeOrigin;
    private int _lastRendererSetHash = 0;
    private float _lastGeomCacheUpdate = 0f;
    private Coroutine _geomCacheCoroutine;
    private bool _geomBatchesReady = false;

    private int BuildCycleLength => _geomBatchCount + 2;
    private List<Renderer> _terrainBatch = new();
    private const int TerrainLayer = 16;
    private const float TerrainAlphaScale = 0.4f;
    private Vector3 _buildTargetOrigin;  // where the back-buffer is centered
    private bool _scrollBuildPending;
    private float _lastLightweightSunRefresh = -999f;
    private const float LightweightSunRefreshInterval = 1.0f;
    private Vector3 _lastSunDirection = Vector3.down;
    private Vector3 _sunShadowOrigin = Vector3.zero;
    private Quaternion rotationFront = new(0.0f, 0.0f, 0.0f, 1.0f);
    private Quaternion rotationLeft = new(0.0f, 0.7f, 0.0f, 0.7f);
    private Quaternion rotationTop = new(0.7f, 0.0f, 0.0f, 0.7f);

    private readonly Dictionary<GameObject, int> _layerRestoreCache = new();
    private List<Renderer> _culledEmissiveRenderers = new();
    private List<Renderer> _cachedEmissiveRenderers = new();
    private List<Renderer> _emissiveBuildBuffer = new();
    private float _lastSpatialCullUpdate = 0f;
    private float _lastEmissiveCacheUpdate = 0f;
    private Coroutine _emissiveCacheCoroutine;

    private readonly HashSet<int> _fixedRobotMaterialIds = new();
    private bool _robotFixEventHooked;
    private Coroutine _robotFixCoroutine;
    private static readonly List<Material> _sharedMaterialsBuffer = new();

    private struct Pass
    {
        public static int DiffuseTrace = 0;
        public static int BilateralBlur = 1;
        public static int BlendWithScene = 2;
        public static int GetCameraDepthTexture = 3;
        public static int GetWorldNormals = 4;
        public static int VisualizeGI = 5;
        public static int VisualizeVoxels = 6;
        public static int BilateralUpsample = 7;
    }

    public SystemSupported systemSupported;

    public struct SystemSupported : IEquatable<SystemSupported>
    {
        public bool HDRTextures;
        public bool RIntTextures;
        public bool DirectX11;
        public bool VolumeTextures;
        public bool PostShader;
        public bool SunDepthShader;
        public bool VoxelizationShader;
        public bool VoxelizationLightShader;

        public readonly bool FullFunctionality => HDRTextures && RIntTextures && DirectX11 && VolumeTextures &&
                                                  PostShader && SunDepthShader && VoxelizationShader &&
                                                  VoxelizationLightShader;

        public override bool Equals(object obj)
        {
            return obj is SystemSupported systemSupported && Equals(systemSupported);
        }

        public bool Equals(SystemSupported other)
        {
            return FullFunctionality == other.FullFunctionality;
        }

        public override int GetHashCode()
        {
            throw new NotImplementedException();
        }

        public static bool operator ==(SystemSupported left, SystemSupported right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(SystemSupported left, SystemSupported right)
        {
            return !left.Equals(right);
        }
    }

    private int MipFilterKernel => ConfigData.GaussianMipFilter ? 1 : 0;
    // private int DummyVoxelResolution => _adaptiveVoxelResolution * (ConfigData.VoxelAntiAliasing ? 2 : 1);
    private int DummyVoxelResolution => adaptiveVoxelResolution * (adaptiveVoxelAA ? 2 : 1);
    private int GIRenderRes => ConfigData.HalfResolution ? 2 : 1;

    private void Start()
    {
        InitCheck();
    }

    private void OnEnable()
    {
        notReadyToRender = true;
        try
        {
            InitCheck();
            ResizeRenderTextures();
            ResizePostProcessRTs();
            CheckSupport();
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
            StartRobotEmissionFix();
            notReadyToRender = false;
        }
        catch (Exception ex)
        {
            SEGIPlugin.Log.LogError($"SEGI Plus OnEnable failed: {ex.Message}");
            notReadyToRender = true;
            initalized = false;
        }
    }

    private void OnDisable()
    {
        notReadyToRender = true;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        try
        {
            Cleanup();
        }
        catch (Exception ex)
        {
            SEGIPlugin.Log.LogError($"SEGI Plus OnDisable cleanup error: {ex.Message}");
            initalized = false;
        }
    }

    private void Update()
    {
        if (notReadyToRender) return;
        if (bypassRendering) return;

        if (previousGIResult == null || _persistGI1 == null)
        {
            ResizeRenderTextures();
            ResizePostProcessRTs();
        }

        if (previousGIResult.width != attachedCamera.pixelWidth ||
            previousGIResult.height != attachedCamera.pixelHeight ||
            _prevGIRenderRes != GIRenderRes)
        {
            ResizeRenderTextures();
            ResizePostProcessRTs();
        }

        sunShadowResolution = ConfigData.SunShadowResolution;
#if SEGI_PROFILER
        sunShadowResolution = DebugOverrides.SunShadowResolution ?? sunShadowResolution;
#endif
        if (sunShadowResolution != prevSunShadowResolution) ResizeSunShadowBuffer();

        int desiredBatchCount = DEFAULT_GEOM_BATCH_COUNT;
        desiredBatchCount = Mathf.Clamp(desiredBatchCount, 1, 16);
        if (desiredBatchCount != _geomBatchCount)
        {
            _geomBatchCount = desiredBatchCount;
            _geomBatches = new List<Renderer>[_geomBatchCount];
            _batchWeights = new int[_geomBatchCount];
            for (int i = 0; i < _geomBatchCount; i++)
                _geomBatches[i] = new List<Renderer>();
            _geomBatchesReady = false;
            _lastRendererSetHash = 0;
        }

        prevSunShadowResolution = sunShadowResolution;

        if (volumeTextures[0].width != adaptiveVoxelResolution) CreateVolumeTextures();

        if (dummyVoxelTextureAAScaled.width != DummyVoxelResolution) ResizeDummyTexture();

        if (ConfigData.LightweightMode != _previousLightweightMode)
        {
            OnLightweightModeChanged();
            _previousLightweightMode = ConfigData.LightweightMode;
        }

        {
            int curQuality = SEGIPlugin.QualityLevel?.Value ?? 1;
            bool curDense = ConfigData.DenseVoxelMode;
            if (curQuality != _prevQualityLevel || curDense != _prevDenseVoxelMode)
            {
                if (_prevQualityLevel >= 0)
                {
                    _cacheNeedsFullRefresh = true;
                    ResetFrameTimeTracking();
                    currentAdaptiveScale = 1.0f;
                    _lastAdaptiveChangeTime = -999f;
                }
                _prevQualityLevel = curQuality;
                _prevDenseVoxelMode = curDense;
            }

            int curStrategy = ConfigData.AdaptiveStrategy;
            int curTargetFps = SEGIPlugin.TargetFramerate?.Value ?? 60;
            bool curAdaptive = ConfigData.AdaptivePerformance;
            if (curStrategy != _prevAdaptiveStrategy || curTargetFps != _prevTargetFramerate ||
                curAdaptive != _prevAdaptivePerformance)
            {
                if (_prevAdaptiveStrategy >= 0)
                {
                    ResetFrameTimeTracking();
                    currentAdaptiveScale = 1.0f;
                    _lastAdaptiveChangeTime = -999f;
                }
                _prevAdaptiveStrategy = curStrategy;
                _prevTargetFramerate = curTargetFps;
                _prevAdaptivePerformance = curAdaptive;
            }
        }

        if (ConfigData.AdaptivePerformance)
        {
            UpdateFrameCapStatus();
            UpdateFrameData();
            UpdateAdaptivePerformance();
            if (volumeTextures != null && volumeTextures[0].width != adaptiveVoxelResolution)
            {
                CreateVolumeTextures();
            }
        }
        else
        {
            UpdateAdaptivePerformance(); // it'll just set defaults when is false
        }
    }

    private void OnPreRender()
    {
        if (bypassRendering) return;

        if (!voxelCamera || !shadowCamera) initalized = false;

        InitCheck();

        if (notReadyToRender) return;

        if (!updateGI) return;

        if (volumeTextures == null || volumeTextures.Length == 0 || volumeTextures[0] == null)
        {
            SEGIPlugin.Log.LogWarning("Volume textures not ready");
            return;
        }

        var previousActive = RenderTexture.active;

        if (Profiler.ShouldSkip("SEGI_All"))
        {
            RenderTexture.active = previousActive;
            return;
        }

        // flip flop has to happen always otherwise stuttering
        activeVolume = (voxelFlipFlop == 0) ? volumeTextures[0] : volumeTextureB;
        previousActiveVolume = (voxelFlipFlop == 0) ? volumeTextureB : volumeTextures[0];
        if (_combinedVolume != null)
        {
            Shader.SetGlobalTexture("SEGIVolume", _combinedVolume);
            Shader.SetGlobalTexture("SEGIVolumeTexture1", _combinedVolume);
        }

        // Shader.SetGlobalInt("SEGIVoxelAA", ConfigData.VoxelAntiAliasing ? 1 : 0);
        Shader.SetGlobalInt("SEGIVoxelAA", adaptiveVoxelAA ? 1 : 0);

        // activeVolume =
            //     voxelFlipFlop == 0
            //         ? volumeTextures[0]
            //         : volumeTextureB; //Flip-flopping volume textures to avoid simultaneous read and write errors in shaders
            // previousActiveVolume = voxelFlipFlop == 0 ? volumeTextureB : volumeTextures[0];

            //Setup the voxel volume origin position
            var interval =
                adaptiveVoxelSpaceSize /
                4.0f; //The interval at which the voxel volume will be "locked" in world-space
            Vector3 rawOrigin;
            if (followTransform)
                rawOrigin = followTransform.position;
            else
                rawOrigin = transform.position;

#if SEGI_PROFILER
            if (DebugOverrides.ForwardOriginBias != false)
#endif
            {
                Vector3 flatForward = transform.forward;
                flatForward.y = 0f;
                flatForward.Normalize();
                rawOrigin += flatForward * (adaptiveVoxelSpaceSize * FORWARD_ORIGIN_BIAS);
            }

            Vector3 desiredOrigin = new Vector3(Mathf.Round(rawOrigin.x / interval) * interval,
                Mathf.Round(rawOrigin.y / interval) * interval, Mathf.Round(rawOrigin.z / interval) * interval);

            if (_buildCycleActive)
            {
                voxelSpaceOriginDelta = Vector3.zero;
                Shader.SetGlobalVector("SEGIVoxelSpaceOriginDelta", Vector3.zero);
            }
            else
            {
                Vector3 potentialDelta = desiredOrigin - voxelSpaceOrigin;
                bool wouldScroll = _hybridCacheReady && !_cacheNeedsFullRefresh &&
                    (Mathf.Abs(potentialDelta.x) > 0.001f ||
                     Mathf.Abs(potentialDelta.y) > 0.001f ||
                     Mathf.Abs(potentialDelta.z) > 0.001f);

                if (!wouldScroll && Profiler.ForceScrollEveryFrame && _hybridCacheReady && !_cacheNeedsFullRefresh)
                {
                    float sign = (Time.frameCount % 2 == 0) ? 1f : -1f;
                    desiredOrigin = voxelSpaceOrigin + new Vector3(
                        sign * adaptiveVoxelSpaceSize / adaptiveVoxelResolution, 0f, 0f);
                    wouldScroll = true;
                }

                if (wouldScroll)
                {
                    _buildTargetOrigin = desiredOrigin;
                    _scrollBuildPending = true;
                    voxelSpaceOriginDelta = Vector3.zero;
                    Shader.SetGlobalVector("SEGIVoxelSpaceOriginDelta", Vector3.zero);
                }
                else
                {
                    voxelSpaceOrigin = desiredOrigin;
                    voxelSpaceOriginDelta = voxelSpaceOrigin - previousVoxelSpaceOrigin;
                    Shader.SetGlobalVector("SEGIVoxelSpaceOriginDelta", voxelSpaceOriginDelta / adaptiveVoxelSpaceSize);
                    previousVoxelSpaceOrigin = voxelSpaceOrigin;
                    _buildTargetOrigin = voxelSpaceOrigin;
                }
            }


            //Set the voxel camera (proxy camera used to render the scene for voxelization) parameters
            voxelCamera.enabled = false;
            voxelCamera.orthographic = true;
            voxelCamera.orthographicSize = adaptiveVoxelSpaceSize * 0.5f;
            voxelCamera.nearClipPlane = 0.0f;
            voxelCamera.farClipPlane = adaptiveVoxelSpaceSize;
            voxelCamera.depth = -2;
            voxelCamera.renderingPath = RenderingPath.Forward;
            voxelCamera.clearFlags = CameraClearFlags.Color;
            voxelCamera.backgroundColor = Color.black;
            voxelCamera.cullingMask = giCullingMask;

            //Move the voxel camera game object and other related objects to the above calculated voxel space origin
            PositionVoxelCameras(voxelSpaceOrigin);

            //Set matrices needed for voxelization
            Shader.SetGlobalMatrix("WorldToCamera", attachedCamera.worldToCameraMatrix);
            Shader.SetGlobalMatrix("SEGIVoxelProjection", voxelCamera.projectionMatrix);
            Shader.SetGlobalMatrix("SEGIVoxelProjectionInverse", voxelCamera.projectionMatrix.inverse);
            Shader.SetGlobalInt("SEGIVoxelResolution", adaptiveVoxelResolution);
            { var v = ConfigData.GIGain;
            if (!Mathf.Approximately(v, _cachedGIGain))
            { Shader.SetGlobalFloat("GIGain", v); material.SetFloat("GIGain", v); _cachedGIGain = v; } }

            { var v = ConfigData.EmissiveLightGain;
            if (!Mathf.Approximately(v, _cachedEmissiveLightGain))
            { Shader.SetGlobalFloat("EmissiveLightGain", v); _cachedEmissiveLightGain = v; } }

            // Emissive exclusion bubble
            {
                float radius = ConfigData.EmissiveBubbleEnabled ? 0.65f : 0f;
                Vector3 center = transform.position;
                var bp = new Vector4(center.x, center.y, center.z, radius);
                if (bp != _cachedEmissiveBubbleParams)
                {
                    Shader.SetGlobalVector("_EmissiveBubbleParams", bp);
                    _cachedEmissiveBubbleParams = bp;
                }
            }

            { var v = ConfigData.SecondaryBounceGain;
            if (!Mathf.Approximately(v, _cachedSecondaryBounceGain))
            { Shader.SetGlobalFloat("SEGISecondaryBounceGain", v); _cachedSecondaryBounceGain = v; } }

            { var v = ConfigData.InnerOcclusionLayers;
            if (v != _cachedInnerOcclusionLayers)
            { Shader.SetGlobalInt("SEGIData.InnerOcclusionLayers", v); _cachedInnerOcclusionLayers = v; } }

            shadowCamera.cullingMask = giCullingMask;
            shadowCamera.renderingPath = RenderingPath.Forward;
            shadowCamera.depthTextureMode |= DepthTextureMode.None;
            shadowCamera.orthographicSize = adaptiveShadowSpaceSize;

            float shadowCamDist = adaptiveShadowSpaceSize * 0.5f * shadowSpaceDepthRatio;
            float margin = adaptiveShadowSpaceSize * 1.5f;
            shadowCamera.nearClipPlane = Mathf.Max(0.01f, shadowCamDist - margin);
            shadowCamera.farClipPlane = shadowCamDist + margin;

            Shader.SetGlobalVector("SEGISunlightVector",
                sun ? Vector3.Normalize(sun.transform.forward) : Vector3.up);
            Shader.SetGlobalColor("SEGISkyColor", skyColor);

            var sunAboveHorizon = sun != null && Vector3.Dot(-sun.transform.forward, Vector3.up) > 0f;
            var currentSunDirection = sun != null ? -sun.transform.forward : Vector3.down;

            if (ConfigData.LightweightMode && sunAboveHorizon)
            {
                bool sunMoved = Vector3.Angle(_lastSunDirection, currentSunDirection) > 0.5f;
                float shadowOriginDrift = Vector3.Distance(voxelSpaceOrigin, _sunShadowOrigin);
                bool cameraDrifted = shadowOriginDrift > adaptiveShadowSpaceSize * 0.25f;
                bool periodicRefresh = Time.unscaledTime - _lastLightweightSunRefresh >= LightweightSunRefreshInterval;
                bool shouldRefreshSun = sunMoved || cameraDrifted || periodicRefresh;

                if (Profiler.IsActive)
                    shouldRefreshSun = !Profiler.ShouldSkip("SunDepth");

                if (shouldRefreshSun)
                {
                    Graphics.SetRenderTarget(sunDepthTexture);
                    shadowCamera.SetTargetBuffers(sunDepthTexture.colorBuffer, sunDepthTexture.depthBuffer);
                    var prevLodBias = QualitySettings.lodBias;
                    QualitySettings.lodBias = float.MaxValue;
                    shadowCamera.RenderWithShader(sunDepthShader, "");
                    QualitySettings.lodBias = prevLodBias;
                    _lastSunDirection = currentSunDirection;
                    _sunShadowOrigin = voxelSpaceOrigin;
                    _lastLightweightSunRefresh = Time.unscaledTime;
                }
            }

            if (sunAboveHorizon)
            {
                Shader.SetGlobalTexture("SEGISunDepth", sunDepthTexture);
                Shader.SetGlobalColor("GISunColor", sun != null ? sun.color : Color.black);
            }
            else
            {
                Shader.SetGlobalVector("SEGISunlightVector", Vector3.up);
                Shader.SetGlobalColor("GISunColor", Color.black);
                Graphics.SetRenderTarget(sunDepthTexture);
                GL.Clear(true, true, Color.black);
                Shader.SetGlobalTexture("SEGISunDepth", sunDepthTexture);
            }

            if (_scrollBuildPending)
            {
                _scrollBuildPending = false;

                Vector3 scrollDelta = _buildTargetOrigin - voxelSpaceOrigin;
                Vector3 voxelDelta = scrollDelta / adaptiveVoxelSpaceSize * adaptiveVoxelResolution;
                if (Mathf.Abs(voxelDelta.x) > adaptiveVoxelResolution / 2 ||
                    Mathf.Abs(voxelDelta.y) > adaptiveVoxelResolution / 2 ||
                    Mathf.Abs(voxelDelta.z) > adaptiveVoxelResolution / 2)
                {
                    ClearIntVolume4(geomCacheVolume4, adaptiveVoxelResolution);
                    _geomCacheWrapOffset = new int3Offset(0, 0, 0);
                }

                if (!_hybridCacheReady) { /* skip*/ }
                else if (_geomBatchesReady)
                {
                    _buildCycleActive = true;
                    _currentBuildBatch = 0;
                }
            }

            if (ConfigData.LightweightMode)
            {
                Shader.SetGlobalInt("SEGIStripSun", 0);
                Shader.SetGlobalFloat("SEGIAlphaScale", 1.0f);
                Shader.SetGlobalInt("SEGISkipInnerOcclusion", 0);

                if (!Profiler.ShouldSkip("ClearInts"))
                    ClearIntVolume4(integerVolume4, adaptiveVoxelResolution);

                UpdateEmissiveCache();

                Shader.SetGlobalInt("SEGISliceMin", 0);
                Shader.SetGlobalInt("SEGISliceMax", adaptiveVoxelResolution);
                SetIntVolume4RandomWrite(integerVolume4);
                voxelCamera.targetTexture = dummyVoxelTextureAAScaled;
                var emissiveRenderersToUse = GetEmissiveRenderers();
                if (emissiveRenderersToUse.Count == 0) return;
                const int tempLayer = 31;
                _layerRestoreCache.Clear();
                    foreach (var r in emissiveRenderersToUse)
                    {
                    if (r != null && r.gameObject.layer != tempLayer)
                        {
                        _layerRestoreCache[r.gameObject] = r.gameObject.layer;
                                r.gameObject.layer = tempLayer;
                            }
                        }

                    var originalMask = voxelCamera.cullingMask;
                    voxelCamera.cullingMask = 1 << tempLayer;
                    voxelCamera.allowHDR = true;

                    if (!Profiler.ShouldSkip("EmissiveVoxelize"))
                        voxelCamera.RenderWithShader(voxelizationShaderBeefEdit, "");

                    voxelCamera.cullingMask = originalMask;
                foreach (var kvp in _layerRestoreCache)
                    {
                    if (kvp.Key != null)
                                kvp.Key.layer = kvp.Value;
                        }
                    Graphics.ClearRandomWriteTargets();

                int lwTransferGroups = Mathf.CeilToInt((float)adaptiveVoxelResolution / 4f);
                if (!Profiler.ShouldSkip("TransferInts"))
                {
                    transferIntsCompute.SetTexture(0, "Result", activeVolume);
                    transferIntsCompute.SetTexture(0, "PrevResult", previousActiveVolume);
                    SetIntVolume4OnCompute(transferIntsCompute, 0, "RG0", integerVolume4);
                    transferIntsCompute.SetInt("VoxelAA", adaptiveVoxelAA ? 1 : 0);
                    transferIntsCompute.SetInt("Resolution", adaptiveVoxelResolution);
                    transferIntsCompute.SetVector("VoxelOriginDelta",
                        voxelSpaceOriginDelta / adaptiveVoxelSpaceSize * adaptiveVoxelResolution);
                    transferIntsCompute.Dispatch(0, lwTransferGroups, lwTransferGroups, lwTransferGroups);
                }
            }
            else
            {

                UpdateGeomRendererCache();

                Shader.SetGlobalInt("SEGISliceMin", 0);
                Shader.SetGlobalInt("SEGISliceMax", adaptiveVoxelResolution);

                bool shouldStartBuild = false;
                if (_cacheNeedsFullRefresh && _geomBatchesReady)
                {
                    _cacheNeedsFullRefresh = false;
                    shouldStartBuild = true;
                }
                bool sunDirectionChanged = Vector3.Angle(_lastSunDirection, currentSunDirection) > 0.5f;
                if (sunDirectionChanged && !_buildCycleActive && _geomBatchesReady && _hybridCacheReady)
                {
                    shouldStartBuild = true;
                }
                if (shouldStartBuild)
                {
                    _buildCycleActive = true;
                    _currentBuildBatch = 0;
                }

                if (!_buildCycleActive && _geomBatchesReady && _hybridCacheReady)
                {
                    _buildCycleActive = true;
                    _currentBuildBatch = 0;
                }

                if (_buildCycleActive && _geomBatchesReady)
                {
                    int batchIdx = _currentBuildBatch;

                    if (batchIdx == 0)
                    {
                        if (!Profiler.ShouldSkip("ClearInts"))
                            ClearIntVolume4(geomCacheCopy4, adaptiveVoxelResolution);

                        // Clear sun depth back-buffer for fresh accumulation
                        if (sunAboveHorizon)
                        {
                            var prevActive = RenderTexture.active;
                            Graphics.SetRenderTarget(sunDepthTextureBack);
                            GL.Clear(true, true, Color.black);
                            RenderTexture.active = prevActive;
                        }
                    }

                    if (batchIdx < _geomBatchCount && _geomBatches[batchIdx].Count > 0)
                    {
                        PositionVoxelCameras(_buildTargetOrigin);
                        const int tempLayer = 31;
                        _layerRestoreCache.Clear();
                        var batch = _geomBatches[batchIdx];
                        for (int i = batch.Count - 1; i >= 0; i--)
                        {
                            var r = batch[i];
                            if (r == null || !r)
                            {
                                batch.RemoveAt(i); // Clean dead refs
                                continue;
                            }
                            if (r.gameObject.layer != tempLayer)
                            {
                                _layerRestoreCache[r.gameObject] = r.gameObject.layer;
                                r.gameObject.layer = tempLayer;
                            }
                        }

                        if (sunAboveHorizon && !Profiler.ShouldSkip("SunDepth"))
                        {
                            shadowCamera.clearFlags = CameraClearFlags.Nothing; // already cleared on batch 0
                            var originalShadowMask = shadowCamera.cullingMask;
                            shadowCamera.cullingMask = 1 << tempLayer;
                            shadowCamera.SetTargetBuffers(sunDepthTextureBack.colorBuffer, sunDepthTextureBack.depthBuffer);
                            var prevLodBias = QualitySettings.lodBias;
                            QualitySettings.lodBias = float.MaxValue;
                            shadowCamera.RenderWithShader(sunDepthShader, "");
                            QualitySettings.lodBias = prevLodBias;
                            shadowCamera.cullingMask = originalShadowMask;
                            shadowCamera.clearFlags = CameraClearFlags.SolidColor;
                        }

                        if (!Profiler.ShouldSkip("GeomVoxelize"))
                        {
                            var originalMask = voxelCamera.cullingMask;
                            voxelCamera.cullingMask = 1 << tempLayer;

                            Shader.SetGlobalInt("SEGIStripSun", 1);
                            Shader.SetGlobalFloat("SEGIAlphaScale", 1.0f);
                            Shader.SetGlobalInt("SEGISkipInnerOcclusion", 0);
                            SetIntVolume4RandomWrite(geomCacheCopy4);

                            voxelCamera.targetTexture = dummyVoxelTextureAAScaled;
                            voxelCamera.RenderWithShader(voxelizationShaderBeefEdit, "");
                            Graphics.ClearRandomWriteTargets();

                            voxelCamera.cullingMask = originalMask;
                        }

                        foreach (var kvp in _layerRestoreCache)
                        {
                            if (kvp.Key != null)
                                kvp.Key.layer = kvp.Value;
                        }

                        PositionVoxelCameras(voxelSpaceOrigin);
                    }

                    if (batchIdx == _geomBatchCount && _terrainBatch.Count > 0)
                    {
                        PositionVoxelCameras(_buildTargetOrigin);
                        const int tempLayer = 31;
                        _layerRestoreCache.Clear();
                        for (int i = _terrainBatch.Count - 1; i >= 0; i--)
                        {
                            var r = _terrainBatch[i];
                            if (r == null || !r)
                            {
                                _terrainBatch.RemoveAt(i);
                                continue;
                            }
                            if (r.gameObject.layer != tempLayer)
                            {
                                _layerRestoreCache[r.gameObject] = r.gameObject.layer;
                                r.gameObject.layer = tempLayer;
                            }
                        }

                        if (sunAboveHorizon && !Profiler.ShouldSkip("SunDepth"))
                        {
                            shadowCamera.clearFlags = CameraClearFlags.Nothing;
                            var originalShadowMask = shadowCamera.cullingMask;
                            shadowCamera.cullingMask = 1 << tempLayer;
                            shadowCamera.SetTargetBuffers(sunDepthTextureBack.colorBuffer, sunDepthTextureBack.depthBuffer);
                            var prevLodBias = QualitySettings.lodBias;
                            QualitySettings.lodBias = float.MaxValue;
                            shadowCamera.RenderWithShader(sunDepthShader, "");
                            QualitySettings.lodBias = prevLodBias;
                            shadowCamera.cullingMask = originalShadowMask;
                            shadowCamera.clearFlags = CameraClearFlags.SolidColor;
                        }

                        if (!Profiler.ShouldSkip("GeomVoxelize"))
                        {
                            var originalMask = voxelCamera.cullingMask;
                            voxelCamera.cullingMask = 1 << tempLayer;

                            Shader.SetGlobalInt("SEGIStripSun", 1);
                            Shader.SetGlobalFloat("SEGIAlphaScale", TerrainAlphaScale);
                            Shader.SetGlobalInt("SEGISkipInnerOcclusion", 1);
                            SetIntVolume4RandomWrite(geomCacheCopy4);

                            voxelCamera.targetTexture = dummyVoxelTextureAAScaled;
                            voxelCamera.RenderWithShader(voxelizationShaderBeefEdit, "");
                            Graphics.ClearRandomWriteTargets();

                            Shader.SetGlobalFloat("SEGIAlphaScale", 1.0f);
                            Shader.SetGlobalInt("SEGISkipInnerOcclusion", 0);
                            voxelCamera.cullingMask = originalMask;
                        }

                        foreach (var kvp in _layerRestoreCache)
                        {
                            if (kvp.Key != null)
                                kvp.Key.layer = kvp.Value;
                        }

                        PositionVoxelCameras(voxelSpaceOrigin);
                    }

                    if (batchIdx == _geomBatchCount + 1 && sunAboveHorizon && !Profiler.ShouldSkip("SunDepth"))
                    {
                        PositionVoxelCameras(_buildTargetOrigin);

                        var emissiveSunRenderers = GetEmissiveRenderers();
                        if (emissiveSunRenderers.Count > 0)
                        {
                            const int tempLayer = 31;
                            _layerRestoreCache.Clear();
                            foreach (var r in emissiveSunRenderers)
                            {
                                if (r != null && r.gameObject.layer != tempLayer)
                                {
                                    _layerRestoreCache[r.gameObject] = r.gameObject.layer;
                                    r.gameObject.layer = tempLayer;
                                }
                            }

                            var originalShadowMask = shadowCamera.cullingMask;
                            shadowCamera.cullingMask = 1 << tempLayer;
                            shadowCamera.clearFlags = CameraClearFlags.Nothing;
                            shadowCamera.SetTargetBuffers(sunDepthTextureBack.colorBuffer, sunDepthTextureBack.depthBuffer);
                            var prevLodBias = QualitySettings.lodBias;
                            QualitySettings.lodBias = float.MaxValue;
                            shadowCamera.RenderWithShader(sunDepthShader, "");
                            QualitySettings.lodBias = prevLodBias;
                            shadowCamera.cullingMask = originalShadowMask;
                            shadowCamera.clearFlags = CameraClearFlags.SolidColor;

                            foreach (var kvp in _layerRestoreCache)
                            {
                                if (kvp.Key != null)
                                    kvp.Key.layer = kvp.Value;
                            }
                        }

                        PositionVoxelCameras(voxelSpaceOrigin);
                    }

                    _currentBuildBatch++;

                    if (_currentBuildBatch >= BuildCycleLength)
                    {
                        voxelSpaceOrigin = _buildTargetOrigin;
                        previousVoxelSpaceOrigin = voxelSpaceOrigin;
                        PositionVoxelCameras(voxelSpaceOrigin);

                        for (int c = 0; c < 4; c++)
                            (geomCacheVolume4[c], geomCacheCopy4[c]) = (geomCacheCopy4[c], geomCacheVolume4[c]);

                        _geomCacheWrapOffset = new int3Offset(0, 0, 0); // Fresh build is physically aligned
                        _hybridCacheReady = true;

                        (sunDepthTexture, sunDepthTextureBack) = (sunDepthTextureBack, sunDepthTexture);
                        Shader.SetGlobalTexture("SEGISunDepth", sunDepthTexture);
                        _lastSunDirection = currentSunDirection;
                        _sunShadowOrigin = voxelSpaceOrigin;

                        if (!Profiler.ShouldSkip("SunBake"))
                            RunSunBake();
                        _buildCycleActive = false;
                        _currentBuildBatch = 0;
                    }
                }

                Shader.SetGlobalInt("SEGIStripSun", 0);
                Shader.SetGlobalFloat("SEGIAlphaScale", 1.0f);
                Shader.SetGlobalInt("SEGISkipInnerOcclusion", 0);
                UpdateEmissiveCache();

                if (!Profiler.ShouldSkip("ClearInts"))
                    ClearIntVolume4(integerVolume4, adaptiveVoxelResolution);

                var emissiveRenderersToUse = GetEmissiveRenderers();
                if (emissiveRenderersToUse.Count > 0)
                {
                    const int tempLayer = 31;
                    _layerRestoreCache.Clear();
                    foreach (var r in emissiveRenderersToUse)
                    {
                        if (r != null && r.gameObject.layer != tempLayer)
                        {
                            _layerRestoreCache[r.gameObject] = r.gameObject.layer;
                            r.gameObject.layer = tempLayer;
                        }
                    }

                    var originalMask = voxelCamera.cullingMask;
                    voxelCamera.cullingMask = 1 << tempLayer;

                    if (!Profiler.ShouldSkip("EmissiveVoxelize"))
                    {
                        SetIntVolume4RandomWrite(integerVolume4);
                        voxelCamera.targetTexture = dummyVoxelTextureAAScaled;
                        voxelCamera.RenderWithShader(voxelizationShaderBeefEdit, "");
                        Graphics.ClearRandomWriteTargets();
                    }

                    voxelCamera.cullingMask = originalMask;
                    foreach (var kvp in _layerRestoreCache)
                    {
                        if (kvp.Key != null)
                            kvp.Key.layer = kvp.Value;
                    }
                }

                if (!Profiler.ShouldSkip("MergeVolumes"))
                {
                    int mergeGroups = Mathf.CeilToInt((float)adaptiveVoxelResolution / 4f);
                    mergeVolumesCompute.SetTexture(0, "Result", activeVolume);
                    SetIntVolume4OnCompute(mergeVolumesCompute, 0, "GeometryCache", geomCacheVolume4);
                    SetIntVolume4OnCompute(mergeVolumesCompute, 0, "EmissiveVolume", integerVolume4);
                    mergeVolumesCompute.SetInt("Resolution", adaptiveVoxelResolution);
                    mergeVolumesCompute.SetInt("VoxelAA", adaptiveVoxelAA ? 1 : 0);
                    mergeVolumesCompute.SetInts("GeomCacheWrapOffset",
                        _geomCacheWrapOffset.x, _geomCacheWrapOffset.y, _geomCacheWrapOffset.z);
                    if (_combinedVolume != null)
                        mergeVolumesCompute.SetTexture(0, "PrevVolume", _combinedVolume);
                    mergeVolumesCompute.SetFloat("SecondaryBounceGain", ConfigData.SecondaryBounceGain);
                    Vector3 bounceScrollDelta = (voxelSpaceOrigin - _combinedVolumeOrigin) / adaptiveVoxelSpaceSize;
                    mergeVolumesCompute.SetVector("BounceScrollOffset", bounceScrollDelta);
                    float emissiveTemporalBlend = 0.6f;
#if SEGI_PROFILER
                    emissiveTemporalBlend = DebugOverrides.EmissiveTemporalBlend ?? emissiveTemporalBlend;
#endif
                    mergeVolumesCompute.SetFloat("EmissiveTemporalBlend", emissiveTemporalBlend);

                    mergeVolumesCompute.Dispatch(0, mergeGroups, mergeGroups, mergeGroups);
                }
            }

            //Manually filter/render mip maps
            if (!Profiler.ShouldSkip("MipChain"))
            {
                if (_combinedVolume == null || !_combinedVolume.IsCreated())
                    CreateCombinedVolume(adaptiveVoxelResolution);
                mipFilterCompute.SetInt("destinationRes", adaptiveVoxelResolution);
                mipFilterCompute.SetTexture(0, "Source", activeVolume);
                mipFilterCompute.SetTexture(0, "Destination", _combinedVolume, 0);
                mipFilterCompute.SetTexture(0, "CombinedMip", _combinedVolume, 0);
                int mip0Groups = Mathf.CeilToInt(adaptiveVoxelResolution / 4f);
                mipFilterCompute.Dispatch(0, mip0Groups, mip0Groups, mip0Groups);
                for (var i = 0; i < mipLevels - 1; i++)
                {
                    var source = (i == 0) ? activeVolume : volumeTextures[i];
                    var destinationRes = adaptiveVoxelResolution / (1 << (i + 1));

                    mipFilterCompute.SetInt("destinationRes", destinationRes);
                    mipFilterCompute.SetTexture(MipFilterKernel, "Source", source);
                    mipFilterCompute.SetTexture(MipFilterKernel, "Destination", volumeTextures[i + 1]);
                    mipFilterCompute.SetTexture(MipFilterKernel, "CombinedMip", _combinedVolume, i + 1);

                    var mipThreadGroups = Mathf.CeilToInt((float)destinationRes / 4.0f);
                    mipFilterCompute.Dispatch(MipFilterKernel, mipThreadGroups, mipThreadGroups, mipThreadGroups);
                }
            }
            if (_combinedVolume != null)
            {
                Shader.SetGlobalTexture("SEGIVolume", _combinedVolume);
                Shader.SetGlobalTexture("SEGIVolumeTexture1", _combinedVolume);
                _combinedVolumeOrigin = voxelSpaceOrigin;
            }

            //Advance the voxel flip flop counter
            {
                voxelFlipFlop += 1;
                voxelFlipFlop %= 2;
            }


        RenderTexture.active = previousActive;
    }

    private bool _wasBypassed = false;

    [ImageEffectOpaque]
    private void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        if (bypassRendering)
        {
            Graphics.Blit(source, destination);
            _wasBypassed = true;
            Profiler.EndFrame();
            return;
        }

        // Coming back from bypass: clear previousGIResult so temporal blend
        // doesn't start from stale/black data causing a black screen
        if (_wasBypassed)
        {
            _wasBypassed = false;
            if (previousGIResult != null)
            {
                RenderTexture prev = RenderTexture.active;
                RenderTexture.active = previousGIResult;
                GL.Clear(true, true, Color.clear);
                if (_previousGIResultBack != null)
                {
                    RenderTexture.active = _previousGIResultBack;
                    GL.Clear(true, true, Color.clear);
                }
                RenderTexture.active = prev;
            }
            // Force all cached shader params to refresh
            _cachedGIGain = -999f;
            _cachedEmissiveLightGain = -999f;
            _cachedEmissiveBubbleParams = new Vector4(0, 0, 0, -999f);
            _cachedSecondaryBounceGain = -999f;
            _cachedTraceLength = -999f;
            _cachedConeWidth = -999f;
            _cachedOcclusionStrength = -999f;
            _cachedOcclusionPower = -999f;
            _cachedConeTraceBias = -999f;
            _cachedNearOcclusionStrength = -999f;
            _cachedFarOcclusionStrength = -999f;
            _cachedFarthestOcclusionStrength = -999f;
            _cachedInnerOcclusionLayers = -999;
            _cachedAdaptiveCones = -999;
            _cachedAdaptiveConeTraceSteps = -999;
            _cachedAdaptiveHalfRes = -999;
        }

        if (notReadyToRender || material == null)
        {
            Graphics.Blit(source, destination);
            Profiler.EndFrame();
            return;
        }

        if (Profiler.ShouldSkip("SEGI_All"))
        {
            Graphics.Blit(source, destination);
            Profiler.EndFrame();
            return;
        }

        if (_persistGI1 == null || _persistGI2 == null || _persistDepth == null || _persistNormal == null)
        {
            ResizePostProcessRTs();
            if (_persistGI1 == null)
            {
                Graphics.Blit(source, destination);
                Profiler.EndFrame();
                return;
            }
        }

        //Set parameters
        int giRenderRes = GIRenderRes;
        Shader.SetGlobalInt("SEGIFrameSwitch", frameCounter);
        Shader.SetGlobalFloat("SEGIVoxelScaleFactor", VoxelScaleFactor);
        material.SetMatrix("CameraToWorld", attachedCamera.cameraToWorldMatrix);
        material.SetMatrix("WorldToCamera", attachedCamera.worldToCameraMatrix);
        material.SetMatrix("ProjectionMatrixInverse", attachedCamera.projectionMatrix.inverse);
        material.SetMatrix("ProjectionMatrix", attachedCamera.projectionMatrix);
        material.SetInt("FrameSwitch", frameCounter);
        material.SetVector("CameraPosition", transform.position);
        material.SetTexture("NoiseTexture", blueNoise[frameCounter % 64]);
        { var v = ConfigData.ConeLength;
#if SEGI_PROFILER
        v = DebugOverrides.ConeLength ?? v;
#endif
        if (!Mathf.Approximately(v, _cachedTraceLength))
        { material.SetFloat("TraceLength", v); _cachedTraceLength = v; } }

        { var v = ConfigData.ConeWidth;
#if SEGI_PROFILER
        v = DebugOverrides.ConeWidth ?? v;
#endif
        if (!Mathf.Approximately(v, _cachedConeWidth))
        { material.SetFloat("ConeSize", v); _cachedConeWidth = v; } }

        { var v = ConfigData.OcclusionStrength;
#if SEGI_PROFILER
        v = DebugOverrides.OcclusionStrength ?? v;
#endif
        if (!Mathf.Approximately(v, _cachedOcclusionStrength))
        { material.SetFloat("OcclusionStrength", v); _cachedOcclusionStrength = v; } }

        { var v = ConfigData.OcclusionPower;
        if (!Mathf.Approximately(v, _cachedOcclusionPower))
        { material.SetFloat("OcclusionPower", v); _cachedOcclusionPower = v; } }

        { var v = ConfigData.ConeTraceBias;
#if SEGI_PROFILER
        v = DebugOverrides.ConeTraceBias ?? v;
#endif
        if (!Mathf.Approximately(v, _cachedConeTraceBias))
        { material.SetFloat("ConeTraceBias", v); _cachedConeTraceBias = v; } }

        { float edgeFadeWidth = 0.1f; // outer 10% per side — smoothstep + square in shader
        material.SetFloat("EdgeFadeWidth", edgeFadeWidth); }

        { var v = ConfigData.GIGain;
        if (!Mathf.Approximately(v, _cachedGIGain))
        { material.SetFloat("GIGain", v); _cachedGIGain = v; } }

        { var v = ConfigData.NearOcclusionStrength;
#if SEGI_PROFILER
        v = DebugOverrides.NearOcclusionStrength ?? v;
#endif
        if (!Mathf.Approximately(v, _cachedNearOcclusionStrength))
        { material.SetFloat("NearOcclusionStrength", v); _cachedNearOcclusionStrength = v; } }

        { var v = ConfigData.FarOcclusionStrength;
        if (!Mathf.Approximately(v, _cachedFarOcclusionStrength))
        { material.SetFloat("FarOcclusionStrength", v); _cachedFarOcclusionStrength = v; } }

        { var v = ConfigData.FarthestOcclusionStrength;
        if (!Mathf.Approximately(v, _cachedFarthestOcclusionStrength))
        { material.SetFloat("FarthestOcclusionStrength", v); _cachedFarthestOcclusionStrength = v; } }

        { var v = adaptiveCones;
        if (v != _cachedAdaptiveCones)
        { material.SetInt("TraceDirections", v); _cachedAdaptiveCones = v; } }

        { var v = adaptiveConeTraceSteps;
        if (v != _cachedAdaptiveConeTraceSteps)
        { material.SetInt("TraceSteps", v); _cachedAdaptiveConeTraceSteps = v; } }

        { var v = adaptiveHalfResolution ? 1 : 0;
        if (v != _cachedAdaptiveHalfRes)
        { material.SetInt("HalfResolution", v); _cachedAdaptiveHalfRes = v; } }

        //If Visualize Voxels is enabled, just render the voxel visualization shader pass and return
        if (visualizeVoxels)
        {
            Graphics.Blit(source, destination, material, Pass.VisualizeVoxels);
            Profiler.EndFrame();
            return;
        }

        //Setup textures
        var gi1 = _persistGI1;
        var gi2 = _persistGI2;

        //Setup textures to hold the current camera depth and normal
        var currentDepth = _persistDepth;
        var currentNormal = _persistNormal;

        //Get the camera depth and normals
        if (!Profiler.ShouldSkip("GBufferSetup"))
        {
            Graphics.Blit(source, currentDepth, material, Pass.GetCameraDepthTexture);
            Graphics.Blit(source, currentNormal, material, Pass.GetWorldNormals);
        }
        material.SetTexture("CurrentDepth", currentDepth);
        material.SetTexture("CurrentNormal", currentNormal);

        //Render diffuse GI tracing result
        if (!Profiler.ShouldSkip("DiffuseTrace"))
        {
            if (coneTraceCompute == null)
            {
                Graphics.Blit(source, gi2, material, Pass.DiffuseTrace);
            }
            else
            {
                var gBufferNormals = Shader.GetGlobalTexture("_CameraGBufferTexture2");
                if (gBufferNormals == null)
                {
                    Graphics.Blit(source, gi2, material, Pass.DiffuseTrace);
                }
                else
                {
                    int probeSpacing = ProbeSpacing;
                    int traceW = gi2.width;
                    int traceH = gi2.height;
                    if (giRenderRes == 2)
                        probeSpacing = Mathf.Max(2, probeSpacing / 2);

                    int probeW = Mathf.CeilToInt((float)traceW / probeSpacing);
                    int probeH = Mathf.CeilToInt((float)traceH / probeSpacing);

                    if (probeIrradiance == null || probeIrradiance.width != probeW || probeIrradiance.height != probeH)
                    {
                        if (probeIrradiance != null) CleanupTexture(ref probeIrradiance);
                        probeIrradiance = new RenderTexture(probeW, probeH, 0, RenderTextureFormat.ARGBHalf)
                        {
                            enableRandomWrite = true,
                            filterMode = FilterMode.Point,
                            hideFlags = HideFlags.HideAndDontSave
                        };
                        probeIrradiance.Create();
                    }

                    SetComputeTraceUniforms(_probeTraceKernel);
                    coneTraceCompute.SetTexture(_probeTraceKernel, "ProbeIrradiance", probeIrradiance);
                    coneTraceCompute.SetTextureFromGlobal(_probeTraceKernel, "DepthTexture", "_CameraDepthTexture");
                    coneTraceCompute.SetTexture(_probeTraceKernel, "GBufferNormals", gBufferNormals);
                    coneTraceCompute.SetTexture(_probeTraceKernel, "NoiseTexture", blueNoise[frameCounter % 64]);
                    coneTraceCompute.SetInt("FrameCounter", frameCounter);
                    BindVolumeTexturesToCompute(_probeTraceKernel);
                    coneTraceCompute.SetMatrix("CameraToWorld", attachedCamera.cameraToWorldMatrix);
                    coneTraceCompute.SetMatrix("ProjectionMatrixInverse", attachedCamera.projectionMatrix.inverse);
                    coneTraceCompute.SetInts("Resolution", traceW, traceH);
                    coneTraceCompute.SetInts("ProbeGridSize", probeW, probeH);
                    coneTraceCompute.SetInt("ProbeSpacing", probeSpacing);
                    coneTraceCompute.SetInt("HalfResolution", giRenderRes == 2 ? 1 : 0);

                    // Dispatch probe trace
                    coneTraceCompute.Dispatch(_probeTraceKernel,
                        Mathf.CeilToInt(probeW / 8f),
                        Mathf.CeilToInt(probeH / 8f), 1);

                    SetComputeTraceUniforms(_probeInterpolateKernel);
                    coneTraceCompute.SetTexture(_probeInterpolateKernel, "ProbeIrradianceIn", probeIrradiance);
                    coneTraceCompute.SetTexture(_probeInterpolateKernel, "Result", gi2);
                    coneTraceCompute.SetTextureFromGlobal(_probeInterpolateKernel, "DepthTexture",
                        "_CameraDepthTexture");
                    coneTraceCompute.SetTexture(_probeInterpolateKernel, "GBufferNormals", gBufferNormals);
                    coneTraceCompute.SetInts("Resolution", traceW, traceH);
                    coneTraceCompute.SetInts("ProbeGridSize", probeW, probeH);
                    coneTraceCompute.SetInt("ProbeSpacing", probeSpacing);

                    if (!Profiler.ShouldSkip("ProbeInterpolate"))
                    {
                        coneTraceCompute.Dispatch(_probeInterpolateKernel,
                            Mathf.CeilToInt(traceW / 8f),
                            Mathf.CeilToInt(traceH / 8f), 1);
                    }

                }
            }
        }
        else
            Graphics.Blit(source, gi2);

        RenderTexture filtered;
        if (!Profiler.ShouldSkip("BilateralBlur"))
        {
            int giW = source.width / giRenderRes;
            int giH = source.height / giRenderRes;
            filtered = DispatchATrousFilter(gi2, gi1, giW, giH);
        }
        else { filtered = gi2; }

        //If Half Resolution tracing is enabled
        if (giRenderRes == 2)
        {
            if (_persistGI3 == null || _persistGI4 == null)
            {
                ResizePostProcessRTs();
                if (_persistGI3 == null || _persistGI4 == null)
                {
                    Graphics.Blit(source, destination);
                    Profiler.EndFrame();
                    return;
                }
            }
            var gi3 = _persistGI3;
            var gi4 = _persistGI4;
            filtered.filterMode = FilterMode.Point;
            Graphics.Blit(filtered, gi4);

            //Perform bilateral upsampling on half-resolution diffuse GI result
            if (!Profiler.ShouldSkip("BilateralUpsample"))
            {
                material.SetVector("Kernel", new Vector2(1.0f, 0.0f));
                Graphics.Blit(gi4, gi3, material, Pass.BilateralUpsample);
                material.SetVector("Kernel", new Vector2(0.0f, 1.0f));
            }
            else
            {
                Graphics.Blit(gi4, gi3);
            }

            //Perform temporal reprojection and blending
            if (ConfigData.TemporalBlendWeight < 1.0f && !Profiler.ShouldSkip("TemporalBlend"))
            {
                Graphics.Blit(gi3, gi4);
                DispatchTemporalBlend(gi4, gi3);
            }

            //Set the result to be accessed in the shader
            material.SetTexture("GITexture", gi3);

            //Actually apply the GI to the scene using gbuffer data
            if (!Profiler.ShouldSkip("FinalComposite"))
                Graphics.Blit(source, destination, material, visualizeGI ? Pass.VisualizeGI : Pass.BlendWithScene);
            else
                Graphics.Blit(source, destination);
        }
        else //If Half Resolution tracing is disabled
        {
            RenderTexture temporalSrc = filtered;
            RenderTexture temporalDst = (filtered == gi1) ? gi2 : gi1;

            //Perform temporal reprojection and blending
            if (ConfigData.TemporalBlendWeight < 1.0f && !Profiler.ShouldSkip("TemporalBlend"))
            {
                DispatchTemporalBlend(temporalSrc, temporalDst);
            }

            //Actually apply the GI to the scene using gbuffer data
            material.SetTexture("GITexture", ConfigData.TemporalBlendWeight < 1.0f ? temporalDst : filtered);

            if (!Profiler.ShouldSkip("FinalComposite"))
                Graphics.Blit(source, destination, material, visualizeGI ? Pass.VisualizeGI : Pass.BlendWithScene);
            else
                Graphics.Blit(source, destination);
        }

        //Visualize the sun depth texture
        bool showSunDepth = visualizeSunDepthTexture;
        if (showSunDepth) Graphics.Blit(sunDepthTexture, destination);

        Profiler.EndFrame();

        _prevProjectionInverse = attachedCamera.projectionMatrix.inverse;
        _prevCameraToWorld = attachedCamera.cameraToWorldMatrix;
        Vector3 curPos = attachedCamera.transform.position;
        float dt = Time.deltaTime > 0 ? Time.deltaTime : 1f / 60f;
        float instantSpeed = Vector3.Distance(curPos, _prevCameraPosition) / dt;
        _smoothedCameraSpeed = Mathf.Lerp(_smoothedCameraSpeed, instantSpeed, 0.33f);
        _prevCameraPosition = curPos;

        //Advance the frame counter
        frameCounter = (frameCounter + 1) % 64;
    }

    private void SetComputeTraceUniforms(int kernel)
    {
        if (coneTraceCompute == null) return;
        coneTraceCompute.SetFloat("SEGIVoxelScaleFactor", VoxelScaleFactor);
        coneTraceCompute.SetInt("StochasticSampling", 1);
        coneTraceCompute.SetInt("TraceDirections", adaptiveCones);
        coneTraceCompute.SetInt("TraceSteps", adaptiveConeTraceSteps);

        float traceLen = ConfigData.ConeLength;
        float coneWidth = ConfigData.ConeWidth;
        float occStr = ConfigData.OcclusionStrength;
        float nearOccStr = ConfigData.NearOcclusionStrength;
        float giGain = ConfigData.GIGain;
        float coneTraceBias = ConfigData.ConeTraceBias;
#if SEGI_PROFILER
        traceLen = DebugOverrides.ConeLength ?? traceLen;
        coneWidth = DebugOverrides.ConeWidth ?? coneWidth;
        occStr = DebugOverrides.OcclusionStrength ?? occStr;
        nearOccStr = DebugOverrides.NearOcclusionStrength ?? nearOccStr;
        coneTraceBias = DebugOverrides.ConeTraceBias ?? coneTraceBias;
#endif
        coneTraceCompute.SetFloat("TraceLength", traceLen);
        coneTraceCompute.SetFloat("ConeSize", coneWidth);
        coneTraceCompute.SetFloat("OcclusionStrength", occStr);
        coneTraceCompute.SetFloat("OcclusionPower", ConfigData.OcclusionPower);
        coneTraceCompute.SetFloat("ConeTraceBias", coneTraceBias);
        coneTraceCompute.SetFloat("GIGain", giGain);
        coneTraceCompute.SetFloat("NearOcclusionStrength", nearOccStr);
        coneTraceCompute.SetFloat("FarOcclusionStrength", ConfigData.FarOcclusionStrength);
        coneTraceCompute.SetFloat("FarthestOcclusionStrength", ConfigData.FarthestOcclusionStrength);
        coneTraceCompute.SetFloat("EdgeFadeWidth", 0.1f);
        coneTraceCompute.SetFloat("SEGISoftSunlight", 0);
        coneTraceCompute.SetInt("SEGISphericalSkylight", 0);
    }

    private void BindVolumeTexturesToCompute(int kernel)
    {
        if (coneTraceCompute == null || _combinedVolume == null) return;
        coneTraceCompute.SetTexture(kernel, "SEGIVolume", _combinedVolume);
    }

    private void OnDrawGizmosSelected()
    {
        if (!enabled) return;

        var prevColor = Gizmos.color;
        Gizmos.color = new Color(1.0f, 0.25f, 0.0f, 0.5f);
        Gizmos.DrawCube(voxelSpaceOrigin,
            new Vector3(adaptiveVoxelSpaceSize, adaptiveVoxelSpaceSize, adaptiveVoxelSpaceSize));
        Gizmos.color = new Color(1.0f, 0.0f, 0.0f, 0.1f);
        Gizmos.color = prevColor;
    }

    private const float ATrous_SigmaDepth  = 1.0f;
    private const float ATrous_SigmaNormal = 32.0f;
    private const float ATrous_SigmaLuma   = 4.0f;
    private static readonly int[] ATrous_StepSizes = { 1, 2, 4 };

    private void DispatchTemporalBlend(RenderTexture currentGI, RenderTexture outputGI)
    {
        if (temporalBlendCompute == null) return;

        int width = outputGI.width;
        int height = outputGI.height;
        temporalBlendCompute.SetTexture(_temporalBlendKernel, "_CurrentGI", currentGI);
        temporalBlendCompute.SetTexture(_temporalBlendKernel, "_PreviousGI", previousGIResult);
        temporalBlendCompute.SetTextureFromGlobal(_temporalBlendKernel, "_CurrentDepth", "_CameraDepthTexture");
        temporalBlendCompute.SetTexture(_temporalBlendKernel, "_PreviousDepth", previousCameraDepth);
        temporalBlendCompute.SetTextureFromGlobal(_temporalBlendKernel, "_MotionVectors", "_CameraMotionVectorsTexture");
        temporalBlendCompute.SetTexture(_temporalBlendKernel, "_OutputGI", outputGI);
        temporalBlendCompute.SetTexture(_temporalBlendKernel, "_HistoryGI", _previousGIResultBack);
        temporalBlendCompute.SetTexture(_temporalBlendKernel, "_HistoryDepth", _previousCameraDepthBack);
        temporalBlendCompute.SetMatrix("ProjectionMatrixInverse", attachedCamera.projectionMatrix.inverse);
        temporalBlendCompute.SetMatrix("CameraToWorld", attachedCamera.cameraToWorldMatrix);
        temporalBlendCompute.SetMatrix("ProjectionPrevInverse", _prevProjectionInverse);
        temporalBlendCompute.SetMatrix("CameraToWorldPrev", _prevCameraToWorld);
        temporalBlendCompute.SetFloat("_CameraTranslationSpeed", _smoothedCameraSpeed);
        temporalBlendCompute.SetVector("_ScreenParams", new Vector4(width, height, 1f / width, 1f / height));
        temporalBlendCompute.SetVector("_MainTex_TexelSize", new Vector4(1f / width, 1f / height, width, height));
        temporalBlendCompute.SetVector("_ZBufferParams", Shader.GetGlobalVector("_ZBufferParams"));

        float blendWeight = ConfigData.TemporalBlendWeight;
#if SEGI_PROFILER
        blendWeight = DebugOverrides.TemporalBlendWeight ?? blendWeight;
#endif
        temporalBlendCompute.SetFloat("BlendWeight", blendWeight);

        float disoccSens = 5.0f;
        float motionMax = 0.25f;
#if SEGI_PROFILER
        disoccSens = DebugOverrides.DisocclusionSensitivity ?? disoccSens;
        motionMax = DebugOverrides.MotionBlendMax ?? motionMax;
#endif
        temporalBlendCompute.SetFloat("_DisocclusionSensitivity", disoccSens);
        temporalBlendCompute.SetFloat("_MotionBlendMax", motionMax);

        int groupsX = Mathf.CeilToInt(width / 8f);
        int groupsY = Mathf.CeilToInt(height / 8f);
        temporalBlendCompute.Dispatch(_temporalBlendKernel, groupsX, groupsY, 1);
        (previousGIResult, _previousGIResultBack) = (_previousGIResultBack, previousGIResult);
        (previousCameraDepth, _previousCameraDepthBack) = (_previousCameraDepthBack, previousCameraDepth);
    }

    private RenderTexture DispatchATrousFilter(
        RenderTexture input, RenderTexture scratch,
        int giWidth, int giHeight)
    {
        if (atrousFilterCompute == null) return input;

        var atrousGBufferNormals = Shader.GetGlobalTexture("_CameraGBufferTexture2");
        if (atrousGBufferNormals == null) return input;

        RenderTexture src = input;
        RenderTexture dst = scratch;

        atrousFilterCompute.SetVector("_ScreenSize",
            new Vector4(giWidth, giHeight, 1f / giWidth, 1f / giHeight));

        float sigmaDepth  = ATrous_SigmaDepth;
        float sigmaNormal = ATrous_SigmaNormal;
        float sigmaLuma   = ATrous_SigmaLuma;
        int   passCount   = ATrous_StepSizes.Length;

        atrousFilterCompute.SetFloat("_SigmaDepth", sigmaDepth);
        atrousFilterCompute.SetFloat("_SigmaNormal", sigmaNormal);
        atrousFilterCompute.SetFloat("_SigmaLuma", sigmaLuma);
        atrousFilterCompute.SetVector("_ZBufferParams", Shader.GetGlobalVector("_ZBufferParams"));

        atrousFilterCompute.SetTextureFromGlobal(_atrousKernel, "_DepthTexture", "_CameraDepthTexture");
        atrousFilterCompute.SetTexture(_atrousKernel, "_NormalTexture", atrousGBufferNormals);

        int groupsX = Mathf.CeilToInt(giWidth / 8f);
        int groupsY = Mathf.CeilToInt(giHeight / 8f);

        passCount = Mathf.Clamp(passCount, 1, ATrous_StepSizes.Length);
        for (int pass = 0; pass < passCount; pass++)
        {
            atrousFilterCompute.SetInt("_StepSize", ATrous_StepSizes[pass]);
            atrousFilterCompute.SetTexture(_atrousKernel, "_InputGI", src);
            atrousFilterCompute.SetTexture(_atrousKernel, "_OutputGI", dst);
            atrousFilterCompute.Dispatch(_atrousKernel, groupsX, groupsY, 1);
            (src, dst) = (dst, src);
        }
        return src;
    }

    private void PositionVoxelCameras(Vector3 origin)
    {
        voxelCameraGameObject.transform.position = origin - Vector3.forward * adaptiveVoxelSpaceSize * 0.5f;
        voxelCameraGameObject.transform.rotation = rotationFront;
        leftViewPoint.transform.position = origin + Vector3.left * adaptiveVoxelSpaceSize * 0.5f;
        leftViewPoint.transform.rotation = rotationLeft;
        topViewPoint.transform.position = origin + Vector3.up * adaptiveVoxelSpaceSize * 0.5f;
        topViewPoint.transform.rotation = rotationTop;

        Shader.SetGlobalMatrix("SEGIVoxelViewFront",
            TransformViewMatrix(voxelCamera.transform.worldToLocalMatrix));
        Shader.SetGlobalMatrix("SEGIVoxelViewLeft",
            TransformViewMatrix(leftViewPoint.transform.worldToLocalMatrix));
        Shader.SetGlobalMatrix("SEGIVoxelViewTop",
            TransformViewMatrix(topViewPoint.transform.worldToLocalMatrix));
        Shader.SetGlobalMatrix("SEGIWorldToVoxel", voxelCamera.worldToCameraMatrix);

        var sunDir = sun != null ? -sun.transform.forward : Vector3.down;
        float dist = adaptiveShadowSpaceSize * 0.5f * shadowSpaceDepthRatio;
        shadowCameraTransform.position = origin + Vector3.Normalize(sunDir) * dist;
        shadowCameraTransform.LookAt(origin, Vector3.up);

        var voxelToGIProjection = shadowCamera.projectionMatrix * shadowCamera.worldToCameraMatrix *
                                  voxelCamera.cameraToWorldMatrix;
        Shader.SetGlobalMatrix("SEGIVoxelToGIProjection", voxelToGIProjection);
    }

    private void RunSunBake()
    {
        if (sunBakeCompute == null) return;

        for (int c = 0; c < 3; c++)
            sunBakeCompute.SetTexture(_sunBakeKernel, "GeomCache" + ChannelSuffixes[c], geomCacheVolume4[c]);
        sunBakeCompute.SetTexture(_sunBakeKernel, "GeomCache_A", geomCacheVolume4[3]);
        sunBakeCompute.SetTexture(_sunBakeKernel, "SEGISunDepth", sunDepthTexture);

        var voxProjInv = voxelCamera.projectionMatrix.inverse;
        var giProj = shadowCamera.projectionMatrix * shadowCamera.worldToCameraMatrix * voxelCamera.cameraToWorldMatrix;

        sunBakeCompute.SetMatrix("SEGIVoxelProjectionInverse", voxProjInv);
        sunBakeCompute.SetMatrix("SEGIVoxelToGIProjection", giProj);
        sunBakeCompute.SetVector("SEGISunlightVector", Shader.GetGlobalVector("SEGISunlightVector"));
        sunBakeCompute.SetVector("GISunColor", Shader.GetGlobalColor("GISunColor"));
        sunBakeCompute.SetInts("GeomCacheWrapOffset",
            _geomCacheWrapOffset.x, _geomCacheWrapOffset.y, _geomCacheWrapOffset.z);
        sunBakeCompute.SetInt("Resolution", adaptiveVoxelResolution);

        sunBakeCompute.SetFloat("SunShadowSoftness", 150.0f);
#if SEGI_PROFILER
        sunBakeCompute.SetFloat("SunShadowSoftness", DebugOverrides.SunShadowSoftness ?? 150.0f);
#endif
        sunBakeCompute.SetFloat("SunBakeShadowBias", 0.002f);
        sunBakeCompute.SetFloat("SunBakeIntensity", 5.0f);
        sunBakeCompute.SetInt("ReversedZ", SystemInfo.usesReversedZBuffer ? 1 : 0);

        int groups = Mathf.CeilToInt((float)adaptiveVoxelResolution / 4f);
        sunBakeCompute.Dispatch(_sunBakeKernel, groups, groups, groups);
    }

    private void InitCheck()
    {
        if (initalized) return;

        Init();
    }

    private void Init()
    {
        sunDepthShader = Bundle.LoadAsset<Shader>("SEGIRenderSunDepth");
        clearCompute = SegiBeefEdit.LoadAsset<ComputeShader>("SEGIClearBeefEdit");
        transferIntsCompute = SegiBeefEdit.LoadAsset<ComputeShader>("SEGITransferIntsBeefEdit");
        mipFilterCompute = SegiBeefEdit.LoadAsset<ComputeShader>("SEGIMipFilterBeefEdit");
        voxelizationShaderBeefEdit = SegiBeefEdit.LoadAsset<Shader>("SEGIVoxelizeSceneBeefEdit");
        mergeVolumesCompute = SegiBeefEdit.LoadAsset<ComputeShader>("SEGIMergeVolumesBeefEdit");
        sliceClearCompute = SegiBeefEdit.LoadAsset<ComputeShader>("SEGISliceClearBeefEdit");
        var mainShader = SegiBeefEdit.LoadAsset<Shader>("SEGIBeefEdit");
        material = new Material(mainShader)
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        coneTraceCompute = SegiBeefEdit.LoadAsset<ComputeShader>("SEGIConeTraceBeefEdit");
        if (coneTraceCompute != null)
        {
            _probeTraceKernel = coneTraceCompute.FindKernel("ProbeTraceCS");
            _probeInterpolateKernel = coneTraceCompute.FindKernel("ProbeInterpolateCS");
        }
        temporalBlendCompute = SegiBeefEdit.LoadAsset<ComputeShader>("SEGITemporalBlendBeefEdit");
        if (temporalBlendCompute != null)
        {
            _temporalBlendKernel = temporalBlendCompute.FindKernel("TemporalBlendCS");
        }
        atrousFilterCompute = SegiBeefEdit.LoadAsset<ComputeShader>("SEGIATrousFilterBeefEdit");
        if (atrousFilterCompute != null)
        {
            _atrousKernel = atrousFilterCompute.FindKernel("ATrousFilter");
        }
        sunBakeCompute = SegiBeefEdit.LoadAsset<ComputeShader>("SEGISunBakeBeefEdit");
        if (sunBakeCompute != null)
        {
            _sunBakeKernel = sunBakeCompute.FindKernel("SunBake");
        }

        ConfigureCullingMask();

        attachedCamera = GetComponent<Camera>();
        attachedCamera.depthTextureMode |= DepthTextureMode.Depth;
        attachedCamera.depthTextureMode |= DepthTextureMode.MotionVectors;

        shadowCameraGameObject = GameObject.Find("SEGI_SHADOWCAM") ?? new GameObject("SEGI_SHADOWCAM")
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        if (shadowCameraGameObject.GetComponent<Camera>())
        {
            shadowCamera = shadowCameraGameObject.GetComponent<Camera>();
            shadowCameraTransform = shadowCameraGameObject.transform;
        }
        else
        {
            shadowCamera = shadowCameraGameObject.AddComponent<Camera>();
            shadowCamera.cullingMask = 0;
            shadowCamera.enabled = false;
            shadowCamera.depth = attachedCamera.depth - 1;
            shadowCamera.orthographic = true;
            shadowCamera.orthographicSize = adaptiveShadowSpaceSize;
            shadowCamera.clearFlags = CameraClearFlags.SolidColor;
            shadowCamera.backgroundColor = new Color(0.0f, 0.0f, 0.0f, 1.0f);
            float initDist = adaptiveShadowSpaceSize * 0.5f * shadowSpaceDepthRatio;
            float initMargin = adaptiveShadowSpaceSize * 1.5f;
            shadowCamera.nearClipPlane = Mathf.Max(0.01f, initDist - initMargin);
            shadowCamera.farClipPlane = initDist + initMargin;
            shadowCamera.cullingMask = giCullingMask;
            shadowCamera.useOcclusionCulling = false;
            shadowCameraTransform = shadowCameraGameObject.transform;
        }

        voxelCameraGameObject = GameObject.Find("SEGI_VOXEL_CAMERA") ?? new GameObject("SEGI_VOXEL_CAMERA")
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        if (voxelCameraGameObject.GetComponent<Camera>())
        {
            voxelCamera = voxelCameraGameObject.GetComponent<Camera>();
        }
        else
        {
            voxelCamera = voxelCameraGameObject.AddComponent<Camera>();
            voxelCamera.enabled = false;
            voxelCamera.orthographic = true;
            voxelCamera.orthographicSize = adaptiveVoxelSpaceSize * 0.5f;
            voxelCamera.nearClipPlane = 0.0f;
            voxelCamera.farClipPlane = adaptiveVoxelSpaceSize;
            voxelCamera.depth = -2;
            voxelCamera.renderingPath = RenderingPath.Forward;
            voxelCamera.clearFlags = CameraClearFlags.Color;
            voxelCamera.backgroundColor = Color.black;
            voxelCamera.useOcclusionCulling = false;
        }

        leftViewPoint = GameObject.Find("SEGI_LEFT_VOXEL_VIEW") ?? new GameObject("SEGI_LEFT_VOXEL_VIEW")
        {
            hideFlags = HideFlags.HideAndDontSave
        };

        topViewPoint = GameObject.Find("SEGI_TOP_VOXEL_VIEW") ?? new GameObject("SEGI_TOP_VOXEL_VIEW")
        {
            hideFlags = HideFlags.HideAndDontSave
        };

        blueNoise = null;
        blueNoise = new Texture2D[64];
        for (var i = 0; i < 64; i++)
        {
            var fileName = "LDR_RGBA_" + i.ToString();
            var blueNoiseTexture = Bundle.LoadAsset<Texture2D>(fileName);
            if (blueNoiseTexture == null)
                SEGIPlugin.Log.LogWarning(
                    "Unable to find noise texture \"Assets/SEGI/Resources/Noise Textures/" + fileName +
                    "\" for SEGI!");

            blueNoise[i] = blueNoiseTexture;
        }

        if (sunDepthTexture) CleanupTexture(ref sunDepthTexture);
        if (sunDepthTextureBack) CleanupTexture(ref sunDepthTextureBack);

        sunDepthTexture = new RenderTexture(sunShadowResolution, sunShadowResolution, 16, RenderTextureFormat.RHalf,
            RenderTextureReadWrite.Linear)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Point
        };
        sunDepthTexture.Create();
        sunDepthTextureBack = new RenderTexture(sunShadowResolution, sunShadowResolution, 16, RenderTextureFormat.RHalf,
            RenderTextureReadWrite.Linear)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Point
        };
        sunDepthTextureBack.Create();
        sunDepthShader.hideFlags = HideFlags.HideAndDontSave;

        voxelFlipFlop = 0;
        frameCounter = 0;
        _lastAdaptiveChangeTime = -999f;
        _previousLightweightMode = false;
        _lastSpatialCullUpdate = 0f;
        _lastEmissiveCacheUpdate = 0f;
        voxelSpaceOrigin = Vector3.zero;
        previousVoxelSpaceOrigin = Vector3.zero;
        _combinedVolumeOrigin = Vector3.zero;
        voxelSpaceOriginDelta = Vector3.zero;

        _cacheNeedsFullRefresh = true;
        _hybridCacheReady = false;
        _currentBuildBatch = 0;
        _buildCycleActive = false;
        _geomCacheWrapOffset = new int3Offset(0, 0, 0);
        _geomBatchesReady = false;
        _lastGeomCacheUpdate = 0f;
        _lastRendererSetHash = 0;
        for (int i = 0; i < _geomBatchCount; i++)
            _geomBatches[i] ??= new List<Renderer>();
        _lastSunDirection = Vector3.down;
        _sunShadowOrigin = Vector3.zero;
        _lastLightweightSunRefresh = -999f;
        _buildTargetOrigin = Vector3.zero;
        _scrollBuildPending = false;

        ResetFrameTimeTracking();
        currentAdaptiveScale = 1.0f;
        adaptiveMaxVoxelRes = (int)ConfigData.VoxelResolution;
        adaptiveMaxVoxelSpaceSize = ConfigData.VoxelSpaceSize;
        adaptiveMaxShadowSpaceSize = ConfigData.ShadowSpaceSize;
        adaptiveMaxCones = ConfigData.Cones;
        adaptiveMaxConeTraceSteps = ConfigData.ConeTraceSteps;

        adaptiveVoxelResolution = adaptiveMaxVoxelRes;
        adaptiveVoxelSpaceSize = adaptiveMaxVoxelSpaceSize;
        adaptiveShadowSpaceSize = adaptiveMaxShadowSpaceSize;
        adaptiveCones = adaptiveMaxCones;
        adaptiveConeTraceSteps = adaptiveMaxConeTraceSteps;
        adaptiveHalfResolution = ConfigData.HalfResolution;
        adaptiveVoxelAA = ConfigData.VoxelAntiAliasing;

        CreateVolumeTextures();

        Shader.SetGlobalFloat("SEGISoftSunlight", 0);
        Shader.SetGlobalInt("SEGISphericalSkylight", 0);
        material.SetInt("StochasticSampling", 1);
        _cachedGIGain = -999f;

        initalized = true;
    }

    private void CheckSupport()
    {
        systemSupported.HDRTextures = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf);
        systemSupported.RIntTextures = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RInt);
        systemSupported.DirectX11 = SystemInfo.graphicsShaderLevel >= 50 && SystemInfo.supportsComputeShaders;
        systemSupported.VolumeTextures = SystemInfo.supports3DTextures;
        systemSupported.PostShader = material.shader.isSupported;
        systemSupported.SunDepthShader = sunDepthShader.isSupported;
        systemSupported.VoxelizationShader = voxelizationShaderBeefEdit.isSupported;
        systemSupported.VoxelizationLightShader = voxelizationShaderBeefEdit.isSupported;

        if (!systemSupported.FullFunctionality)
        {
            SEGIPlugin.Log.LogWarning("SEGI is not supported on the current platform.");
            enabled = false;
            DestroyImmediate(this);
        }
    }

    private void ConfigureCullingMask()
    {
        giCullingMask = int.MaxValue;
        giCullingMask &= ~(1 << 5); // UI
        giCullingMask &= ~(1 << 12); // WorldspaceUI
        giCullingMask &= ~(1 << 13); // HUD
        giCullingMask &= ~(1 << 14); // MiniMap
        giCullingMask &= ~(1 << 15); // LabelText
        giCullingMask &= ~(1 << 8); // PlayerInvisible
        giCullingMask &= ~(1 << 10); // PlayerImmune
        giCullingMask &= ~(1 << 17); // CursorVoxel
        giCullingMask &= ~(1 << 18); // CharacterCreation
        giCullingMask &= ~(1 << 29); // ThumbnailCreation
        giCullingMask &= ~(1 << 1); // TransparentFX
        giCullingMask &= ~(1 << 2); // Ignore Raycast
        giCullingMask &= ~(1 << 21); // PostProcess
        giCullingMask &= ~(1 << 22); // Stars (sky elements)
        giCullingMask &= ~(1 << 23); // BlockSound
        giCullingMask &= ~(1 << 27); // LiquidSolverParticles
        giCullingMask &= ~(1 << 3); // (unnamed) - empty
        giCullingMask &= ~(1 << 6); // (unnamed) - empty
        giCullingMask &= ~(1 << 7); // (unnamed) - empty
        giCullingMask &= ~(1 << 28); // (unnamed) - empty
    }

    private void OnLightweightModeChanged()
    {
        CleanupCaches();
        if (_emissiveCacheCoroutine != null)
        {
            StopCoroutine(_emissiveCacheCoroutine);
            _emissiveCacheCoroutine = null;
        }
        _cachedEmissiveRenderers.Clear();
        _culledEmissiveRenderers.Clear();
        _lastEmissiveCacheUpdate = 0f;
        _lastSpatialCullUpdate = 0f;

        if (ConfigData.LightweightMode)
        {
            FreeGeomCacheVolumes();
        }
        else
        {
            EnsureGeomCacheVolumes(adaptiveVoxelResolution);
        }

        _cacheNeedsFullRefresh = true;
        _hybridCacheReady = false;
        _currentBuildBatch = 0;
        _buildCycleActive = false;
        _geomCacheWrapOffset = new int3Offset(0, 0, 0);
        _geomBatchesReady = false;
        _lastRendererSetHash = 0;
    }

    private Matrix4x4 TransformViewMatrix(Matrix4x4 mat)
    {
        //Since the third column of the view matrix needs to be reversed if using reversed z-buffer, do so here
        if (SystemInfo.usesReversedZBuffer)
        {
            mat[2, 0] = -mat[2, 0];
            mat[2, 1] = -mat[2, 1];
            mat[2, 2] = -mat[2, 2];
            mat[2, 3] = -mat[2, 3];
        }

        return mat;
    }

    private void UpdateEmissiveCache()
    {
        if (Time.time - _lastEmissiveCacheUpdate < 0.5f || _emissiveCacheCoroutine != null)
            return;

        if (_emissiveCacheCoroutine == null) _emissiveCacheCoroutine = StartCoroutine(UpdateEmissiveCacheCoroutine());
    }

    private IEnumerator UpdateEmissiveCacheCoroutine()
    {
        var newCache = _emissiveBuildBuffer;
        newCache.Clear();
        var frameStartTime = Time.realtimeSinceStartup * 1000f;
        var allRenderers = FindObjectsByType<Renderer>(FindObjectsSortMode.None);
        const int BATCH_SIZE = 100;
        for (var i = 0; i < allRenderers.Length; i += BATCH_SIZE)
        {
            var endIndex = Mathf.Min(i + BATCH_SIZE, allRenderers.Length);

            for (var j = i; j < endIndex; j++)
            {
                var r = allRenderers[j];
                if (r != null && r && r.gameObject != null && r.gameObject.activeInHierarchy)
                {
                    if (IsRendererEmissive(r))
                    {
                        r.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
                        newCache.Add(r);
                    }
                }
            }

            if (Time.realtimeSinceStartup * 1000f - frameStartTime > 0.2f)
            {
                yield return null;
                frameStartTime = Time.realtimeSinceStartup * 1000f;
            }
        }
        (_cachedEmissiveRenderers, _emissiveBuildBuffer) = (_emissiveBuildBuffer, _cachedEmissiveRenderers);
        _emissiveBuildBuffer.Clear();
        _culledEmissiveRenderers.Clear();
        _lastSpatialCullUpdate = 0f;
        _lastEmissiveCacheUpdate = Time.time;
        _emissiveCacheCoroutine = null;
        // SEGIPlugin.Log.LogInfo($"Updated emissive cache: {newCache.Count} renderers");
    }

    private List<Renderer> GetEmissiveRenderers()
    {
        if (Time.time - _lastSpatialCullUpdate < SpatialCullUpdateInterval)
            return _culledEmissiveRenderers;

        _culledEmissiveRenderers.Clear();

        var halfSize = adaptiveVoxelSpaceSize * 0.75f;
        var voxelMin = voxelSpaceOrigin - Vector3.one * halfSize;
        var voxelMax = voxelSpaceOrigin + Vector3.one * halfSize;

        int deadRefs = 0;
        int writeIdx = 0;
        int count = _cachedEmissiveRenderers.Count;
        for (var i = 0; i < count; i++)
        {
            var r = _cachedEmissiveRenderers[i];
            if (r == null || !r)
            {
                deadRefs++;
                continue;
            }

            _cachedEmissiveRenderers[writeIdx] = r;
            writeIdx++;

            var bounds = r.bounds;
            if (bounds.max.x >= voxelMin.x && bounds.min.x <= voxelMax.x &&
                bounds.max.y >= voxelMin.y && bounds.min.y <= voxelMax.y &&
                bounds.max.z >= voxelMin.z && bounds.min.z <= voxelMax.z)
            {
                _culledEmissiveRenderers.Add(r);
            }
        }

        if (writeIdx < count)
            _cachedEmissiveRenderers.RemoveRange(writeIdx, count - writeIdx);

        _lastSpatialCullUpdate = Time.time;
        return _culledEmissiveRenderers;
    }

    private bool IsRendererEmissive(Renderer renderer)
    {
        if (renderer == null)
            // LogExcludedRenderer(renderer, "null materials");
            return false;

        if (IncludedObjectNames.Contains(renderer.gameObject.name)) return true;
        if (ExcludedObjectNames.Contains(renderer.gameObject.name))
            // LogExcludedRenderer(renderer, "blacklisted object name");
            return false;
        // bool hasEmissiveProperty = false;
        // bool hasEmission = false;
        renderer.GetSharedMaterials(_sharedMaterialsBuffer);
        foreach (var material in _sharedMaterialsBuffer)
        {
            if (material is null) continue;
            if (ContainsEmissiveKeyword(material.name)) return true;
            if (!material.IsKeywordEnabled("_EMISSION")) continue;
            if (material.HasProperty("_EmissionColor"))
            {
                var emission = material.GetColor("_EmissionColor");
                var emissionIntensity = emission.r + emission.g + emission.b;
                if (emissionIntensity > 0.1f)
                    return true;
            }
        }
        // string reason = "no emission";
        // if (!hasEmissiveProperty)
        //     reason = "no _EmissionColor property";
        // else if (!hasEmission)
        //     reason = "emission too low";
        // LogExcludedRenderer(renderer, reason);
        return false;
    }
    private bool ContainsEmissiveKeyword(string materialName)
    {
        if (string.IsNullOrEmpty(materialName)) return false;

        foreach (var keyword in EmissiveKeywords)
            if (materialName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

        return false;
    }

    private void StartRobotEmissionFix()
    {
        if (_robotFixCoroutine != null) return;
        _robotFixCoroutine = StartCoroutine(RobotEmissionFixCoroutine());
    }

    private static int GetRendererTriangleCount(Renderer r)
    {
        Mesh mesh = null;
        if (r is SkinnedMeshRenderer smr)
            mesh = smr.sharedMesh;
        else
        {
            var mf = r.GetComponent<MeshFilter>();
            if (mf != null) mesh = mf.sharedMesh;
        }
        if (mesh == null) return 0;

        int totalIndices = 0;
        for (int i = 0; i < mesh.subMeshCount; i++)
            totalIndices += (int)mesh.GetIndexCount(i);
        return totalIndices / 3;
    }

    private void BuildBalancedBatches()
    {
        _weightedRenderers.Sort((a, b) => b.triangles.CompareTo(a.triangles));

        for (int i = 0; i < _geomBatchCount; i++)
        {
            _geomBatches[i] ??= new List<Renderer>();
            _geomBatches[i].Clear();
            _batchWeights[i] = 0;
        }
        _terrainBatch.Clear();

        foreach (var rw in _weightedRenderers)
        {
            if (rw.renderer != null && rw.renderer.gameObject.layer == TerrainLayer)
            {
                _terrainBatch.Add(rw.renderer);
                continue;
            }

            int lightest = 0;
            for (int i = 1; i < _geomBatchCount; i++)
            {
                if (_batchWeights[i] < _batchWeights[lightest])
                    lightest = i;
            }
            _geomBatches[lightest].Add(rw.renderer);
            _batchWeights[lightest] += rw.triangles;
        }

        int totalTris = 0;
        for (int i = 0; i < _geomBatchCount; i++)
            totalTris += _batchWeights[i];

    }

    private void UpdateGeomRendererCache()
    {
        if (Time.time - _lastGeomCacheUpdate < 2.0f || _geomCacheCoroutine != null)
            return;
        _geomCacheCoroutine = StartCoroutine(UpdateGeomRendererCacheCoroutine());
    }

    private IEnumerator UpdateGeomRendererCacheCoroutine()
    {
        _geomBuildBuffer.Clear();
        float frameStartTime = Time.realtimeSinceStartup * 1000f;
        var allRenderers = FindObjectsByType<Renderer>(FindObjectsSortMode.None);

        float halfSize = adaptiveVoxelSpaceSize * 0.75f;
        Vector3 voxelMin = voxelSpaceOrigin - Vector3.one * halfSize;
        Vector3 voxelMax = voxelSpaceOrigin + Vector3.one * halfSize;

        const int SCAN_BATCH_SIZE = 100;
        for (int i = 0; i < allRenderers.Length; i += SCAN_BATCH_SIZE)
        {
            int endIndex = Mathf.Min(i + SCAN_BATCH_SIZE, allRenderers.Length);
            for (int j = i; j < endIndex; j++)
            {
                var r = allRenderers[j];
                if (r == null || !r || r.gameObject == null || !r.gameObject.activeInHierarchy)
                    continue;

                if ((giCullingMask & (1 << r.gameObject.layer)) == 0) continue;

                if (IsRendererEmissive(r)) continue;

                var bounds = r.bounds;
                if (bounds.max.x < voxelMin.x || bounds.min.x > voxelMax.x ||
                    bounds.max.y < voxelMin.y || bounds.min.y > voxelMax.y ||
                    bounds.max.z < voxelMin.z || bounds.min.z > voxelMax.z)
                    continue;

                int triCount = GetRendererTriangleCount(r);
                if (triCount > 0)
                {
                    r.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
                    _geomBuildBuffer.Add(new RendererWeight { renderer = r, triangles = triCount });
                }
            }

            // Yield if we've spent too long this frame
            if (Time.realtimeSinceStartup * 1000f - frameStartTime > 0.3f)
            {
                yield return null;
                frameStartTime = Time.realtimeSinceStartup * 1000f;
            }
        }

        (_weightedRenderers, _geomBuildBuffer) = (_geomBuildBuffer, _weightedRenderers);
        int newHash = ComputeRendererSetHash(_weightedRenderers);
        bool changed = newHash != _lastRendererSetHash;
        _lastRendererSetHash = newHash;
        BuildBalancedBatches();

        _geomBatchesReady = true;
        if (changed && !_buildCycleActive && !_cacheNeedsFullRefresh)
        {
            _buildCycleActive = true;
            _currentBuildBatch = 0;
        }

        _lastGeomCacheUpdate = Time.time;
        _geomCacheCoroutine = null;
    }

    private static int ComputeRendererSetHash(List<RendererWeight> renderers)
    {
        int hash = renderers.Count;
        for (int i = 0; i < renderers.Count; i++)
        {
            var r = renderers[i];
            if (r.renderer != null)
                hash = hash * 31 + r.renderer.GetInstanceID();
        }
        return hash;
    }

    private IEnumerator RobotEmissionFixCoroutine()
    {
        var timeout = 120f;
        var elapsed = 0f;
        while (elapsed < timeout)
        {
            try
            {
                var worldSun = WorldManager.Instance?.WorldSun?.TargetLight;
                if (worldSun != null) break;
            }
            catch { }

            elapsed += 1f;
            yield return new WaitForSeconds(1f);
        }

        yield return new WaitForSeconds(5f);
        HookRobotSpawnEvent();
        FixAllRobotMaterials();

        _robotFixCoroutine = null;
    }

    private void FixAllRobotMaterials()
    {
        var totalFixed = 0;
        var robotCount = 0;

        foreach (var human in Human.AllHumans)
        {
            if (human == null) continue;
            if (!human.IsArtificial && human.SpeciesClass != SpeciesClass.Robot) continue;

            robotCount++;
            totalFixed += FixOneRobotMaterials(human);
        }

        if (robotCount > 0)
            SEGIPlugin.Log.LogInfo($"Robot emission fix: {robotCount} robot(s), suppressed {totalFixed} material(s)");
    }

    private int FixOneRobotMaterials(Human human)
    {
        var fixedCount = 0;
        var renderers = human.GetComponentsInChildren<Renderer>(true);

        foreach (var renderer in renderers)
        {
            if (renderer == null) continue;

            renderer.GetSharedMaterials(_sharedMaterialsBuffer);
            foreach (var mat in _sharedMaterialsBuffer)
            {
                if (mat == null) continue;

                var id = mat.GetInstanceID();
                if (_fixedRobotMaterialIds.Contains(id)) continue;

                if (ShouldSuppressRobotEmission(mat))
                {
                    mat.SetColor("_EmissionColor", Color.black);
                    mat.DisableKeyword("_EMISSION");
                    mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
                    _fixedRobotMaterialIds.Add(id);
                    fixedCount++;
                    // SEGIPlugin.Log.LogInfo($"Suppressed emission on '{mat.name}' ({renderer.gameObject.name})");
                }
            }
        }

        return fixedCount;
    }

    private static bool ShouldSuppressRobotEmission(Material mat)
    {
        if (mat.IsKeywordEnabled("_EMISSION")) return false;
        if (mat.name.StartsWith("Eye", StringComparison.OrdinalIgnoreCase)) return false;

        if (!mat.HasProperty("_EmissionColor")) return false;
        var emission = mat.GetColor("_EmissionColor");
        var emissionIntensity = emission.r + emission.g + emission.b;
        if (emissionIntensity < 0.001f) return false;

        return true;
    }

    private void HookRobotSpawnEvent()
    {
        if (_robotFixEventHooked) return;
        try
        {
            Human.OnHumanCreated += OnHumanCreatedRobotFix;
            _robotFixEventHooked = true;
        }
        catch (Exception ex)
        {
            SEGIPlugin.Log.LogWarning($"Could not hook OnHumanCreated for robot fix: {ex.Message}");
        }
    }

    private void UnhookRobotSpawnEvent()
    {
        if (!_robotFixEventHooked) return;
        try { Human.OnHumanCreated -= OnHumanCreatedRobotFix; } catch { }
        _robotFixEventHooked = false;
    }

    private void OnHumanCreatedRobotFix(Entity entity)
    {
        if (entity == null) return;
        var human = entity as Human;
        if (human == null) return;
        if (!human.IsArtificial && human.SpeciesClass != SpeciesClass.Robot) return;
        StartCoroutine(FixRobotDelayed(human));
    }

    private IEnumerator FixRobotDelayed(Human human)
    {
        yield return new WaitForSeconds(2f);
        if (human != null)
        {
            var count = FixOneRobotMaterials(human);
            if (count > 0)
                SEGIPlugin.Log.LogInfo($"Robot emission fix: {count} material(s) on newly spawned '{human.DisplayName}'");
        }
    }

    private void CleanupRobotEmissionFix()
    {
        if (_robotFixCoroutine != null)
        {
            StopCoroutine(_robotFixCoroutine);
            _robotFixCoroutine = null;
        }
        UnhookRobotSpawnEvent();
        _fixedRobotMaterialIds.Clear();
    }

    private void CreateVolumeTextures()
    {
        if (volumeTextures != null)
            for (var i = 0; i < volumeTextures.Length; i++)
                if (volumeTextures[i] != null)
                    CleanupTexture(ref volumeTextures[i]);

        volumeTextures = new RenderTexture[mipLevels];
        for (var i = 0; i < mipLevels; i++)
        {
            var resolution = adaptiveVoxelResolution / (1 << i);
            volumeTextures[i] = new RenderTexture(resolution, resolution, 0,
                RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
            {
                dimension = TextureDimension.Tex3D,
                volumeDepth = resolution,
                enableRandomWrite = true,
                filterMode = FilterMode.Bilinear,
                autoGenerateMips = false,
                useMipMap = false
            };
            volumeTextures[i].Create();
            volumeTextures[i].hideFlags = HideFlags.HideAndDontSave;
        }

        if (volumeTextureB) CleanupTexture(ref volumeTextureB);

        volumeTextureB = new RenderTexture(adaptiveVoxelResolution, adaptiveVoxelResolution, 0,
            RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
        {
            dimension = TextureDimension.Tex3D,
            volumeDepth = adaptiveVoxelResolution,
            enableRandomWrite = true,
            filterMode = FilterMode.Bilinear,
            autoGenerateMips = false,
            useMipMap = false
        };
        volumeTextureB.Create();
        volumeTextureB.hideFlags = HideFlags.HideAndDontSave;

        CreateIntVolume4(integerVolume4, adaptiveVoxelResolution);

        if (!ConfigData.LightweightMode)
        {
            EnsureGeomCacheVolumes(adaptiveVoxelResolution);
        }

        if (probeIrradiance) CleanupTexture(ref probeIrradiance);
        probeIrradiance = null;

        _cacheNeedsFullRefresh = true;
        _hybridCacheReady = false;
        _currentBuildBatch = 0;
        _buildCycleActive = false;
        _geomCacheWrapOffset = new int3Offset(0, 0, 0);

        ResizeDummyTexture();

        var voxelCameraResolution = DummyVoxelResolution;

        CreateCombinedVolume(adaptiveVoxelResolution);
    }

    private void CreateCombinedVolume(int resolution)
    {
        if (_combinedVolume) CleanupTexture(ref _combinedVolume);
        _combinedVolume = new RenderTexture(resolution, resolution, 0,
            RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
        {
            dimension = TextureDimension.Tex3D,
            volumeDepth = resolution,
            useMipMap = true,
            autoGenerateMips = false,
            enableRandomWrite = true,
            filterMode = FilterMode.Trilinear,
            hideFlags = HideFlags.HideAndDontSave
        };
        if (!_combinedVolume.Create())
            SEGIPlugin.Log.LogError($"Failed to create combined volume ({resolution}³ ARGBHalf Tex3D mipmapped)");
    }

    private void ResizeRenderTextures()
    {
        if (previousGIResult) CleanupTexture(ref previousGIResult);

        var width = attachedCamera.pixelWidth == 0 ? 2 : attachedCamera.pixelWidth;
        var height = attachedCamera.pixelHeight == 0 ? 2 : attachedCamera.pixelHeight;

        previousGIResult = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBHalf)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            useMipMap = true,
            autoGenerateMips = false,
            enableRandomWrite = true
        };
        previousGIResult.Create();
        previousGIResult.hideFlags = HideFlags.HideAndDontSave;

        if (_previousGIResultBack) CleanupTexture(ref _previousGIResultBack);

        _previousGIResultBack = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBHalf)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            useMipMap = true,
            autoGenerateMips = false,
            enableRandomWrite = true
        };
        _previousGIResultBack.Create();
        _previousGIResultBack.hideFlags = HideFlags.HideAndDontSave;

        if (previousCameraDepth) CleanupTexture(ref previousCameraDepth);

        previousCameraDepth =
            new RenderTexture(width, height, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                enableRandomWrite = true
            };
        previousCameraDepth.Create();
        previousCameraDepth.hideFlags = HideFlags.HideAndDontSave;

        if (_previousCameraDepthBack) CleanupTexture(ref _previousCameraDepthBack);

        _previousCameraDepthBack = new RenderTexture(width, height, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                enableRandomWrite = true
            };
        _previousCameraDepthBack.Create();
        _previousCameraDepthBack.hideFlags = HideFlags.HideAndDontSave;
    }

    private void ResizePostProcessRTs()
    {
        var width = attachedCamera.pixelWidth == 0 ? 2 : attachedCamera.pixelWidth;
        var height = attachedCamera.pixelHeight == 0 ? 2 : attachedCamera.pixelHeight;
        int giRes = GIRenderRes;
        int halfW = width / giRes;
        int halfH = height / giRes;

        if (_persistGI1) CleanupTexture(ref _persistGI1);
        _persistGI1 = new RenderTexture(halfW, halfH, 0, RenderTextureFormat.ARGBHalf)
            { enableRandomWrite = true, hideFlags = HideFlags.HideAndDontSave };
        _persistGI1.Create();

        if (_persistGI2) CleanupTexture(ref _persistGI2);
        _persistGI2 = new RenderTexture(halfW, halfH, 0, RenderTextureFormat.ARGBHalf)
            { enableRandomWrite = true, hideFlags = HideFlags.HideAndDontSave };
        _persistGI2.Create();

        if (_persistDepth) CleanupTexture(ref _persistDepth);
        _persistDepth = new RenderTexture(halfW, halfH, 0, RenderTextureFormat.RFloat,
            RenderTextureReadWrite.Linear)
            { filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave };
        _persistDepth.Create();

        if (_persistNormal) CleanupTexture(ref _persistNormal);
        _persistNormal = new RenderTexture(halfW, halfH, 0, RenderTextureFormat.ARGBHalf,
            RenderTextureReadWrite.Linear)
            { filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave };
        _persistNormal.Create();

        if (giRes == 2)
        {
            if (_persistGI3) CleanupTexture(ref _persistGI3);
            _persistGI3 = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBHalf)
                { filterMode = FilterMode.Point, enableRandomWrite = true, hideFlags = HideFlags.HideAndDontSave };
            _persistGI3.Create();

            if (_persistGI4) CleanupTexture(ref _persistGI4);
            _persistGI4 = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBHalf)
                { filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave };
            _persistGI4.Create();
        }
        else
        {
            if (_persistGI3) CleanupTexture(ref _persistGI3);
            if (_persistGI4) CleanupTexture(ref _persistGI4);
        }

        _prevGIRenderRes = giRes;
    }

    private void ResizeSunShadowBuffer()
    {
        if (sunDepthTexture) CleanupTexture(ref sunDepthTexture);
        if (sunDepthTextureBack) CleanupTexture(ref sunDepthTextureBack);

        sunDepthTexture = new RenderTexture(sunShadowResolution, sunShadowResolution, 16, RenderTextureFormat.RHalf,
            RenderTextureReadWrite.Linear)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Point
        };
        sunDepthTexture.Create();
        sunDepthTextureBack = new RenderTexture(sunShadowResolution, sunShadowResolution, 16, RenderTextureFormat.RHalf,
            RenderTextureReadWrite.Linear)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Point
        };
        sunDepthTextureBack.Create();
        sunDepthShader.hideFlags = HideFlags.HideAndDontSave;
    }

    private void ResizeDummyTexture()
    {
        if (dummyVoxelTextureAAScaled) CleanupTexture(ref dummyVoxelTextureAAScaled);

        dummyVoxelTextureAAScaled =
            new RenderTexture(DummyVoxelResolution, DummyVoxelResolution, 0, RenderTextureFormat.ARGBHalf);
        dummyVoxelTextureAAScaled.Create();
        dummyVoxelTextureAAScaled.hideFlags = HideFlags.HideAndDontSave;

        if (dummyVoxelTextureFixed) CleanupTexture(ref dummyVoxelTextureFixed);

        dummyVoxelTextureFixed = new RenderTexture(adaptiveVoxelResolution, adaptiveVoxelResolution,
            0, RenderTextureFormat.ARGBHalf);
        dummyVoxelTextureFixed.Create();
        dummyVoxelTextureFixed.hideFlags = HideFlags.HideAndDontSave;
    }

    private void CleanupTexture(ref RenderTexture texture)
    {
        if (texture)
        {
            texture.DiscardContents();
            texture.Release();
            DestroyImmediate(texture);
        }
    }

    private static readonly string[] ChannelSuffixes = { "_R", "_G", "_B", "_A" };

    private void CreateIntVolume4(RenderTexture[] vol, int resolution)
    {
        for (int c = 0; c < 4; c++)
        {
            if (vol[c]) CleanupTexture(ref vol[c]);
            vol[c] = new RenderTexture(resolution, resolution, 0,
                RenderTextureFormat.RInt, RenderTextureReadWrite.Linear)
            {
                dimension = TextureDimension.Tex3D,
                volumeDepth = resolution,
                enableRandomWrite = true,
                filterMode = FilterMode.Point,
                hideFlags = HideFlags.HideAndDontSave
            };
            vol[c].Create();
        }
    }

    private void CleanupIntVolume4(RenderTexture[] vol)
    {
        for (int c = 0; c < 4; c++)
            CleanupTexture(ref vol[c]);
    }

    private bool GeomCacheAllocated =>
        geomCacheVolume4[0] != null && geomCacheVolume4[0].IsCreated();

    private void EnsureGeomCacheVolumes(int resolution)
    {
        if (geomCacheVolume4[0] == null || !geomCacheVolume4[0].IsCreated())
        {
            CreateIntVolume4(geomCacheVolume4, resolution);
            CreateIntVolume4(geomCacheCopy4, resolution);
            _cacheNeedsFullRefresh = true;
            _hybridCacheReady = false;
            _geomCacheWrapOffset = new int3Offset(0, 0, 0);
        }
    }

    private void FreeGeomCacheVolumes()
    {
        CleanupIntVolume4(geomCacheVolume4);
        CleanupIntVolume4(geomCacheCopy4);
        _hybridCacheReady = false;
    }

    private void ClearIntVolume4(RenderTexture[] vol, int resolution)
    {
        for (int c = 0; c < 4; c++)
            clearCompute.SetTexture(0, "RG0" + ChannelSuffixes[c], vol[c]);
        clearCompute.SetInt("Res", resolution);
        int groups = Mathf.CeilToInt((float)resolution / 4f);
        clearCompute.Dispatch(0, groups, groups, groups);
    }

    private void SetIntVolume4OnCompute(ComputeShader cs, int kernel, string prefix, RenderTexture[] vol)
    {
        for (int c = 0; c < 4; c++)
            cs.SetTexture(kernel, prefix + ChannelSuffixes[c], vol[c]);
    }

    private void SetIntVolume4RandomWrite(RenderTexture[] vol, int startIdx = 1)
    {
        for (int c = 0; c < 4; c++)
            Graphics.SetRandomWriteTarget(startIdx + c, vol[c]);
    }

    private void CleanupTextures()
    {
        CleanupTexture(ref sunDepthTexture);
        CleanupTexture(ref sunDepthTextureBack);
        CleanupTexture(ref previousGIResult);
        CleanupTexture(ref previousCameraDepth);
        CleanupTexture(ref _previousGIResultBack);
        CleanupTexture(ref _previousCameraDepthBack);
        CleanupIntVolume4(integerVolume4);
        CleanupIntVolume4(geomCacheVolume4);
        CleanupIntVolume4(geomCacheCopy4);

        if (volumeTextures != null)
            for (var i = 0; i < volumeTextures.Length; i++)
                CleanupTexture(ref volumeTextures[i]);

        CleanupTexture(ref _combinedVolume);
        CleanupTexture(ref volumeTextureB);
        CleanupTexture(ref probeIrradiance);
        CleanupTexture(ref dummyVoxelTextureAAScaled);
        CleanupTexture(ref dummyVoxelTextureFixed);
        CleanupTexture(ref _persistGI1);
        CleanupTexture(ref _persistGI2);
        CleanupTexture(ref _persistDepth);
        CleanupTexture(ref _persistNormal);
        CleanupTexture(ref _persistGI3);
        CleanupTexture(ref _persistGI4);
    }

    private void Cleanup()
    {
        CleanupRobotEmissionFix();

        if (_emissiveCacheCoroutine != null)
        {
            StopCoroutine(_emissiveCacheCoroutine);
            _emissiveCacheCoroutine = null;
        }
        if (_geomCacheCoroutine != null)
        {
            StopCoroutine(_geomCacheCoroutine);
            _geomCacheCoroutine = null;
        }
        try
        {
            Shader.SetGlobalTexture("SEGIVolume", null);
            Shader.SetGlobalTexture("SEGIVolumeTexture1", null);
            Shader.SetGlobalTexture("SEGISunDepth", null);
        }
        catch (Exception ex)
        {
            SEGIPlugin.Log.LogError($"Error clearing: {ex.Message}");
        }

        CleanupCaches();

        DestroyImmediate(material);
        material = null;

        DestroyImmediate(voxelCameraGameObject);
        voxelCameraGameObject = null;
        voxelCamera = null;

        DestroyImmediate(leftViewPoint);
        leftViewPoint = null;

        DestroyImmediate(topViewPoint);
        topViewPoint = null;

        DestroyImmediate(shadowCameraGameObject);
        shadowCameraGameObject = null;
        shadowCamera = null;
        shadowCameraTransform = null;

        initalized = false;
        CleanupTextures();
    }

    private void CleanupCaches()
    {
        var deadKeys = new List<GameObject>();
        foreach (var kvp in _layerRestoreCache)
        {
            if (kvp.Key == null || !kvp.Key)
            {
                deadKeys.Add(kvp.Key);
            }
        }
        foreach (var key in deadKeys)
        {
            _layerRestoreCache.Remove(key);
        }
        _layerRestoreCache.Clear();
        _culledEmissiveRenderers.Clear();
    }

    private void ScrollGeometryCache()
    {
        if (!GeomCacheAllocated) return;
        Vector3 voxelDelta = voxelSpaceOriginDelta / adaptiveVoxelSpaceSize * adaptiveVoxelResolution;
        int3Offset scrollOffset = new int3Offset(
            Mathf.RoundToInt(voxelDelta.x),
            Mathf.RoundToInt(voxelDelta.y),
            Mathf.RoundToInt(voxelDelta.z));

        if (scrollOffset.x == 0 && scrollOffset.y == 0 && scrollOffset.z == 0)
            return;

        int res = adaptiveVoxelResolution;

        if (Mathf.Abs(scrollOffset.x) > res / 2 ||
            Mathf.Abs(scrollOffset.y) > res / 2 ||
            Mathf.Abs(scrollOffset.z) > res / 2)
        {
            ClearIntVolume4(geomCacheVolume4, res);
            _geomCacheWrapOffset = new int3Offset(0, 0, 0);
            return;
        }

        int[] axisDeltas = { scrollOffset.x, scrollOffset.y, scrollOffset.z };
        int[] wrapAxes = { _geomCacheWrapOffset.x, _geomCacheWrapOffset.y, _geomCacheWrapOffset.z };

        for (int axis = 0; axis < 3; axis++)
        {
            int S = axisDeltas[axis];
            if (S == 0) continue;

            int absS = Mathf.Abs(S);
            int startPhys;
            if (S > 0)
                startPhys = ((wrapAxes[axis] % res) + res) % res;
            else
                startPhys = (((wrapAxes[axis] + S) % res) + res) % res;

            ClearGeomCacheSlices(axis, startPhys, absS, res);
        }

        _geomCacheWrapOffset = new int3Offset(
            ((_geomCacheWrapOffset.x + scrollOffset.x) % res + res) % res,
            ((_geomCacheWrapOffset.y + scrollOffset.y) % res + res) % res,
            ((_geomCacheWrapOffset.z + scrollOffset.z) % res + res) % res);
    }

    private void ClearGeomCacheSlices(int axis, int startPhys, int count, int res)
    {
        SetIntVolume4OnCompute(sliceClearCompute, 0, "Dst", geomCacheVolume4);

        sliceClearCompute.SetInt("Resolution", res);
        sliceClearCompute.SetInt("Axis", axis);
        sliceClearCompute.SetInt("SliceStart", startPhys);
        sliceClearCompute.SetInt("SliceCount", count);

        int gSlice = Mathf.CeilToInt((float)count / 4f);
        int gFace  = Mathf.CeilToInt((float)res / 4f);
        sliceClearCompute.Dispatch(0, gSlice, gFace, gFace);
    }

    private struct int3Offset
    {
        public int x, y, z;
        public int3Offset(int x, int y, int z) { this.x = x; this.y = y; this.z = z; }
    }

    private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        initalized = false;
        _cachedEmissiveRenderers.Clear();
        CleanupCaches();
        _lastEmissiveCacheUpdate = 0f;
        _lastSpatialCullUpdate = 0f;
        if (_emissiveCacheCoroutine != null)
        {
            StopCoroutine(_emissiveCacheCoroutine);
            _emissiveCacheCoroutine = null;
        }
        if (_geomCacheCoroutine != null)
        {
            StopCoroutine(_geomCacheCoroutine);
            _geomCacheCoroutine = null;
        }

        _cacheNeedsFullRefresh = true;
        _hybridCacheReady = false;
        _currentBuildBatch = 0;
        _buildCycleActive = false;
        _geomCacheWrapOffset = new int3Offset(0, 0, 0);
        _geomBatchesReady = false;
        _lastGeomCacheUpdate = 0f;
        _lastRendererSetHash = 0;

        CleanupRobotEmissionFix();
        StartRobotEmissionFix();

        SEGIPlugin.Log.LogInfo($"SEGI Plus reset for scene: {scene.name}");
    }

    private void LogRenderingLayers()
    {
        SEGIPlugin.Log.LogInfo("=== RENDERING LAYERS ===");

        for (var i = 0; i < 32; i++)
        {
            var layerName = LayerMask.LayerToName(i);
            var isInCullingMask = (giCullingMask & (1 << i)) != 0;

            if (!string.IsNullOrEmpty(layerName))
                SEGIPlugin.Log.LogInfo(
                    $"Layer {i:D2}: '{layerName}' - {(isInCullingMask ? "INCLUDED" : "EXCLUDED")} in GI");
            else if (isInCullingMask) SEGIPlugin.Log.LogInfo($"Layer {i:D2}: (unnamed) - INCLUDED in GI");
        }

        SEGIPlugin.Log.LogInfo($"Current giCullingMask value: {giCullingMask}");
        SEGIPlugin.Log.LogInfo("=== END LAYERS ===");
    }

    private void LogExcludedRenderer(Renderer renderer, string reason)
    {
        if (renderer == null) return;

        string objectName = renderer.gameObject.name;
        string materialNames = string.Join(", ", renderer.sharedMaterials?.Where(m => m != null).Select(m => m.name) ?? new string[0]);
        string logKey = $"{objectName}|{materialNames}|{reason}";
        if (LoggedExclusions.Add(logKey))
        {
            SEGIPlugin.Log.LogInfo($"EXCLUDED: '{objectName}' materials:[{materialNames}] reason:{reason}");
        }
    }

    private void UpdateFrameData()
    {
        float currentFrameTime = Time.unscaledDeltaTime;
        _bucketSum += currentFrameTime;
        _bucketCount++;
        if (currentFrameTime > _bucketMax) _bucketMax = currentFrameTime;
        _bucketTimer += currentFrameTime;
        if (_bucketTimer >= BucketDuration && _bucketCount > 1)
        {
            float trimmedAvg = (_bucketSum - _bucketMax) / (_bucketCount - 1);
            if (_bucketAverages.Count >= MaxBuckets)
                _bucketAverages.Dequeue();
            _bucketAverages.Enqueue(trimmedAvg);
            float sum = 0f;
            foreach (var b in _bucketAverages) sum += b;
            frameTimeAverage = sum / _bucketAverages.Count;

            _bucketSum = 0f;
            _bucketMax = 0f;
            _bucketCount = 0;
            _bucketTimer = 0f;
        }

        adaptiveLongTermTimer += Time.unscaledDeltaTime;
        float targetFrameTime = TargetFrameTime;
        float frameTimeRatio = frameTimeAverage / targetFrameTime;
        adaptiveLongTermAcc += frameTimeRatio;
    }

    private float ComputeRepresentativeFrameTime()
    {
        return frameTimeAverage;
    }

    private void ResetFrameTimeTracking()
    {
        _bucketSum = 0f;
        _bucketMax = 0f;
        _bucketCount = 0;
        _bucketTimer = 0f;
        _bucketAverages.Clear();
        frameTimeAverage = 0.016f;
    }

    private void UpdateAdaptivePerformance()
    {
        if (!ConfigData.AdaptivePerformance)
        {
            adaptiveVoxelResolution = (int)ConfigData.VoxelResolution;
            adaptiveVoxelSpaceSize = ConfigData.VoxelSpaceSize;
            adaptiveShadowSpaceSize = ConfigData.ShadowSpaceSize;
            adaptiveCones = ConfigData.Cones;
            adaptiveConeTraceSteps = ConfigData.ConeTraceSteps;
            adaptiveHalfResolution = ConfigData.HalfResolution;
            adaptiveVoxelAA = ConfigData.VoxelAntiAliasing;
            adaptiveLongTermAcc = 0f;
            adaptiveLongTermTimer = 0f;
            return;
        }

        int adaptiveStrategy = ConfigData.AdaptiveStrategy;
        adaptiveMaxVoxelRes = (int)ConfigData.VoxelResolution;
        adaptiveMaxVoxelSpaceSize = ConfigData.GetAdaptiveMaxVoxelSpaceSize(adaptiveStrategy);
        adaptiveMaxShadowSpaceSize = adaptiveMaxVoxelSpaceSize * 0.75f;
        adaptiveMaxCones = ConfigData.Cones;
        adaptiveMaxConeTraceSteps = ConfigData.ConeTraceSteps;

        // float scaleDownThreshold = ConfigData.AdaptiveScaleDownThreshold;
        // float scaleUpThreshold = ConfigData.AdaptiveScaleUpThreshold;

        float scaleDownThreshold = ConfigData.AdaptiveScaleDownThreshold / performanceMarginMultiplier;
        float scaleUpThreshold = ConfigData.AdaptiveScaleUpThreshold / performanceMarginMultiplier;

        float targetFrameTime = TargetFrameTime;
        float representativeFrameTime = ComputeRepresentativeFrameTime();
        float frameTimeRatio = representativeFrameTime / targetFrameTime;
        bool inAdaptiveCooldown = Time.unscaledTime - _lastAdaptiveChangeTime < AdaptiveChangeCooldown;

        float scaleDelta = 0f;
        if (!inAdaptiveCooldown)
        {
            if (frameTimeRatio > scaleDownThreshold) // less quality needed
            {
                // scaleDelta = -(frameTimeRatio - scaleDownThreshold) * ConfigData.AdaptiveRate;
                scaleDelta = -(frameTimeRatio - scaleDownThreshold) * ConfigData.AdaptiveRate * performanceMarginMultiplier;
            }
            else if (frameTimeRatio < scaleUpThreshold) // more quality wanted
            {
                // scaleDelta = (scaleUpThreshold - frameTimeRatio) * ConfigData.AdaptiveRate;
                scaleDelta = (scaleUpThreshold - frameTimeRatio) * ConfigData.AdaptiveRate / performanceMarginMultiplier;
            }
            else if (adaptiveLongTermTimer >= AdaptiveLongTermInterval)
            {
                float samples = adaptiveLongTermTimer / (frameTimeAverage > 0 ? frameTimeAverage : 0.016f);
                float longTermAverage = samples > 0 ? adaptiveLongTermAcc / samples : 1.0f;
                float longTermThreshold = AdaptiveLongTermThreshold * (isFrameCapped ? 0.95f : 1.0f);
                if (longTermAverage <= longTermThreshold && currentAdaptiveScale < 1.0f)
                {
                    scaleDelta = 0.05f / (isFrameCapped ? performanceMarginMultiplier : 1.0f);
                }

                adaptiveLongTermAcc = 0f;
                adaptiveLongTermTimer = 0f;
            }
        }

        currentAdaptiveScale = Mathf.Clamp(currentAdaptiveScale + scaleDelta, 0.125f, 1.0f);

        float voxelSpaceScaleThreshold = ConfigData.GetAdaptiveVoxelSpaceScaleThreshold(adaptiveStrategy);
        float voxelSpaceScaleRange = ConfigData.GetAdaptiveVoxelSpaceScaleRange(adaptiveStrategy);
        float voxelSpaceScale = Mathf.Clamp01((currentAdaptiveScale - (voxelSpaceScaleThreshold - voxelSpaceScaleRange)) / voxelSpaceScaleRange);

        float minVoxelSpaceSize = ConfigData.GetAdaptiveMinVoxelSpaceSize(adaptiveStrategy);
        float targetVoxelSpaceSize;

        if (currentAdaptiveScale < voxelSpaceScaleThreshold)
        {
            targetVoxelSpaceSize = Mathf.Lerp(minVoxelSpaceSize, adaptiveMaxVoxelSpaceSize, voxelSpaceScale);
        }
        else
        {
            targetVoxelSpaceSize = adaptiveMaxVoxelSpaceSize;
        }

        if (Mathf.Abs(targetVoxelSpaceSize - adaptiveVoxelSpaceSize) >= 1.0f
            && Time.unscaledTime - _lastAdaptiveChangeTime >= AdaptiveChangeCooldown)
        {
            adaptiveVoxelSpaceSize = targetVoxelSpaceSize;
            adaptiveShadowSpaceSize = targetVoxelSpaceSize * 0.75f;
            _lastAdaptiveChangeTime = Time.unscaledTime;
            ResetFrameTimeTracking();
        }

        float resolutionScaleThreshold = ConfigData.GetAdaptiveResolutionScaleThreshold(adaptiveStrategy);
        float resolutionScaleRange = ConfigData.GetAdaptiveResolutionScaleRange(adaptiveStrategy);
        float resolutionScale = Mathf.Clamp01((currentAdaptiveScale - (resolutionScaleThreshold - resolutionScaleRange)) / resolutionScaleRange);

        int targetRes;
        if (currentAdaptiveScale < resolutionScaleThreshold)
        {
            float minRatio = minVoxelSpaceSize / adaptiveMaxVoxelSpaceSize;

            if (resolutionScale > minRatio)
            {
                targetRes = adaptiveMaxVoxelRes;
            }
            else
            {
                float adjustedResScale = resolutionScale / minRatio;
                targetRes = Mathf.RoundToInt(Mathf.Lerp(ConfigData.AdaptiveMinVoxelRes, adaptiveMaxVoxelRes, adjustedResScale));
            }
            targetRes = Mathf.Max(ConfigData.AdaptiveMinVoxelRes, (targetRes / 32) * 32);
        }
        else
        {
            targetRes = adaptiveMaxVoxelRes;
        }

        int newVoxelRes = Mathf.Clamp(targetRes, ConfigData.AdaptiveMinVoxelRes, adaptiveMaxVoxelRes);
        if (Mathf.Abs(newVoxelRes - adaptiveVoxelResolution) >= 32
            && Time.unscaledTime - _lastAdaptiveChangeTime >= AdaptiveChangeCooldown)
        {
            adaptiveVoxelResolution = newVoxelRes;
            _lastAdaptiveChangeTime = Time.unscaledTime;
            ResetFrameTimeTracking();
        }

        float conesStepsScaleThreshold = ConfigData.GetAdaptiveConesStepsScaleThreshold(adaptiveStrategy);
        float conesStepsScaleRange = ConfigData.GetAdaptiveConesStepsScaleRange(adaptiveStrategy);
        float conesStepsScale = Mathf.Clamp01((currentAdaptiveScale - (conesStepsScaleThreshold - conesStepsScaleRange)) / conesStepsScaleRange);

        adaptiveCones = Mathf.Min(adaptiveMaxCones,
            Mathf.Max(ConfigData.AdaptiveMinCones,
                Mathf.RoundToInt(Mathf.Lerp(ConfigData.AdaptiveMinCones, adaptiveMaxCones, conesStepsScale))));

        adaptiveConeTraceSteps = Mathf.Min(adaptiveMaxConeTraceSteps,
            Mathf.Max(ConfigData.AdaptiveMinConeTraceSteps,
                Mathf.RoundToInt(Mathf.Lerp(ConfigData.AdaptiveMinConeTraceSteps, adaptiveMaxConeTraceSteps, conesStepsScale))));

        if (currentAdaptiveScale >= 0.9f)
        {
            adaptiveHalfResolution = ConfigData.AdaptiveMaxHalfResolution;
            adaptiveVoxelAA = ConfigData.AdaptiveMaxVoxelAA;
        }
        else
        {
            float halfResOnThreshold = ConfigData.GetAdaptiveHalfResOnThreshold(adaptiveStrategy);
            float halfResOffThreshold = ConfigData.GetAdaptiveHalfResOffThreshold(adaptiveStrategy);

            if (currentAdaptiveScale < halfResOnThreshold)
                adaptiveHalfResolution = true;
            else if (currentAdaptiveScale >= halfResOffThreshold)
                adaptiveHalfResolution = ConfigData.AdaptiveMaxHalfResolution;

            float voxelAAOffThreshold = ConfigData.GetAdaptiveVoxelAAOffThreshold(adaptiveStrategy);
            float voxelAAOnThreshold = ConfigData.GetAdaptiveVoxelAAOnThreshold(adaptiveStrategy);

            if (currentAdaptiveScale < voxelAAOffThreshold)
                adaptiveVoxelAA = false;
            else if (currentAdaptiveScale >= voxelAAOnThreshold)
                adaptiveVoxelAA = ConfigData.AdaptiveMaxVoxelAA;
        }

        // float currentFPS = 1.0f / frameTimeAverage;
        // SEGIPlugin.Log.LogInfo(
        //     $"FPS={currentFPS:F1} Target={ConfigData.TargetFramerate} Scale={currentAdaptiveScale:F2} " +
        //     $"VoxelRes={adaptiveVoxelResolution} Cones={adaptiveCones} Steps={adaptiveConeTraceSteps} " +
        //     $"HalfRes={adaptiveHalfResolution} VoxelAA={adaptiveVoxelAA}");
    }

    private int GetGameFrameCap()
    {
        if (Time.time - lastFrameCapCheck < FrameCapCheckInterval)
            return cachedFrameCap;

        lastFrameCapCheck = Time.time;

        try
        {
            int targetFrameRate = Application.targetFrameRate;
            if (targetFrameRate <= 0 || targetFrameRate > 250)
            {
                cachedFrameCap = -1;
            }
            else if (targetFrameRate == 25)
            {
                cachedFrameCap = 25;
            }
            else
            {
                cachedFrameCap = targetFrameRate - 1;
            }
        }
        catch
        {
            cachedFrameCap = -1;
        }

        return cachedFrameCap;
    }

    private void UpdateFrameCapStatus()
    {
        int frameCap = GetGameFrameCap();
        float userTarget = ConfigData.TargetFramerate;
        if (frameCap <= 0)
        {
            isFrameCapped = false;
            performanceMarginMultiplier = 1.0f;
            effectiveTargetFrameTime = 1.0f / userTarget;
        }
        else if (userTarget <= frameCap)
        {
            isFrameCapped = false;
            performanceMarginMultiplier = 1.0f;
            effectiveTargetFrameTime = 1.0f / userTarget;
        }
        else
        {
            isFrameCapped = true;
            performanceMarginMultiplier = userTarget / (float)frameCap;
            effectiveTargetFrameTime = 1.0f / frameCap;
        }
    }
}

#if SEGI_PROFILER
internal static class DebugOverrides
{
    public static float? TemporalBlendWeight = null;
    public static float? DisocclusionSensitivity = null;
    public static float? MotionBlendMax = null;
    public static float? ConeLength = null;
    public static float? ConeWidth = null;
    public static float? ConeTraceBias = null;
    public static float? OcclusionStrength = null;
    public static float? NearOcclusionStrength = null;
    public static float? SunShadowSoftness = null;
    public static int?   SunShadowResolution = null;
    public static float? EmissiveTemporalBlend = null;
    public static bool? ForwardOriginBias = null;
}
#endif