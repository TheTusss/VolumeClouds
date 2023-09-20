using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace VolumetricClouds {
    [Serializable]
    internal class VolumetricCloudsSettings {
        // Bounds
        [SerializeField] internal string CloudsBoxObjectName;

        // Texture
        [SerializeField] internal Texture3D CloudsFBMTexture;
        [SerializeField] internal Texture3D CloudsDetailNoiseTexture;
        [SerializeField] internal Texture2D WeatherMap;
        [SerializeField] internal Texture2D MaskNoiseTexture;

        // Sun
        [SerializeField] internal Color SunLightColor = Color.white;
        [SerializeField] internal float SunLightIntensity = 1.0f;

        // Clouds Shape
        [SerializeField] internal float CloudsShapeTiling = 100.0f;

        [SerializeField] internal Vector4 CloudsFBMWeights;

        [SerializeField] [Range(0.0f, 1.0f)] internal float HeightWeight = 0.5f;
        [SerializeField] [Range(0.0f, 3.0f)] internal float NoiseStrength = 0.1f;
        [SerializeField] [Range(0.01f, 1.0f)] internal float EdgeFadeProportion = 0.1f;

        [SerializeField] internal float CloudsDetailTiling = 100.0f;

        [SerializeField] internal float DetailWeight = 1.0f;
        [SerializeField] internal float DetailNoiseWeight = 1.0f;
        [SerializeField] [Range(0.0f, 1.0f)] internal float DetailNoiseStrength = 0.1f;

        // Speed
        [SerializeField] internal Vector4 SpeedParams;

        // Clouds Lighting
        [SerializeField] internal Color ColorA = Color.white;
        [SerializeField] internal Color ColorB = Color.white;
        [SerializeField] [Range(0.0f, 1.0f)] internal float LightAbsorptionTowardSun = 0.1f;
        [SerializeField] [Range(0.0f, 1.0f)] internal float LightAbsorptionThroughClouds = 0.25f;
        [SerializeField] [Range(0.0f, 1.0f)] internal float ColorOffset1 = 0.86f;
        [SerializeField] [Range(0.0f, 1.0f)] internal float ColorOffset2 = 0.82f;
        [SerializeField] [Range(0.0f, 1.0f)] internal float DarknessThreshold = 0.6f;

        [SerializeField] internal int StepCount = 32;
        [SerializeField] internal float StepStride = 0.5f;
        [SerializeField] internal int LightStepCount = 8;

        // Density
        [SerializeField] [Range(-3.0f, 3.0f)] internal float DensityOffset = 0.0f;
        [SerializeField] internal float DensityMultiplier = 5.0f;

        // Phase
        [SerializeField] internal Color Albedo = Color.white;
        [SerializeField] [Range(0.0f, 1.0f)] internal float ForwardPhaseG = 0.8f;
        [SerializeField] [Range(0.0f, 1.0f)] internal float BackPhaseG = 0.3f;
        [SerializeField] [Range(0.0f, 1.0f)] internal float PhaseBlend = 0.5f;

        internal Transform CloudsBox;
    }


    [DisallowMultipleRendererFeature("Volumetric Clouds")]
    public class VolumetricClouds : ScriptableRendererFeature {
        [SerializeField] private VolumetricCloudsSettings mSettings = new VolumetricCloudsSettings();

        private Shader mShader;
        private const string mShaderName = "Hidden/Atmosphere/VolumetricClouds";

        private VolumetricCloudsPass mVolumetricCloudsPass;
        private Material mMaterial;

        public override void Create() {
            if (mVolumetricCloudsPass == null) {
                mVolumetricCloudsPass = new VolumetricCloudsPass();
                // 修改注入点
                mVolumetricCloudsPass.renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
            }
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData) {
            if (renderingData.cameraData.postProcessEnabled) {
                if (!GetMaterials()) {
                    Debug.LogErrorFormat("{0}.AddRenderPasses(): Missing material. {1} render pass will not be added.", GetType().Name, name);
                    return;
                }

                if (!GetCloudsBoxTransform()) {
                    Debug.LogErrorFormat("{0}.AddRenderPasses(): Missing clouds box transform. {1} render pass will not be added.", GetType().Name, name);
                    return;
                }

                bool shouldAdd = mVolumetricCloudsPass.Setup(ref mSettings, ref mMaterial);

                if (shouldAdd)
                    renderer.EnqueuePass(mVolumetricCloudsPass);
            }
        }

        protected override void Dispose(bool disposing) {
            CoreUtils.Destroy(mMaterial);

            mVolumetricCloudsPass?.Dispose();
            mVolumetricCloudsPass = null;
        }

        private bool GetCloudsBoxTransform() {
            if (mSettings.CloudsBox != null) return true;

            GameObject obj = null;

            // Load CloudsBox Transforms
            if (mSettings.CloudsBox == null) {
                obj = GameObject.Find(mSettings.CloudsBoxObjectName);
                if (obj != null) mSettings.CloudsBox = obj.transform;
            }

            return obj != null;
        }

        private bool GetMaterials() {
            if (mShader == null)
                mShader = Shader.Find(mShaderName);
            if (mMaterial == null && mShader != null)
                mMaterial = CoreUtils.CreateEngineMaterial(mShader);
            return mMaterial != null;
        }

        class VolumetricCloudsPass : ScriptableRenderPass {
            private VolumetricCloudsSettings mSettings;

            private Material mMaterial;

            private ProfilingSampler mProfilingSampler = new ProfilingSampler("Volumetric Clouds");

            private RTHandle mSourceTexture;
            private RTHandle mDestinationTexture;

            // -----------------------------------------------------------------------------------
            // ID

            // 重建世界坐标相关ID
            private static readonly int mCameraViewTopLeftCornerID = Shader.PropertyToID("_CameraViewTopLeftCorner"),
                mCameraViewXExtentID = Shader.PropertyToID("_CameraViewXExtent"),
                mCameraViewYExtentID = Shader.PropertyToID("_CameraViewYExtent"),
                mProjectionParams2ID = Shader.PropertyToID("_ProjectionParams2");

            // CloudBox相关ID
            private static readonly int mBoundsMinID = Shader.PropertyToID("_CloudsBoundsMin"),
                mBoundsMaxID = Shader.PropertyToID("_CloudsBoundsMax");

            // Texture
            private static readonly int mCloudsFBMTexture = Shader.PropertyToID("_CloudsFBMTexture"),
                mCloudsDetailNoiseTextureID = Shader.PropertyToID("_CloudsDetailNoiseTexture"),
                mWeatherTextureID = Shader.PropertyToID("_WeatherTexture"),
                mMaskNoiseTextureID = Shader.PropertyToID("_MaskNoiseTexture");

            // Clouds Shape
            private static readonly int mCloudsShapeParamsID = Shader.PropertyToID("_CloudsShapeParams"),
                mCloudsShapeParams2ID = Shader.PropertyToID("_CloudsShapeParams2"),
                mCloudsFBMWeightsID = Shader.PropertyToID("_CloudsFBMWeights");

            // Speed
            private static readonly int mSpeedParamsID = Shader.PropertyToID("_SpeedParams");

            // Clouds Color
            private static readonly int mColorAID = Shader.PropertyToID("_ColorA"),
                mColorBID = Shader.PropertyToID("_ColorB"),
                mColorOffsetsID = Shader.PropertyToID("_ColorOffsets"),
                mLightAbsorptionTowardSunID = Shader.PropertyToID("_LightAbsorptionTowardSun"),
                mLightAbsorptionThroughCloudsID = Shader.PropertyToID("_LightAbsorptionThroughClouds"),
                mDarknessThresholdID = Shader.PropertyToID("_DarknessThreshold");

            // Step Params
            private static readonly int mStepParamsID = Shader.PropertyToID("_StepParams");

            // Density Params
            private static readonly int mDensityParamsID = Shader.PropertyToID("_DensityParams");

            // Sun
            private static readonly int mSunLightColorID = Shader.PropertyToID("_SunLightColor"),
                mSunLightIntensityID = Shader.PropertyToID("_SunLightIntensity");

            // Phase
            private static readonly int mAlbedoID = Shader.PropertyToID("_Albedo");
            private static readonly int mPhaseParamsID = Shader.PropertyToID("_PhaseParams");


            // -----------------------------------------------------------------------------------
            // RenderTexture

            private const string mVolumetricCloudsTextureName = "_VolumetricCloudsTexture";
            private RTHandle mVolumetricCloudsTexture;
            private RenderTextureDescriptor mVolumetricCloudsTextureDescriptor;


            internal VolumetricCloudsPass() {
                mSettings = new VolumetricCloudsSettings();
            }

            internal bool Setup(ref VolumetricCloudsSettings featureSettings, ref Material material) {
                mMaterial = material;
                mSettings = featureSettings;

                ConfigureInput(ScriptableRenderPassInput.Normal);

                return mMaterial != null;
            }

            public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData) {
                var renderer = renderingData.cameraData.renderer;

                // -----------------------------------------------------------------------------------
                // 设置Material属性

                // -------------------------
                // 重建世界坐标相关
                Matrix4x4 view = renderingData.cameraData.GetViewMatrix();
                Matrix4x4 proj = renderingData.cameraData.GetProjectionMatrix();
                Matrix4x4 vp = proj * view;

                // 将camera view space 的平移置为0，用来计算world space下相对于相机的vector  
                Matrix4x4 cview = view;
                cview.SetColumn(3, new Vector4(0.0f, 0.0f, 0.0f, 1.0f));
                Matrix4x4 cviewProj = proj * cview;

                // 计算viewProj逆矩阵，即从裁剪空间变换到世界空间  
                Matrix4x4 cviewProjInv = cviewProj.inverse;

                // 计算世界空间下，近平面四个角的坐标  
                var near = renderingData.cameraData.camera.nearClipPlane;
                Vector4 topLeftCorner = cviewProjInv.MultiplyPoint(new Vector4(-1.0f, 1.0f, -1.0f, 1.0f));
                Vector4 topRightCorner = cviewProjInv.MultiplyPoint(new Vector4(1.0f, 1.0f, -1.0f, 1.0f));
                Vector4 bottomLeftCorner = cviewProjInv.MultiplyPoint(new Vector4(-1.0f, -1.0f, -1.0f, 1.0f));

                // 计算相机近平面上方向向量  
                Vector4 cameraXExtent = topRightCorner - topLeftCorner;
                Vector4 cameraYExtent = bottomLeftCorner - topLeftCorner;

                near = renderingData.cameraData.camera.nearClipPlane;

                mMaterial.SetVector(mCameraViewTopLeftCornerID, topLeftCorner);
                mMaterial.SetVector(mCameraViewXExtentID, cameraXExtent);
                mMaterial.SetVector(mCameraViewYExtentID, cameraYExtent);
                mMaterial.SetVector(mProjectionParams2ID, new Vector4(1.0f / near, renderingData.cameraData.worldSpaceCameraPos.x, renderingData.cameraData.worldSpaceCameraPos.y, renderingData.cameraData.worldSpaceCameraPos.z));

                // -------------------------
                // CloudsBounds
                var boundsOppositePosition = mSettings.CloudsBox.transform.position - renderingData.cameraData.camera.transform.position;
                var boundsScale = mSettings.CloudsBox.localScale * 0.5f;
                mMaterial.SetVector(mBoundsMinID, boundsOppositePosition - boundsScale);
                mMaterial.SetVector(mBoundsMaxID, boundsOppositePosition + boundsScale);

                // -------------------------
                // Clouds Textures
                mMaterial.SetTexture(mCloudsFBMTexture, mSettings.CloudsFBMTexture);
                mMaterial.SetTexture(mCloudsDetailNoiseTextureID, mSettings.CloudsDetailNoiseTexture);
                mMaterial.SetTexture(mWeatherTextureID, mSettings.WeatherMap);
                mMaterial.SetTexture(mMaskNoiseTextureID, mSettings.MaskNoiseTexture);

                // -------------------------
                // Clouds Shape
                mMaterial.SetVector(mCloudsShapeParamsID, new Vector4(1.0f / mSettings.CloudsShapeTiling, mSettings.HeightWeight, mSettings.NoiseStrength, mSettings.EdgeFadeProportion));
                mMaterial.SetVector(mCloudsShapeParams2ID, new Vector4(1.0f / mSettings.CloudsDetailTiling, mSettings.DetailWeight, mSettings.DetailNoiseWeight, mSettings.DetailNoiseStrength));
                mMaterial.SetVector(mCloudsFBMWeightsID, mSettings.CloudsFBMWeights);

                // -------------------------
                // Speed
                mMaterial.SetVector(mSpeedParamsID, mSettings.SpeedParams);

                // -------------------------
                // Clouds Color
                mMaterial.SetVector(mColorAID, mSettings.ColorA.linear);
                mMaterial.SetVector(mColorBID, mSettings.ColorB.linear);
                mMaterial.SetFloat(mLightAbsorptionTowardSunID, mSettings.LightAbsorptionTowardSun);
                mMaterial.SetFloat(mLightAbsorptionThroughCloudsID, mSettings.LightAbsorptionThroughClouds);
                mMaterial.SetVector(mColorOffsetsID, new Vector4(mSettings.ColorOffset1, mSettings.ColorOffset2));
                mMaterial.SetFloat(mDarknessThresholdID, mSettings.DarknessThreshold);

                // -------------------------
                // Step Params
                mMaterial.SetVector(mStepParamsID, new Vector4(mSettings.StepCount, mSettings.StepStride, mSettings.LightStepCount));

                // -------------------------
                // Density Params
                mMaterial.SetVector(mDensityParamsID, new Vector4(mSettings.DensityOffset, mSettings.DensityMultiplier));

                // -------------------------
                // Sun Params
                mMaterial.SetColor(mSunLightColorID, mSettings.SunLightColor);
                mMaterial.SetFloat(mSunLightIntensityID, mSettings.SunLightIntensity);

                // -------------------------
                // Phase Params
                mMaterial.SetColor(mAlbedoID, mSettings.Albedo);
                mMaterial.SetVector(mPhaseParamsID, new Vector4(mSettings.ForwardPhaseG, mSettings.BackPhaseG, mSettings.PhaseBlend));


                // -----------------------------------------------------------------------------------
                // 分配RTHandle
                mVolumetricCloudsTextureDescriptor = renderingData.cameraData.cameraTargetDescriptor;
                mVolumetricCloudsTextureDescriptor.msaaSamples = 1;
                mVolumetricCloudsTextureDescriptor.depthBufferBits = 0;

                RenderingUtils.ReAllocateIfNeeded(ref mVolumetricCloudsTexture, mVolumetricCloudsTextureDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: mVolumetricCloudsTextureName);

                // -----------------------------------------------------------------------------------
                // 配置目标和清除
                ConfigureTarget(renderer.cameraColorTargetHandle);
                ConfigureClear(ClearFlag.None, Color.white);
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData) {
                if (mMaterial == null) {
                    Debug.LogErrorFormat("{0}.Execute(): Missing material. ScreenSpaceAmbientOcclusion pass will not execute. Check for missing reference in the renderer resources.", GetType().Name);
                    return;
                }

                var cmd = CommandBufferPool.Get();
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                mSourceTexture = renderingData.cameraData.renderer.cameraColorTargetHandle;
                mDestinationTexture = renderingData.cameraData.renderer.cameraColorTargetHandle;

                using (new ProfilingScope(cmd, mProfilingSampler)) {
                    // Blit
                    Blitter.BlitCameraTexture(cmd, mSourceTexture, mVolumetricCloudsTexture, mMaterial, 0);
                    Blitter.BlitCameraTexture(cmd, mVolumetricCloudsTexture, mDestinationTexture);
                }

                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }

            public override void OnCameraCleanup(CommandBuffer cmd) {
                mSourceTexture = null;
                mDestinationTexture = null;
            }

            public void Dispose() {
                // 释放RTHandle
                mVolumetricCloudsTexture?.Release();
                mVolumetricCloudsTexture = null;
            }
        }
    }
}