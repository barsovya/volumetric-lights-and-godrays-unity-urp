using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Plugins.VolumetricLightScattering.Runtime
{
    public class VolumetricLightScattering : ScriptableRendererFeature
    {
        public enum ColorSpaceEnum
        {
            HDR,
            Default,
        }

        [System.Serializable]
        public class VolumetricLightScatteringSettings
        {
            [Range(0.1f, 1f)] public float ResolutionScale = 0.5f;

            [Range(0.0f, 1.0f)] public float Intensity = 1.0f;

            [Range(0.0f, 1.0f)] public float BlurWidth = 0.85f;
        }

        [Range(0.0f, 30.0f)] public float DiffuseRadius = 30f;

        public float LightQuality = 1800;
        [Range(0.0f, 1.0f)] public float HorizontalStep = 0.1f;
        [Range(0.0f, 1.0f)] public float VerticalStep = 0.1f;
        public bool DiffuseLighting = true;
        public ColorSpaceEnum ColorFormat = ColorSpaceEnum.HDR;

        public VolumetricLightScatteringSettings Quality =
            new VolumetricLightScatteringSettings();

        private class LightScatteringPass : ScriptableRenderPass
        {
            private FilteringSettings FilteringSettings =
                new FilteringSettings(RenderQueueRange.opaque);

            private readonly List<ShaderTagId> ShaderTagIdList =
                new List<ShaderTagId>();

            private readonly Material OccludesMaterial;

            private readonly RenderTargetHandle Occludes =
                RenderTargetHandle.CameraTarget;

            private readonly Material RadialBlurMaterial;
            private readonly Material GaussianBlurMaterial;

            private readonly float ResolutionScale;
            private readonly float Intensity;
            private readonly float BlurWidth;
            private readonly ColorSpaceEnum ColorFormat;
            private readonly bool DiffuseLighting;
            private readonly float DiffuseRadius;
            private readonly float LightQuality;
            private readonly float HorizontalStep;
            private readonly float VerticalStep;

            private RenderTargetIdentifier CameraColorTargetIdent;

            public LightScatteringPass(VolumetricLightScatteringSettings settings, ColorSpaceEnum colorFormat,
                bool diffuseLighting, float diffuseRadius, float lightQuality, float horizontalStep, float verticalStep)
            {
                Occludes.Init("_OccludesMap");
                ResolutionScale = settings.ResolutionScale;
                Intensity = settings.Intensity;
                BlurWidth = settings.BlurWidth;
                ColorFormat = colorFormat;
                DiffuseLighting = diffuseLighting;
                DiffuseRadius = diffuseRadius;
                LightQuality = lightQuality;
                HorizontalStep = horizontalStep;
                VerticalStep = verticalStep;
                OccludesMaterial = new Material(Shader.Find("Hidden/Volumetric/UnlitColor"));
                ShaderTagIdList.Add(new ShaderTagId("UniversalForward"));
                ShaderTagIdList.Add(new ShaderTagId("UniversalForwardOnly"));
                ShaderTagIdList.Add(new ShaderTagId("LightweightForward"));
                ShaderTagIdList.Add(new ShaderTagId("SRPDefaultUnlit"));
                RadialBlurMaterial = new Material(
                    Shader.Find("Hidden/Volumetric/RadialBlur"));
                GaussianBlurMaterial = new Material(
                    Shader.Find("Hidden/GaussianBlur"));
            }

            public void SetCameraColorTarget(RenderTargetIdentifier cameraColorTargetIdent)
            {
                this.CameraColorTargetIdent = cameraColorTargetIdent;
            }

            // This method is called before executing the render pass.
            // It can be used to configure render targets and their clear state. Also to create temporary render target textures.
            // When empty this render pass will render to the active camera render target.
            // You should never call CommandBuffer.SetRenderTarget. Instead call <c>ConfigureTarget</c> and <c>ConfigureClear</c>.
            // The render pipeline will ensure target setup and clearing happens in a performant manner.
            public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
            {
                RenderTextureDescriptor cameraTextureDescriptor =
                    renderingData.cameraData.cameraTargetDescriptor;

                cameraTextureDescriptor.depthBufferBits = 0;

                cameraTextureDescriptor.colorFormat = ColorFormat switch
                {
                    ColorSpaceEnum.Default => RenderTextureFormat.Default,
                    ColorSpaceEnum.HDR => RenderTextureFormat.DefaultHDR,
                    _ => cameraTextureDescriptor.colorFormat
                };

                cameraTextureDescriptor.width = Mathf.RoundToInt(
                    cameraTextureDescriptor.width * ResolutionScale);
                cameraTextureDescriptor.height = Mathf.RoundToInt(
                    cameraTextureDescriptor.height * ResolutionScale);

                cmd.GetTemporaryRT(Occludes.id, cameraTextureDescriptor,
                    FilterMode.Trilinear);

                ConfigureTarget(Occludes.Identifier());
            }

            // Here you can implement the rendering logic.
            // Use <c>ScriptableRenderContext</c> to issue drawing commands or execute command buffers
            // https://docs.unity3d.com/ScriptReference/Rendering.ScriptableRenderContext.html
            // You don't have to call ScriptableRenderContext.submit, the render pipeline will call it at specific points in the pipeline.
            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                if (!OccludesMaterial || !RadialBlurMaterial)
                {
                    return;
                }

                CommandBuffer cmd = CommandBufferPool.Get();
                using (new ProfilingScope(cmd,
                           new ProfilingSampler("VolumetricLightScattering")))
                {
                    context.ExecuteCommandBuffer(cmd);
                    cmd.Clear();

                    Camera camera = renderingData.cameraData.camera;
                    context.DrawSkybox(camera);

                    DrawingSettings drawSettings = CreateDrawingSettings(ShaderTagIdList,
                        ref renderingData, SortingCriteria.CommonOpaque);

                    drawSettings.overrideMaterial = OccludesMaterial;
                    context.DrawRenderers(renderingData.cullResults,
                        ref drawSettings, ref FilteringSettings);

                    Vector3 sunDirectionWorldSpace =
                        RenderSettings.sun.transform.forward;
                    Vector3 cameraPositionWorldSpace =
                        camera.transform.position;
                    Vector3 sunPositionWorldSpace =
                        cameraPositionWorldSpace + sunDirectionWorldSpace;
                    Vector3 sunPositionViewportSpace =
                        camera.WorldToViewportPoint(sunPositionWorldSpace);

                    RadialBlurMaterial.SetVector(Center, new Vector4(
                        sunPositionViewportSpace.x, sunPositionViewportSpace.y, 0, 0));
                    RadialBlurMaterial.SetFloat(Intensity1, Intensity);
                    RadialBlurMaterial.SetFloat(Width, BlurWidth);
                }

                if (DiffuseLighting)
                {
                    GaussianBlurMaterial.SetFloat(Radius, DiffuseRadius);
                    GaussianBlurMaterial.SetFloat(Resolution1, LightQuality);
                    GaussianBlurMaterial.SetFloat(Step, HorizontalStep);
                    GaussianBlurMaterial.SetFloat(VerticalStep1, VerticalStep);
                    Blit(cmd, Occludes.Identifier(), CameraColorTargetIdent,
                        GaussianBlurMaterial);
                }
                
                Blit(cmd, Occludes.Identifier(), CameraColorTargetIdent,
                    RadialBlurMaterial);
                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }

            // Cleanup any allocated resources that were created during the execution of this render pass.
            public override void OnCameraCleanup(CommandBuffer cmd)
            {
                cmd.ReleaseTemporaryRT(Occludes.id);
            }
        }

        LightScatteringPass m_ScriptablePass;
        private static readonly int Center = Shader.PropertyToID("_Center");
        private static readonly int Intensity1 = Shader.PropertyToID("_Intensity");
        private static readonly int Width = Shader.PropertyToID("_BlurWidth");
        private static readonly int Radius = Shader.PropertyToID("radius");
        private static readonly int Resolution1 = Shader.PropertyToID("resolution");
        private static readonly int Step = Shader.PropertyToID("hstep");
        private static readonly int VerticalStep1 = Shader.PropertyToID("vstep");

        /// <inheritdoc/>
        public override void Create()
        {
            m_ScriptablePass = new LightScatteringPass(Quality, ColorFormat, DiffuseLighting, DiffuseRadius,
                LightQuality, HorizontalStep, VerticalStep)
            {
                renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing
            };
        }

        // Here you can inject one or multiple render passes in the renderer.
        // This method is called when setting up the renderer once per-camera.
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            renderer.EnqueuePass(m_ScriptablePass);
        }

        public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
        {
            m_ScriptablePass.SetCameraColorTarget(renderer.cameraColorTarget);
        }
    }
}