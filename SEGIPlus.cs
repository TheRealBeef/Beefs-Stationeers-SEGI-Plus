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
    // private const float SunHorizonThreshold = 5.0f; //deg
    private const float SunHorizonThresholdMin = 2.0f; // deg
    private const float SunHorizonThresholdMax = 10.0f; // deg
    private const float MaterialCacheClearInterval = 300f; //s
    private const float SpatialCullUpdateInterval = 0.1f; //s
    private const int mipLevels = 6;

    private bool initalized = false;
    private bool notReadyToRender = false;
    private bool _previousLightweightMode = false;

    private int sunShadowResolution = 256;
    private int frameCounter;
    private int voxelFlipFlop;
    private int prevSunShadowResolution;

    private float shadowSpaceDepthRatio = 10.0f;
    private float _currentAmbientBrightness;

    // private float TargetFrameTime => 1.0f / ConfigData.TargetFramerate;
    private float TargetFrameTime => effectiveTargetFrameTime;
    private float currentAdaptiveScale = 1.0f;
    private Queue<float> frameTimeHistory = new Queue<float>();
    private float frameTimeAverage = 0.016f;
    private float frameTimeSum = 0f;
    private float adaptiveLongTermAcc = 0f;
    private float adaptiveLongTermTimer = 0f;
    private const float AdaptiveLongTermInterval = 5.0f;
    private const float AdaptiveLongTermThreshold = 0.95f;
    private int adaptiveVoxelResolution;
    private float adaptiveVoxelSpaceSize;
    private float adaptiveShadowSpaceSize;
    private int adaptiveCones;
    private int adaptiveConeTraceSteps;
    private bool adaptiveHalfResolution;
    private bool adaptiveVoxelAA;
    private bool adaptiveBilateralFiltering;
    private int adaptiveMaxVoxelRes;
    private float adaptiveMaxVoxelSpaceSize;
    private float adaptiveMaxShadowSpaceSize;
    private int adaptiveMaxCones;
    private int adaptiveMaxConeTraceSteps;
    private bool adaptiveSkipVoxelization;
    private int adaptiveVoxelizationInterval = 1;
    private int adaptiveVoxelizationFrameCounter = 0;
    private float _lastResolutionChangeTime = -999f;
    private const float ResolutionChangeCooldown = 3.0f;
    private float performanceMarginMultiplier = 1.0f;
    private float effectiveTargetFrameTime;
    private bool isFrameCapped = false;
    private int cachedFrameCap = -1;
    private float lastFrameCapCheck = 0f;
    private const float FrameCapCheckInterval = 1.0f;
    private float _cachedGIGain = -999f;
    private float _cachedNearLightGain = -999f;
    private float _cachedSecondaryBounceGain = -999f;
    private float _cachedTraceLength = -999f;
    private float _cachedConeWidth = -999f;
    private float _cachedOcclusionStrength = -999f;
    private float _cachedOcclusionPower = -999f;
    private float _cachedConeTraceBias = -999f;
    private float _cachedNearOcclusionStrength = -999f;
    private float _cachedFarOcclusionStrength = -999f;
    private float _cachedFarthestOcclusionStrength = -999f;
    private float _cachedBlendWeight = -999f;
    private int _cachedInnerOcclusionLayers = -999;
    private int _cachedAdaptiveCones = -999;
    private int _cachedAdaptiveConeTraceSteps = -999;
    private int _cachedAdaptiveHalfRes = -999;
    private const int LowerPrecisionMipThreshold = 3;
    private static readonly string[] MipLevelNames = {
        "SEGIVolumeLevel1", "SEGIVolumeLevel2", "SEGIVolumeLevel3",
        "SEGIVolumeLevel4", "SEGIVolumeLevel5"
    };

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

    private enum RenderState
    {
        Voxelize,
        Bounce
    }

    private float VoxelScaleFactor => (float)ConfigData.VoxelResolution / 256.0f;
    private RenderState renderState = RenderState.Voxelize;
    private Material material;
    private Camera attachedCamera;
    private Transform shadowCameraTransform;
    private Camera shadowCamera;
    private GameObject shadowCameraGameObject;
    private Texture2D[] blueNoise;
    private Shader sunDepthShader;
    private RenderTexture sunDepthTexture;
    private RenderTexture previousGIResult;
    private RenderTexture previousCameraDepth;
    private RenderTexture integerVolume;
    private RenderTexture[] volumeTextures;
    private RenderTexture secondaryIrradianceVolume;
    private RenderTexture volumeTextureB;
    private RenderTexture activeVolume;
    private RenderTexture previousActiveVolume;
    private RenderTexture dummyVoxelTextureAAScaled;
    private RenderTexture dummyVoxelTextureFixed;
    private Shader voxelizationShader;
    private Shader voxelizationShaderBeefEdit;
    private Shader voxelTracingShader;
    private Shader voxelTracingShaderBeefEdit;
    private ComputeShader clearCompute;
    private ComputeShader transferIntsCompute;
    private ComputeShader mipFilterCompute;
    private Camera voxelCamera;
    private GameObject voxelCameraGameObject;
    private GameObject leftViewPoint;
    private GameObject topViewPoint;
    private Vector3 voxelSpaceOrigin;
    private Vector3 previousVoxelSpaceOrigin;
    private Vector3 voxelSpaceOriginDelta;
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

    // Robot fix
    private readonly HashSet<int> _fixedRobotMaterialIds = new();
    private bool _robotFixEventHooked;
    private Coroutine _robotFixCoroutine;
    private static readonly List<Material> _sharedMaterialsBuffer = new();

    private struct Pass
    {
        public static int DiffuseTrace = 0;
        public static int BilateralBlur = 1;
        public static int BlendWithScene = 2;
        public static int TemporalBlend = 3;
        public static int SpecularTrace = 4;
        public static int GetCameraDepthTexture = 5;
        public static int GetWorldNormals = 6;
        public static int VisualizeGI = 7;
        public static int WriteBlack = 8;
        public static int VisualizeVoxels = 10;
        public static int BilateralUpsample = 11;
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
        public bool TracingShader;

        public readonly bool FullFunctionality => HDRTextures && RIntTextures && DirectX11 && VolumeTextures &&
                                                  PostShader && SunDepthShader && VoxelizationShader &&
                                                  VoxelizationLightShader &&
                                                  TracingShader;

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
        InitCheck();
        ResizeRenderTextures();
        CheckSupport();
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        StartRobotEmissionFix();
        notReadyToRender = false;
    }

    private void OnDisable()
    {
        notReadyToRender = true;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        Cleanup();
    }

    private void Update()
    {
        if (notReadyToRender) return;

        if (previousGIResult == null) ResizeRenderTextures();

        if (previousGIResult.width != attachedCamera.pixelWidth ||
            previousGIResult.height != attachedCamera.pixelHeight)
            ResizeRenderTextures();

        if (sunShadowResolution != prevSunShadowResolution) ResizeSunShadowBuffer();

        prevSunShadowResolution = sunShadowResolution;

        if (volumeTextures[0].width != adaptiveVoxelResolution) CreateVolumeTextures();

        if (dummyVoxelTextureAAScaled.width != DummyVoxelResolution) ResizeDummyTexture();

        if (ConfigData.LightweightMode != _previousLightweightMode)
        {
            OnLightweightModeChanged();
            _previousLightweightMode = ConfigData.LightweightMode;
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

        if (ConfigData.AdaptivePerformance)
        {
            adaptiveVoxelizationFrameCounter++;
            adaptiveSkipVoxelization = (adaptiveVoxelizationFrameCounter % adaptiveVoxelizationInterval) != 0;
        }
        else
        {
            adaptiveSkipVoxelization = false;
        }

        // flip flop has to happen always otherwise stuttering
        activeVolume = (voxelFlipFlop == 0) ? volumeTextures[0] : volumeTextureB;
        previousActiveVolume = (voxelFlipFlop == 0) ? volumeTextureB : volumeTextures[0];
        Shader.SetGlobalTexture("SEGIVolumeLevel0", activeVolume);
        for (int i = 0; i < mipLevels - 1; i++)
        {
            Shader.SetGlobalTexture(MipLevelNames[i], volumeTextures[i + 1]);
        }

        if (adaptiveSkipVoxelization)
        {
            var interval = adaptiveVoxelSpaceSize / 4.0f;
            Vector3 origin;
            if (followTransform)
                origin = followTransform.position;
            else
                origin = transform.position + transform.forward * adaptiveVoxelSpaceSize / 4.0f;

            voxelSpaceOrigin = new Vector3(
                Mathf.Round(origin.x / interval) * interval,
                Mathf.Round(origin.y / interval) * interval,
                Mathf.Round(origin.z / interval) * interval);

            previousVoxelSpaceOrigin = voxelSpaceOrigin;

            RenderTexture.active = previousActive;
            return;
        }

        // Shader.SetGlobalInt("SEGIVoxelAA", ConfigData.VoxelAntiAliasing ? 1 : 0);
        Shader.SetGlobalInt("SEGIVoxelAA", adaptiveVoxelAA ? 1 : 0);

        if (renderState == RenderState.Voxelize)
        {
            // activeVolume =
            //     voxelFlipFlop == 0
            //         ? volumeTextures[0]
            //         : volumeTextureB; //Flip-flopping volume textures to avoid simultaneous read and write errors in shaders
            // previousActiveVolume = voxelFlipFlop == 0 ? volumeTextureB : volumeTextures[0];

            //Setup the voxel volume origin position
            var interval =
                adaptiveVoxelSpaceSize /
                4.0f; //The interval at which the voxel volume will be "locked" in world-space
            Vector3 origin;
            if (followTransform)
                origin = followTransform.position;
            else
                //GI is still flickering a bit when the scene view and the game view are opened at the same time
                origin = transform.position + transform.forward * adaptiveVoxelSpaceSize / 4.0f;

            //Lock the voxel volume origin based on the interval
            voxelSpaceOrigin = new Vector3(Mathf.Round(origin.x / interval) * interval,
                Mathf.Round(origin.y / interval) * interval, Mathf.Round(origin.z / interval) * interval);

            //Calculate how much the voxel origin has moved since last voxelization pass. Used for scrolling voxel data in shaders to avoid ghosting when the voxel volume moves in the world
            voxelSpaceOriginDelta = voxelSpaceOrigin - previousVoxelSpaceOrigin;
            Shader.SetGlobalVector("SEGIVoxelSpaceOriginDelta", voxelSpaceOriginDelta / adaptiveVoxelSpaceSize);
            previousVoxelSpaceOrigin = voxelSpaceOrigin;


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
            voxelCameraGameObject.transform.position =
                voxelSpaceOrigin - Vector3.forward * adaptiveVoxelSpaceSize * 0.5f;
            voxelCameraGameObject.transform.rotation = rotationFront;
            leftViewPoint.transform.position = voxelSpaceOrigin + Vector3.left * adaptiveVoxelSpaceSize * 0.5f;
            leftViewPoint.transform.rotation = rotationLeft;
            topViewPoint.transform.position = voxelSpaceOrigin + Vector3.up * adaptiveVoxelSpaceSize * 0.5f;
            topViewPoint.transform.rotation = rotationTop;

            //Set matrices needed for voxelization
            Shader.SetGlobalMatrix("WorldToCamera", attachedCamera.worldToCameraMatrix);
            Shader.SetGlobalMatrix("SEGIVoxelViewFront",
                TransformViewMatrix(voxelCamera.transform.worldToLocalMatrix));
            Shader.SetGlobalMatrix("SEGIVoxelViewLeft",
                TransformViewMatrix(leftViewPoint.transform.worldToLocalMatrix));
            Shader.SetGlobalMatrix("SEGIVoxelViewTop",
                TransformViewMatrix(topViewPoint.transform.worldToLocalMatrix));
            Shader.SetGlobalMatrix("SEGIWorldToVoxel", voxelCamera.worldToCameraMatrix);
            Shader.SetGlobalMatrix("SEGIVoxelProjection", voxelCamera.projectionMatrix);
            Shader.SetGlobalMatrix("SEGIVoxelProjectionInverse", voxelCamera.projectionMatrix.inverse);
            Shader.SetGlobalInt("SEGIVoxelResolution", adaptiveVoxelResolution);

            var voxelToGIProjection = shadowCamera.projectionMatrix * shadowCamera.worldToCameraMatrix *
                                      voxelCamera.cameraToWorldMatrix;
            Shader.SetGlobalMatrix("SEGIVoxelToGIProjection", voxelToGIProjection);
            { var v = ConfigData.GIGain;
            if (!Mathf.Approximately(v, _cachedGIGain))
            { Shader.SetGlobalFloat("GIGain", v); material.SetFloat("GIGain", v); _cachedGIGain = v; } }

            { var v = ConfigData.SecondaryBounceGain;
            if (!Mathf.Approximately(v, _cachedSecondaryBounceGain))
            { Shader.SetGlobalFloat("SEGISecondaryBounceGain", v); _cachedSecondaryBounceGain = v; } }

            { var v = ConfigData.InnerOcclusionLayers;
            if (v != _cachedInnerOcclusionLayers)
            { Shader.SetGlobalInt("SEGIData.InnerOcclusionLayers", v); _cachedInnerOcclusionLayers = v; } }

            shadowCamera.cullingMask = giCullingMask;
            var sunDirection = sun != null ? -sun.transform.forward : Vector3.down;
            var shadowCamPosition = voxelSpaceOrigin + Vector3.Normalize(sunDirection) *
                adaptiveShadowSpaceSize * 0.5f * shadowSpaceDepthRatio;

            shadowCameraTransform.position = shadowCamPosition;
            shadowCameraTransform.LookAt(voxelSpaceOrigin, Vector3.up);
            shadowCamera.renderingPath = RenderingPath.Forward;
            shadowCamera.depthTextureMode |= DepthTextureMode.None;
            shadowCamera.orthographicSize = adaptiveShadowSpaceSize;
            shadowCamera.farClipPlane = adaptiveShadowSpaceSize * 2.0f * shadowSpaceDepthRatio;

            _currentAmbientBrightness = CalculateAmbientBrightness();
            Shader.SetGlobalVector("SEGISunlightVector",
                sun ? Vector3.Normalize(sun.transform.forward) : Vector3.up);
            Shader.SetGlobalColor("SEGISkyColor",
                new Color(_currentAmbientBrightness, _currentAmbientBrightness, _currentAmbientBrightness, 0.05f));

            var sunAboveHorizon = sun != null && Vector3.Dot(-sun.transform.forward, Vector3.up) > 0f;

            if (sunAboveHorizon)
            {
                Graphics.SetRenderTarget(sunDepthTexture);
                shadowCamera.SetTargetBuffers(sunDepthTexture.colorBuffer, sunDepthTexture.depthBuffer);
                shadowCamera.RenderWithShader(sunDepthShader, "");
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

            clearCompute.SetTexture(0, "RG0", integerVolume);
            clearCompute.SetInt("Res", adaptiveVoxelResolution);
            clearCompute.Dispatch(0, Mathf.CeilToInt((float)adaptiveVoxelResolution / 4f),
                Mathf.CeilToInt((float)adaptiveVoxelResolution / 4f),
                Mathf.CeilToInt((float)adaptiveVoxelResolution / 4f));

            // LogRenderingLayers();

            if (ConfigData.LightweightMode)
            {
                UpdateEmissiveCache();

                Graphics.SetRandomWriteTarget(1, integerVolume);
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
                    voxelCamera.RenderWithShader(voxelizationShader, "");
                    voxelCamera.cullingMask = originalMask;
                foreach (var kvp in _layerRestoreCache)
                    {
                    if (kvp.Key != null)
                                kvp.Key.layer = kvp.Value;
                        }
                    Graphics.ClearRandomWriteTargets();
            }
            else
            {
                Graphics.SetRandomWriteTarget(1, integerVolume);
                voxelCamera.targetTexture = dummyVoxelTextureAAScaled;
                voxelCamera.RenderWithShader(voxelizationShaderBeefEdit, "");
                Graphics.ClearRandomWriteTargets();
            }

            transferIntsCompute.SetTexture(0, "Result", activeVolume);
            transferIntsCompute.SetTexture(0, "PrevResult", previousActiveVolume);
            transferIntsCompute.SetTexture(0, "RG0", integerVolume);
            transferIntsCompute.SetInt("VoxelAA", adaptiveVoxelAA ? 1 : 0);
            transferIntsCompute.SetInt("Resolution", adaptiveVoxelResolution);
            transferIntsCompute.SetVector("VoxelOriginDelta",
                voxelSpaceOriginDelta / adaptiveVoxelSpaceSize * adaptiveVoxelResolution);
            transferIntsCompute.Dispatch(0, Mathf.CeilToInt((float)adaptiveVoxelResolution / 4f),
                Mathf.CeilToInt((float)adaptiveVoxelResolution / 4f),
                Mathf.CeilToInt((float)adaptiveVoxelResolution / 4f));

            //Manually filter/render mip maps
            Shader.SetGlobalTexture("SEGIVolumeLevel0", activeVolume);
            for (var i = 0; i < mipLevels - 1; i++)
            {
                var source = volumeTextures[i];
                if (i == 0) source = activeVolume;

                var destinationRes = adaptiveVoxelResolution / Mathf.RoundToInt(Mathf.Pow(2, i + 1.0f));
                mipFilterCompute.SetInt("destinationRes", destinationRes);
                mipFilterCompute.SetTexture(MipFilterKernel, "Source", source);
                mipFilterCompute.SetTexture(MipFilterKernel, "Destination", volumeTextures[i + 1]);

                // Optimized dispatch - use 8x8 thread groups for better occupancy
                var mipThreadGroups = Mathf.CeilToInt((float)destinationRes / 8.0f);
                mipFilterCompute.Dispatch(MipFilterKernel, mipThreadGroups, mipThreadGroups, mipThreadGroups);
                Shader.SetGlobalTexture(MipLevelNames[i], volumeTextures[i + 1]);
            }

            //Advance the voxel flip flop counter
            voxelFlipFlop += 1;
            voxelFlipFlop %= 2;

            if (ConfigData.InfiniteBounces) renderState = RenderState.Bounce;
        }
        else if (renderState == RenderState.Bounce)
        {
            //Clear the volume texture that is immediately written to in the voxelization scene shader
            clearCompute.SetTexture(0, "RG0", integerVolume);
            var bounceThreadGroups = Mathf.CeilToInt((float)adaptiveVoxelResolution / 4.0f);
            clearCompute.Dispatch(0, bounceThreadGroups, bounceThreadGroups, bounceThreadGroups);

            //Set secondary tracing parameters
            Shader.SetGlobalInt("SEGISecondaryCones", ConfigData.SecondaryCones);
            Shader.SetGlobalFloat("SEGISecondaryOcclusionStrength", ConfigData.SecondaryOcclusionStrength);

            //Render the scene from the voxel camera object with the voxel tracing shader to render a bounce of GI into the irradiance volume
            Graphics.SetRandomWriteTarget(1, integerVolume);
            voxelCamera.targetTexture = dummyVoxelTextureFixed;
            voxelCamera.RenderWithShader(voxelTracingShaderBeefEdit, "");
            Graphics.ClearRandomWriteTargets();

            //Transfer the data from the volume integer texture to the irradiance volume texture. This result is added to the next main voxelization pass to create a feedback loop for infinite bounces
            transferIntsCompute.SetTexture(1, "Result", secondaryIrradianceVolume);
            transferIntsCompute.SetTexture(1, "RG0", integerVolume);
            transferIntsCompute.SetInt("Resolution", adaptiveVoxelResolution);
            transferIntsCompute.Dispatch(1, bounceThreadGroups, bounceThreadGroups, bounceThreadGroups);
            Shader.SetGlobalTexture("SEGIVolumeTexture1", secondaryIrradianceVolume);

            renderState = RenderState.Voxelize;
        }

        RenderTexture.active = previousActive;
    }

    [ImageEffectOpaque]
    private void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        if (notReadyToRender)
        {
            Graphics.Blit(source, destination);
            return;
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
        material.SetFloat("DeltaTime", Time.deltaTime);
        material.SetTexture("NoiseTexture", blueNoise[frameCounter % 64]);
        { var v = ConfigData.ConeLength;
        if (!Mathf.Approximately(v, _cachedTraceLength))
        { material.SetFloat("TraceLength", v); _cachedTraceLength = v; } }

        { var v = ConfigData.ConeWidth;
        if (!Mathf.Approximately(v, _cachedConeWidth))
        { material.SetFloat("ConeSize", v); _cachedConeWidth = v; } }

        { var v = ConfigData.OcclusionStrength;
        if (!Mathf.Approximately(v, _cachedOcclusionStrength))
        { material.SetFloat("OcclusionStrength", v); _cachedOcclusionStrength = v; } }

        { var v = ConfigData.OcclusionPower;
        if (!Mathf.Approximately(v, _cachedOcclusionPower))
        { material.SetFloat("OcclusionPower", v); _cachedOcclusionPower = v; } }

        { var v = ConfigData.ConeTraceBias;
        if (!Mathf.Approximately(v, _cachedConeTraceBias))
        { material.SetFloat("ConeTraceBias", v); _cachedConeTraceBias = v; } }

        { var v = ConfigData.GIGain;
        if (!Mathf.Approximately(v, _cachedGIGain))
        { material.SetFloat("GIGain", v); _cachedGIGain = v; } }

        { var v = ConfigData.NearLightGain;
        if (!Mathf.Approximately(v, _cachedNearLightGain))
        { material.SetFloat("NearLightGain", v); _cachedNearLightGain = v; } }

        { var v = ConfigData.NearOcclusionStrength;
        if (!Mathf.Approximately(v, _cachedNearOcclusionStrength))
        { material.SetFloat("NearOcclusionStrength", v); _cachedNearOcclusionStrength = v; } }

        { var v = ConfigData.FarOcclusionStrength;
        if (!Mathf.Approximately(v, _cachedFarOcclusionStrength))
        { material.SetFloat("FarOcclusionStrength", v); _cachedFarOcclusionStrength = v; } }

        { var v = ConfigData.FarthestOcclusionStrength;
        if (!Mathf.Approximately(v, _cachedFarthestOcclusionStrength))
        { material.SetFloat("FarthestOcclusionStrength", v); _cachedFarthestOcclusionStrength = v; } }

        { var v = ConfigData.TemporalBlendWeight;
        if (!Mathf.Approximately(v, _cachedBlendWeight))
        { material.SetFloat("BlendWeight", v); _cachedBlendWeight = v; } }

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
            return;
        }

        //Setup temporary textures
        var gi1 = RenderTexture.GetTemporary(source.width / giRenderRes, source.height / giRenderRes, 0,
            RenderTextureFormat.ARGBHalf);
        var gi2 = RenderTexture.GetTemporary(source.width / giRenderRes, source.height / giRenderRes, 0,
            RenderTextureFormat.ARGBHalf);

        //Setup textures to hold the current camera depth and normal
        var currentDepth = RenderTexture.GetTemporary(source.width / giRenderRes,
            source.height / giRenderRes, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear);
        currentDepth.filterMode = FilterMode.Point;
        var currentNormal = RenderTexture.GetTemporary(source.width / giRenderRes,
            source.height / giRenderRes, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
        currentNormal.filterMode = FilterMode.Point;

        //Get the camera depth and normals
        Graphics.Blit(source, currentDepth, material, Pass.GetCameraDepthTexture);
        material.SetTexture("CurrentDepth", currentDepth);
        Graphics.Blit(source, currentNormal, material, Pass.GetWorldNormals);
        material.SetTexture("CurrentNormal", currentNormal);

        //Set the previous GI result and camera depth textures to access them in the shader
        material.SetTexture("PreviousGITexture", previousGIResult);
        Shader.SetGlobalTexture("PreviousGITexture", previousGIResult);
        material.SetTexture("PreviousDepth", previousCameraDepth);

        //Render diffuse GI tracing result
        Graphics.Blit(source, gi2, material, Pass.DiffuseTrace);

        //Perform bilateral filtering
        // if (ConfigData.UseBilateralFiltering)
        if (adaptiveBilateralFiltering)
        {
            material.SetVector("Kernel", new Vector2(0.0f, 1.0f));
            Graphics.Blit(gi2, gi1, material, Pass.BilateralBlur);
            material.SetVector("Kernel", new Vector2(1.0f, 0.0f));
            Graphics.Blit(gi1, gi2, material, Pass.BilateralBlur);
            material.SetVector("Kernel", new Vector2(0.0f, 1.0f));
            Graphics.Blit(gi2, gi1, material, Pass.BilateralBlur);
            material.SetVector("Kernel", new Vector2(1.0f, 0.0f));
            Graphics.Blit(gi1, gi2, material, Pass.BilateralBlur);
        }

        //If Half Resolution tracing is enabled
        if (giRenderRes == 2)
        {
            RenderTexture.ReleaseTemporary(gi1);
            //Setup temporary textures
            var gi3 =
                RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGBHalf);
            var gi4 =
                RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGBHalf);

            //Prepare the half-resolution diffuse GI result to be bilaterally upsampled
            gi2.filterMode = FilterMode.Point;
            Graphics.Blit(gi2, gi4);
            RenderTexture.ReleaseTemporary(gi2);
            gi4.filterMode = FilterMode.Point;
            gi3.filterMode = FilterMode.Point;

            //Perform bilateral upsampling on half-resolution diffuse GI result
            material.SetVector("Kernel", new Vector2(1.0f, 0.0f));
            Graphics.Blit(gi4, gi3, material, Pass.BilateralUpsample);
            material.SetVector("Kernel", new Vector2(0.0f, 1.0f));

            //Perform temporal reprojection and blending
            if (ConfigData.TemporalBlendWeight < 1.0f)
            {
                Graphics.Blit(gi3, gi4);
                Graphics.Blit(gi4, gi3, material, Pass.TemporalBlend);
                Graphics.Blit(gi3, previousGIResult);
                Graphics.Blit(source, previousCameraDepth, material, Pass.GetCameraDepthTexture);
            }

            //Set the result to be accessed in the shader
            material.SetTexture("GITexture", gi3);

            //Actually apply the GI to the scene using gbuffer data
            Graphics.Blit(source, destination, material, visualizeGI ? Pass.VisualizeGI : Pass.BlendWithScene);

            //Release temporary textures
            RenderTexture.ReleaseTemporary(gi3);
            RenderTexture.ReleaseTemporary(gi4);
        }
        else //If Half Resolution tracing is disabled
        {
            //Perform temporal reprojection and blending
            if (ConfigData.TemporalBlendWeight < 1.0f)
            {
                Graphics.Blit(gi2, gi1, material, Pass.TemporalBlend);
                Graphics.Blit(gi1, previousGIResult);
                Graphics.Blit(source, previousCameraDepth, material, Pass.GetCameraDepthTexture);
            }

            //Actually apply the GI to the scene using gbuffer data
            material.SetTexture("GITexture", ConfigData.TemporalBlendWeight < 1.0f ? gi1 : gi2);
            Graphics.Blit(source, destination, material, visualizeGI ? Pass.VisualizeGI : Pass.BlendWithScene);

            //Release temporary textures
            RenderTexture.ReleaseTemporary(gi1);
            RenderTexture.ReleaseTemporary(gi2);
        }

        //Release temporary textures
        RenderTexture.ReleaseTemporary(currentDepth);
        RenderTexture.ReleaseTemporary(currentNormal);

        //Visualize the sun depth texture
        if (visualizeSunDepthTexture) Graphics.Blit(sunDepthTexture, destination);


        //Set matrices/vectors for use during temporal reprojection
        material.SetMatrix("ProjectionPrev", attachedCamera.projectionMatrix);
        material.SetMatrix("ProjectionPrevInverse", attachedCamera.projectionMatrix.inverse);
        material.SetMatrix("WorldToCameraPrev", attachedCamera.worldToCameraMatrix);
        material.SetMatrix("CameraToWorldPrev", attachedCamera.cameraToWorldMatrix);
        material.SetVector("CameraPositionPrev", transform.position);

        //Advance the frame counter
        frameCounter = (frameCounter + 1) % 64;
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
        mipFilterCompute = Bundle.LoadAsset<ComputeShader>("SEGIMipFilter");
        voxelizationShader = Bundle.LoadAsset<Shader>("SEGIVoxelizeScene");
        voxelizationShaderBeefEdit = SegiBeefEdit.LoadAsset<Shader>("SEGIVoxelizeSceneBeefEdit");
        voxelTracingShader = SegiBeefEdit.LoadAsset<Shader>("SEGITraceScene");
        voxelTracingShaderBeefEdit = SegiBeefEdit.LoadAsset<Shader>("SEGITraceSceneBeefEdit");
        material = new Material(Bundle.LoadAsset<Shader>("SEGI"))
        {
            hideFlags = HideFlags.HideAndDontSave
        };

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
            shadowCamera.farClipPlane = adaptiveShadowSpaceSize * 2.0f * shadowSpaceDepthRatio;
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

        sunDepthTexture = new RenderTexture(sunShadowResolution, sunShadowResolution, 16, RenderTextureFormat.RHalf,
            RenderTextureReadWrite.Linear)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Point
        };
        sunDepthTexture.Create();
        sunDepthShader.hideFlags = HideFlags.HideAndDontSave;

        renderState = RenderState.Voxelize;
        voxelFlipFlop = 0;
        frameCounter = 0;
        _lastResolutionChangeTime = -999f;
        _previousLightweightMode = false;
        _lastSpatialCullUpdate = 0f;
        _lastEmissiveCacheUpdate = 0f;
        voxelSpaceOrigin = Vector3.zero;
        previousVoxelSpaceOrigin = Vector3.zero;
        voxelSpaceOriginDelta = Vector3.zero;

        frameTimeHistory = new Queue<float>();
        frameTimeSum = 0f;
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
        adaptiveBilateralFiltering = ConfigData.UseBilateralFiltering;

        CreateVolumeTextures();

        Shader.SetGlobalFloat("SEGISoftSunlight", 0);
        Shader.SetGlobalInt("SEGISphericalSkylight", 0);
        material.SetInt("StochasticSampling", 1); // ConfigData.StochasticSampling is hardcoded true
        material.SetInt("DoReflections", 0);
        material.SetFloat("SkyReflectionIntensity", 0.0f);
        material.SetFloat("ReflectionOcclusionPower", 0.0f);
        material.SetInt("ReflectionSteps", 0);
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
        systemSupported.VoxelizationShader = voxelizationShader.isSupported;
        systemSupported.VoxelizationLightShader = voxelizationShader.isSupported;
        systemSupported.TracingShader = voxelTracingShaderBeefEdit.isSupported;

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

    private float GetSunElevationAngle()
    {
        if (sun == null) return -90f;

        var sunForward = -sun.transform.forward;
        var dotProduct = Vector3.Dot(sunForward, Vector3.up);
        return Mathf.Asin(dotProduct) * Mathf.Rad2Deg;
    }

    private float CalculateAmbientBrightness()
    {
        var sunElevation = GetSunElevationAngle();
        var thresholdRange = SunHorizonThresholdMax - SunHorizonThresholdMin;
        var t = Mathf.Clamp01((sunElevation - (-SunHorizonThresholdMin)) / (thresholdRange * 2f));
        // SEGIPlugin.Log.LogInfo($"it says it's now brightness {Mathf.Lerp(ConfigData.NightAmbientBrightness, ConfigData.DayAmbientBrightness, t)}");
        return Mathf.Lerp(ConfigData.NightAmbientBrightness, ConfigData.DayAmbientBrightness, t);
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
            if (material.HasProperty("_EmissionColor"))
            {
                // hasEmissiveProperty = true;
                var emission = material.GetColor("_EmissionColor");
                var emissionIntensity = emission.r + emission.g + emission.b;
                if (emissionIntensity > 0.1f)
                    // hasEmission = true;
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
            for (var i = 0; i < mipLevels; i++)
                if (volumeTextures[i] != null)
                    CleanupTexture(ref volumeTextures[i]);

        volumeTextures = new RenderTexture[mipLevels];
        for (var i = 0; i < mipLevels; i++)
        {
            var resolution = adaptiveVoxelResolution / Mathf.RoundToInt(Mathf.Pow(2, i));
            var format = i >= LowerPrecisionMipThreshold
                ? RenderTextureFormat.ARGB32
                : RenderTextureFormat.ARGBHalf;
            volumeTextures[i] = new RenderTexture(resolution, resolution, 0, format,
                RenderTextureReadWrite.Linear)
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

        if (secondaryIrradianceVolume) CleanupTexture(ref secondaryIrradianceVolume);

        secondaryIrradianceVolume = new RenderTexture(adaptiveVoxelResolution,
            adaptiveVoxelResolution, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
        {
            dimension = TextureDimension.Tex3D,
            volumeDepth = adaptiveVoxelResolution,
            enableRandomWrite = true,
            filterMode = FilterMode.Point,
            autoGenerateMips = false,
            useMipMap = false,
            antiAliasing = 1
        };
        secondaryIrradianceVolume.Create();
        secondaryIrradianceVolume.hideFlags = HideFlags.HideAndDontSave;

        if (integerVolume) CleanupTexture(ref integerVolume);

        integerVolume = new RenderTexture(adaptiveVoxelResolution, adaptiveVoxelResolution, 0,
            RenderTextureFormat.RInt, RenderTextureReadWrite.Linear)
        {
            dimension = TextureDimension.Tex3D,
            volumeDepth = adaptiveVoxelResolution,
            enableRandomWrite = true,
            filterMode = FilterMode.Point,
            hideFlags = HideFlags.HideAndDontSave
        };
        integerVolume.Create();
        integerVolume.hideFlags = HideFlags.HideAndDontSave;

        ResizeDummyTexture();

        var voxelCameraResolution = DummyVoxelResolution;
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
            autoGenerateMips = false
        };
        previousGIResult.Create();
        previousGIResult.hideFlags = HideFlags.HideAndDontSave;

        if (previousCameraDepth) CleanupTexture(ref previousCameraDepth);

        previousCameraDepth =
            new RenderTexture(width, height, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
        previousCameraDepth.Create();
        previousCameraDepth.hideFlags = HideFlags.HideAndDontSave;
    }

    private void ResizeSunShadowBuffer()
    {
        if (sunDepthTexture) CleanupTexture(ref sunDepthTexture);

        sunDepthTexture = new RenderTexture(sunShadowResolution, sunShadowResolution, 16, RenderTextureFormat.RHalf,
            RenderTextureReadWrite.Linear)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Point
        };
        sunDepthTexture.Create();
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

    private void CleanupTextures()
    {
        CleanupTexture(ref sunDepthTexture);
        CleanupTexture(ref previousGIResult);
        CleanupTexture(ref previousCameraDepth);
        CleanupTexture(ref integerVolume);

        if (volumeTextures != null)
            for (var i = 0; i < volumeTextures.Length; i++)
                CleanupTexture(ref volumeTextures[i]);

        CleanupTexture(ref secondaryIrradianceVolume);
        CleanupTexture(ref volumeTextureB);
        CleanupTexture(ref dummyVoxelTextureAAScaled);
        CleanupTexture(ref dummyVoxelTextureFixed);
    }

    private void Cleanup()
    {
        CleanupRobotEmissionFix();

        if (_emissiveCacheCoroutine != null)
        {
            StopCoroutine(_emissiveCacheCoroutine);
            _emissiveCacheCoroutine = null;
        }
        try
        {
            Shader.SetGlobalTexture("SEGIVolumeLevel0", null);
            for (int i = 0; i < mipLevels - 1; i++)
            {
                Shader.SetGlobalTexture(MipLevelNames[i], null);
            }
            Shader.SetGlobalTexture("SEGIVolumeTexture1", null);
            Shader.SetGlobalTexture("SEGISunDepth", null);
            Shader.SetGlobalTexture("PreviousGITexture", null);
        }
        catch (Exception ex)
        {
            SEGIPlugin.Log.LogError($"Error clearing: {ex.Message}");
        }

        CleanupCaches();
        DestroyImmediate(material);

        DestroyImmediate(voxelCameraGameObject);
        DestroyImmediate(leftViewPoint);
        DestroyImmediate(topViewPoint);
        DestroyImmediate(shadowCameraGameObject);

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
        if (frameTimeHistory.Count >= 30) //30frame avg
            frameTimeSum -= frameTimeHistory.Dequeue();

        frameTimeHistory.Enqueue(currentFrameTime);
        frameTimeSum += currentFrameTime;
        frameTimeAverage = frameTimeSum / frameTimeHistory.Count;

        adaptiveLongTermTimer += Time.unscaledDeltaTime;
        float targetFrameTime = TargetFrameTime;
        float frameTimeRatio = frameTimeAverage / targetFrameTime;
        adaptiveLongTermAcc += frameTimeRatio;
    }

    private void UpdateAdaptivePerformance()
    {
        if (!ConfigData.AdaptivePerformance)
        {
            adaptiveVoxelResolution = adaptiveMaxVoxelRes;
            adaptiveVoxelSpaceSize = adaptiveMaxVoxelSpaceSize;
            adaptiveShadowSpaceSize = adaptiveMaxShadowSpaceSize;
            adaptiveCones = adaptiveMaxCones;
            adaptiveConeTraceSteps = adaptiveMaxConeTraceSteps;
            adaptiveHalfResolution = ConfigData.HalfResolution;
            adaptiveVoxelAA = ConfigData.VoxelAntiAliasing;
            adaptiveBilateralFiltering = ConfigData.UseBilateralFiltering;
            adaptiveVoxelizationInterval = 1;
            adaptiveLongTermAcc = 0f;
            adaptiveLongTermTimer = 0f;
            return;
        }

        // float scaleDownThreshold = ConfigData.AdaptiveScaleDownThreshold;
        // float scaleUpThreshold = ConfigData.AdaptiveScaleUpThreshold;

        float scaleDownThreshold = ConfigData.AdaptiveScaleDownThreshold / performanceMarginMultiplier;
        float scaleUpThreshold = ConfigData.AdaptiveScaleUpThreshold / performanceMarginMultiplier;

        float targetFrameTime = TargetFrameTime;
        float frameTimeRatio = frameTimeAverage / targetFrameTime;
        bool inResolutionCooldown = Time.unscaledTime - _lastResolutionChangeTime < ResolutionChangeCooldown;

        float scaleDelta = 0f;
        if (!inResolutionCooldown)
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

        int adaptiveStrategy = ConfigData.AdaptiveStrategy;

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

        if (Mathf.Abs(targetVoxelSpaceSize - adaptiveVoxelSpaceSize) >= 1.0f)
        {
            adaptiveVoxelSpaceSize = targetVoxelSpaceSize;
            adaptiveShadowSpaceSize = targetVoxelSpaceSize * 0.75f;
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
            && Time.unscaledTime - _lastResolutionChangeTime >= ResolutionChangeCooldown)
        {
            adaptiveVoxelResolution = newVoxelRes;
            _lastResolutionChangeTime = Time.unscaledTime;
            frameTimeHistory.Clear();
            frameTimeSum = 0f;
            frameTimeAverage = 0.016f;
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
            adaptiveBilateralFiltering = ConfigData.AdaptiveMaxBilateralFiltering;
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

            float bilateralOffThreshold = ConfigData.GetAdaptiveBilateralOffThreshold(adaptiveStrategy);
            float bilateralOnThreshold = ConfigData.GetAdaptiveBilateralOnThreshold(adaptiveStrategy);

            if (currentAdaptiveScale < bilateralOffThreshold)
                adaptiveBilateralFiltering = false;
            else if (currentAdaptiveScale >= bilateralOnThreshold)
                adaptiveBilateralFiltering = ConfigData.AdaptiveMaxBilateralFiltering;
        }

        float interval3Threshold = ConfigData.GetAdaptiveVoxelInterval3Threshold(adaptiveStrategy);
        float interval2Threshold = ConfigData.GetAdaptiveVoxelInterval2Threshold(adaptiveStrategy);
        float interval1Threshold = ConfigData.GetAdaptiveVoxelInterval1Threshold(adaptiveStrategy);

        if (currentAdaptiveScale < interval3Threshold)
            adaptiveVoxelizationInterval = 3;
        else if (currentAdaptiveScale < interval2Threshold)
            adaptiveVoxelizationInterval = 2;
        else if (currentAdaptiveScale > interval1Threshold)
            adaptiveVoxelizationInterval = 1;

        // float currentFPS = 1.0f / frameTimeAverage;
        // SEGIPlugin.Log.LogInfo(
        //     $"FPS={currentFPS:F1} Target={ConfigData.TargetFramerate} Scale={currentAdaptiveScale:F2} " +
        //     $"VoxelRes={adaptiveVoxelResolution} Cones={adaptiveCones} Steps={adaptiveConeTraceSteps} " +
        //     $"HalfRes={adaptiveHalfResolution} VoxelAA={adaptiveVoxelAA} Filtering={adaptiveBilateralFiltering} " +
        //     $"Interval={adaptiveVoxelizationInterval}");
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