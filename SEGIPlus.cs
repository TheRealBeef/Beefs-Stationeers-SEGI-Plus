using UnityEngine;
using UnityEngine.Rendering;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Linq;


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
    private float _lastSpatialCullUpdate = 0f;
    private float _lastEmissiveCacheUpdate = 0f;
    private Coroutine _emissiveCacheCoroutine;

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
    private int DummyVoxelResolution => (int)ConfigData.VoxelResolution * (ConfigData.VoxelAntiAliasing ? 2 : 1);
    private int GIRenderRes => ConfigData.HalfResolution ? 2 : 1;

    private void Start()
    {
        InitCheck();
    }

    private void OnEnable()
    {
        InitCheck();
        ResizeRenderTextures();
        CheckSupport();
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
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

        if (volumeTextures[0].width != (int)ConfigData.VoxelResolution) CreateVolumeTextures();

        if (dummyVoxelTextureAAScaled.width != DummyVoxelResolution) ResizeDummyTexture();

        if (ConfigData.LightweightMode != _previousLightweightMode)
        {
            OnLightweightModeChanged();
            _previousLightweightMode = ConfigData.LightweightMode;
        }
    }

    private void OnPreRender()
    {
        if (!voxelCamera || !shadowCamera) initalized = false;

        InitCheck();

        if (notReadyToRender) return;

        if (!updateGI) return;

        var previousActive = RenderTexture.active;

        Shader.SetGlobalInt("SEGIVoxelAA", ConfigData.VoxelAntiAliasing ? 1 : 0);

        //Main voxelization work
        if (renderState == RenderState.Voxelize)
        {
            activeVolume =
                voxelFlipFlop == 0
                    ? volumeTextures[0]
                    : volumeTextureB; //Flip-flopping volume textures to avoid simultaneous read and write errors in shaders
            previousActiveVolume = voxelFlipFlop == 0 ? volumeTextureB : volumeTextures[0];

            //Setup the voxel volume origin position
            var interval =
                ConfigData.VoxelSpaceSize /
                4.0f; //The interval at which the voxel volume will be "locked" in world-space
            Vector3 origin;
            if (followTransform)
                origin = followTransform.position;
            else
                //GI is still flickering a bit when the scene view and the game view are opened at the same time
                origin = transform.position + transform.forward * ConfigData.VoxelSpaceSize / 4.0f;

            //Lock the voxel volume origin based on the interval
            voxelSpaceOrigin = new Vector3(Mathf.Round(origin.x / interval) * interval,
                Mathf.Round(origin.y / interval) * interval, Mathf.Round(origin.z / interval) * interval);

            //Calculate how much the voxel origin has moved since last voxelization pass. Used for scrolling voxel data in shaders to avoid ghosting when the voxel volume moves in the world
            voxelSpaceOriginDelta = voxelSpaceOrigin - previousVoxelSpaceOrigin;
            Shader.SetGlobalVector("SEGIVoxelSpaceOriginDelta", voxelSpaceOriginDelta / ConfigData.VoxelSpaceSize);
            previousVoxelSpaceOrigin = voxelSpaceOrigin;


            //Set the voxel camera (proxy camera used to render the scene for voxelization) parameters
            voxelCamera.enabled = false;
            voxelCamera.orthographic = true;
            voxelCamera.orthographicSize = ConfigData.VoxelSpaceSize * 0.5f;
            voxelCamera.nearClipPlane = 0.0f;
            voxelCamera.farClipPlane = ConfigData.VoxelSpaceSize;
            voxelCamera.depth = -2;
            voxelCamera.renderingPath = RenderingPath.Forward;
            voxelCamera.clearFlags = CameraClearFlags.Color;
            voxelCamera.backgroundColor = Color.black;
            voxelCamera.cullingMask = giCullingMask;

            //Move the voxel camera game object and other related objects to the above calculated voxel space origin
            voxelCameraGameObject.transform.position =
                voxelSpaceOrigin - Vector3.forward * ConfigData.VoxelSpaceSize * 0.5f;
            voxelCameraGameObject.transform.rotation = rotationFront;
            leftViewPoint.transform.position = voxelSpaceOrigin + Vector3.left * ConfigData.VoxelSpaceSize * 0.5f;
            leftViewPoint.transform.rotation = rotationLeft;
            topViewPoint.transform.position = voxelSpaceOrigin + Vector3.up * ConfigData.VoxelSpaceSize * 0.5f;
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
            Shader.SetGlobalInt("SEGIVoxelResolution", (int)ConfigData.VoxelResolution);
            var voxelToGIProjection = shadowCamera.projectionMatrix * shadowCamera.worldToCameraMatrix *
                                      voxelCamera.cameraToWorldMatrix;
            Shader.SetGlobalMatrix("SEGIVoxelToGIProjection", voxelToGIProjection);
            Shader.SetGlobalFloat("SEGISoftSunlight", 0);
            Shader.SetGlobalFloat("GIGain", ConfigData.GIGain);
            Shader.SetGlobalFloat("SEGISecondaryBounceGain", ConfigData.SecondaryBounceGain);
            Shader.SetGlobalInt("SEGIData.InnerOcclusionLayers", ConfigData.InnerOcclusionLayers);
            Shader.SetGlobalInt("SEGISphericalSkylight", 0);

            shadowCamera.cullingMask = giCullingMask;
            var sunDirection = sun != null ? -sun.transform.forward : Vector3.down;
            var shadowCamPosition = voxelSpaceOrigin + Vector3.Normalize(sunDirection) *
                ConfigData.ShadowSpaceSize * 0.5f * shadowSpaceDepthRatio;

            shadowCameraTransform.position = shadowCamPosition;
            shadowCameraTransform.LookAt(voxelSpaceOrigin, Vector3.up);
            shadowCamera.renderingPath = RenderingPath.Forward;
            shadowCamera.depthTextureMode |= DepthTextureMode.None;
            shadowCamera.orthographicSize = ConfigData.ShadowSpaceSize;
            shadowCamera.farClipPlane = ConfigData.ShadowSpaceSize * 2.0f * shadowSpaceDepthRatio;

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
            clearCompute.SetInt("Res", (int)ConfigData.VoxelResolution);
            clearCompute.Dispatch(0, (int)ConfigData.VoxelResolution / 16, (int)ConfigData.VoxelResolution / 16, 1);

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
            transferIntsCompute.SetInt("VoxelAA", ConfigData.VoxelAntiAliasing ? 1 : 0);
            transferIntsCompute.SetInt("Resolution", (int)ConfigData.VoxelResolution);
            transferIntsCompute.SetVector("VoxelOriginDelta",
                voxelSpaceOriginDelta / ConfigData.VoxelSpaceSize * (int)ConfigData.VoxelResolution);
            transferIntsCompute.Dispatch(0, (int)ConfigData.VoxelResolution / 16,
                (int)ConfigData.VoxelResolution / 16, 1);

            //Manually filter/render mip maps
            Shader.SetGlobalTexture("SEGIVolumeLevel0", activeVolume);
            for (var i = 0; i < mipLevels - 1; i++)
            {
                var source = volumeTextures[i];
                if (i == 0) source = activeVolume;

                var destinationRes = (int)ConfigData.VoxelResolution / Mathf.RoundToInt(Mathf.Pow(2, i + 1.0f));
                mipFilterCompute.SetInt("destinationRes", destinationRes);
                mipFilterCompute.SetTexture(MipFilterKernel, "Source", source);
                mipFilterCompute.SetTexture(MipFilterKernel, "Destination", volumeTextures[i + 1]);

                // Optimized dispatch - use 8x8 thread groups for better occupancy
                var mipThreadGroups = Mathf.CeilToInt((float)destinationRes / 8.0f);
                mipFilterCompute.Dispatch(MipFilterKernel, mipThreadGroups, mipThreadGroups, mipThreadGroups);
                Shader.SetGlobalTexture("SEGIVolumeLevel" + (i + 1).ToString(), volumeTextures[i + 1]);
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
            var bounceThreadGroups = Mathf.CeilToInt((float)ConfigData.VoxelResolution / 8.0f);
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
            transferIntsCompute.SetInt("Resolution", (int)ConfigData.VoxelResolution);
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
        Shader.SetGlobalInt("SEGIFrameSwitch", frameCounter);
        Shader.SetGlobalFloat("SEGIVoxelScaleFactor", VoxelScaleFactor);
        material.SetMatrix("CameraToWorld", attachedCamera.cameraToWorldMatrix);
        material.SetMatrix("WorldToCamera", attachedCamera.worldToCameraMatrix);
        material.SetMatrix("ProjectionMatrixInverse", attachedCamera.projectionMatrix.inverse);
        material.SetMatrix("ProjectionMatrix", attachedCamera.projectionMatrix);
        material.SetInt("FrameSwitch", frameCounter);
        material.SetVector("CameraPosition", transform.position);
        material.SetFloat("DeltaTime", Time.deltaTime);
        material.SetInt("StochasticSampling", ConfigData.StochasticSampling ? 1 : 0);
        material.SetInt("TraceDirections", ConfigData.Cones);
        material.SetInt("TraceSteps", ConfigData.ConeTraceSteps);
        material.SetFloat("TraceLength", ConfigData.ConeLength);
        material.SetFloat("ConeSize", ConfigData.ConeWidth);
        material.SetFloat("OcclusionStrength", ConfigData.OcclusionStrength);
        material.SetFloat("OcclusionPower", ConfigData.OcclusionPower);
        material.SetFloat("ConeTraceBias", ConfigData.ConeTraceBias);
        material.SetFloat("GIGain", ConfigData.GIGain);
        material.SetFloat("NearLightGain", ConfigData.NearLightGain);
        material.SetFloat("NearOcclusionStrength", ConfigData.NearOcclusionStrength);
        material.SetInt("HalfResolution", ConfigData.HalfResolution ? 1 : 0);
        material.SetFloat("FarOcclusionStrength", ConfigData.FarOcclusionStrength);
        material.SetFloat("FarthestOcclusionStrength", ConfigData.FarthestOcclusionStrength);
        material.SetTexture("NoiseTexture", blueNoise[frameCounter % 64]);
        material.SetFloat("BlendWeight", ConfigData.TemporalBlendWeight);

        //Disabled
        material.SetInt("DoReflections", 0);
        material.SetFloat("SkyReflectionIntensity", 0.0f);
        material.SetFloat("ReflectionOcclusionPower", 0.0f);
        material.SetInt("ReflectionSteps", 0);

        //If Visualize Voxels is enabled, just render the voxel visualization shader pass and return
        if (visualizeVoxels)
        {
            Graphics.Blit(source, destination, material, Pass.VisualizeVoxels);
            return;
        }

        //Setup temporary textures
        var gi1 = RenderTexture.GetTemporary(source.width / GIRenderRes, source.height / GIRenderRes, 0,
            RenderTextureFormat.ARGBHalf);
        var gi2 = RenderTexture.GetTemporary(source.width / GIRenderRes, source.height / GIRenderRes, 0,
            RenderTextureFormat.ARGBHalf);

        //Setup textures to hold the current camera depth and normal
        var currentDepth = RenderTexture.GetTemporary(source.width / GIRenderRes,
            source.height / GIRenderRes, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear);
        currentDepth.filterMode = FilterMode.Point;
        var currentNormal = RenderTexture.GetTemporary(source.width / GIRenderRes,
            source.height / GIRenderRes, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
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
        if (ConfigData.UseBilateralFiltering)
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
        if (GIRenderRes == 2)
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
            new Vector3(ConfigData.VoxelSpaceSize, ConfigData.VoxelSpaceSize, ConfigData.VoxelSpaceSize));
        Gizmos.color = new Color(1.0f, 0.0f, 0.0f, 0.1f);
        Gizmos.color = prevColor;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
    }

 private void InitCheck()
    {
        if (initalized) return;

        Init();
    }

    private void Init()
    {
        sunDepthShader = Bundle.LoadAsset<Shader>("SEGIRenderSunDepth");
        clearCompute = Bundle.LoadAsset<ComputeShader>("SEGIClear");
        transferIntsCompute = Bundle.LoadAsset<ComputeShader>("SEGITransferInts");
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
            shadowCamera.enabled = false;
            shadowCamera.depth = attachedCamera.depth - 1;
            shadowCamera.orthographic = true;
            shadowCamera.orthographicSize = ConfigData.ShadowSpaceSize;
            shadowCamera.clearFlags = CameraClearFlags.SolidColor;
            shadowCamera.backgroundColor = new Color(0.0f, 0.0f, 0.0f, 1.0f);
            shadowCamera.farClipPlane = ConfigData.ShadowSpaceSize * 2.0f * shadowSpaceDepthRatio;
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
            voxelCamera.orthographicSize = ConfigData.VoxelSpaceSize * 0.5f;
            voxelCamera.nearClipPlane = 0.0f;
            voxelCamera.farClipPlane = ConfigData.VoxelSpaceSize;
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

        CreateVolumeTextures();

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
        var newCache = new List<Renderer>();
        var frameStartTime = Time.realtimeSinceStartup * 1000f;
        var allRenderers = FindObjectsOfType<Renderer>();
        const int BATCH_SIZE = 100;
        for (var i = 0; i < allRenderers.Length; i += BATCH_SIZE)
        {
            var endIndex = Mathf.Min(i + BATCH_SIZE, allRenderers.Length);

            for (var j = i; j < endIndex; j++)
            {
                var r = allRenderers[j];
                if (IsRendererEmissive(r)) newCache.Add(r);
            }

            if (Time.realtimeSinceStartup * 1000f - frameStartTime > 0.2f)
            {
                yield return null;
                frameStartTime = Time.realtimeSinceStartup * 1000f;
            }
        }
        _cachedEmissiveRenderers.Clear();
        _cachedEmissiveRenderers.AddRange(newCache);
        _culledEmissiveRenderers.Clear();
        _lastSpatialCullUpdate = 0f;
        _lastEmissiveCacheUpdate = Time.time;
        _emissiveCacheCoroutine = null;
        // SEGIPlugin.Log.LogInfo($"Updated emissive cache: {newCache.Count} renderers");
    }

    private List<Renderer> GetEmissiveRenderers()
    {
        if (Time.time - _lastSpatialCullUpdate < SpatialCullUpdateInterval) return _culledEmissiveRenderers;
        _culledEmissiveRenderers.Clear();
        if (_culledEmissiveRenderers.Capacity < 500)
            _culledEmissiveRenderers.Capacity = 500;
        var halfSize = ConfigData.VoxelSpaceSize * 0.75f;
        var voxelMin = voxelSpaceOrigin - Vector3.one * halfSize;
        var voxelMax = voxelSpaceOrigin + Vector3.one * halfSize;
        for (var i = 0; i < _cachedEmissiveRenderers.Count; i++)
        {
            var r = _cachedEmissiveRenderers[i];
            if (r == null) continue;
            var bounds = r.bounds;
            if (bounds.max.x >= voxelMin.x && bounds.min.x <= voxelMax.x &&
                bounds.max.y >= voxelMin.y && bounds.min.y <= voxelMax.y &&
                bounds.max.z >= voxelMin.z && bounds.min.z <= voxelMax.z)
                _culledEmissiveRenderers.Add(r);
        }
        _lastSpatialCullUpdate = Time.time;
        return _culledEmissiveRenderers;
    }

    private bool IsRendererEmissive(Renderer renderer)
    {
        if (renderer?.materials == null)
            // LogExcludedRenderer(renderer, "null materials");
            return false;

        if (IncludedObjectNames.Contains(renderer.gameObject.name)) return true;
        if (ExcludedObjectNames.Contains(renderer.gameObject.name))
            // LogExcludedRenderer(renderer, "blacklisted object name");
            return false;
        // bool hasEmissiveProperty = false;
        // bool hasEmission = false;
        foreach (var material in renderer.materials)
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

private void CreateVolumeTextures()
    {
        if (volumeTextures != null)
            for (var i = 0; i < mipLevels; i++)
                if (volumeTextures[i] != null)
                    CleanupTexture(ref volumeTextures[i]);

        volumeTextures = new RenderTexture[mipLevels];
        for (var i = 0; i < mipLevels; i++)
        {
            var resolution = (int)ConfigData.VoxelResolution / Mathf.RoundToInt(Mathf.Pow(2, i));
            volumeTextures[i] = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.ARGBHalf,
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

        volumeTextureB = new RenderTexture((int)ConfigData.VoxelResolution, (int)ConfigData.VoxelResolution, 0,
            RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
        {
            dimension = TextureDimension.Tex3D,
            volumeDepth = (int)ConfigData.VoxelResolution,
            enableRandomWrite = true,
            filterMode = FilterMode.Bilinear,
            autoGenerateMips = false,
            useMipMap = false
        };
        volumeTextureB.Create();
        volumeTextureB.hideFlags = HideFlags.HideAndDontSave;

        if (secondaryIrradianceVolume) CleanupTexture(ref secondaryIrradianceVolume);

        secondaryIrradianceVolume = new RenderTexture((int)ConfigData.VoxelResolution,
            (int)ConfigData.VoxelResolution, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
        {
            dimension = TextureDimension.Tex3D,
            volumeDepth = (int)ConfigData.VoxelResolution,
            enableRandomWrite = true,
            filterMode = FilterMode.Point,
            autoGenerateMips = false,
            useMipMap = false,
            antiAliasing = 1
        };
        secondaryIrradianceVolume.Create();
        secondaryIrradianceVolume.hideFlags = HideFlags.HideAndDontSave;

        if (integerVolume) CleanupTexture(ref integerVolume);

        integerVolume = new RenderTexture((int)ConfigData.VoxelResolution, (int)ConfigData.VoxelResolution, 0,
            RenderTextureFormat.RInt, RenderTextureReadWrite.Linear)
        {
            dimension = TextureDimension.Tex3D,
            volumeDepth = (int)ConfigData.VoxelResolution,
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

        dummyVoxelTextureFixed = new RenderTexture((int)ConfigData.VoxelResolution, (int)ConfigData.VoxelResolution,
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
        if (_emissiveCacheCoroutine != null)
        {
            StopCoroutine(_emissiveCacheCoroutine);
            _emissiveCacheCoroutine = null;
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
        foreach (var kvp in _layerRestoreCache)
        {
            if (kvp.Key != null)
                kvp.Key.layer = kvp.Value;
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
        string materialNames = string.Join(", ", renderer.materials?.Where(m => m != null).Select(m => m.name) ?? new string[0]);
        string logKey = $"{objectName}|{materialNames}|{reason}";
        if (LoggedExclusions.Add(logKey))
        {
            SEGIPlugin.Log.LogInfo($"EXCLUDED: '{objectName}' materials:[{materialNames}] reason:{reason}");
        }
    }
}