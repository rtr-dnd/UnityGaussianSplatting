// SPDX-License-Identifier: MIT
#if GS_ENABLE_URP

#if !UNITY_6000_0_OR_NEWER
#error Unity Gaussian Splatting URP support only works in Unity 6 or later
#endif

using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

namespace GaussianSplatting.Runtime
{
    class GaussianSplatURPFeature : ScriptableRendererFeature
    {
        public LayerMask m_MaskLayer = 0;
        public float m_MaskBlurRadius = 0;
        public Shader m_ShaderBlur;

        class GSRenderPass : ScriptableRenderPass
        {
            const string GaussianSplatRTName = "_GaussianSplatRT";
            const string GaussianAlphaMaskRTName = "_GaussianAlphaMaskRT";
            const string GaussianAlphaMaskBlurRTName = "_GaussianAlphaMaskBlurRT";

            const string ProfilerTag = "GaussianSplatRenderGraph";
            static readonly ProfilingSampler s_profilingSampler = new(ProfilerTag);
            static readonly int s_gaussianSplatRT = Shader.PropertyToID(GaussianSplatRTName);
            static readonly int s_gaussianAlphaMaskRT = Shader.PropertyToID(GaussianAlphaMaskRTName);
            static readonly int s_blurRadius = Shader.PropertyToID("_BlurRadius");

            public LayerMask m_MaskLayer;
            public float m_MaskBlurRadius;
            public Material m_MatBlur;

            class PassData
            {
                internal UniversalCameraData CameraData;
                internal TextureHandle SourceTexture;
                internal TextureHandle SourceDepth;
                internal TextureHandle GaussianSplatRT;
                internal TextureHandle GaussianAlphaMaskRT;
                internal TextureHandle GaussianAlphaMaskBlurRT;
                internal RendererListHandle MaskRendererList;
                internal float MaskBlurRadius;
                internal Material MatBlur;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var cameraData = frameData.Get<UniversalCameraData>();
                var resourceData = frameData.Get<UniversalResourceData>();
                var renderingData = frameData.Get<UniversalRenderingData>();

                // --- Mask Pass ---
                RenderTextureDescriptor maskDesc = cameraData.cameraTargetDescriptor;
                maskDesc.graphicsFormat = GraphicsFormat.R8_UNorm;
                maskDesc.depthBufferBits = 0;
                maskDesc.msaaSamples = 1;
                var maskTextureHandle = UniversalRenderer.CreateRenderGraphTexture(renderGraph, maskDesc, GaussianAlphaMaskRTName, true);

                using (var maskBuilder = renderGraph.AddUnsafePass("GaussianAlphaMaskPass", out PassData maskPassData))
                {
                    maskPassData.GaussianAlphaMaskRT = maskTextureHandle;
                    maskBuilder.UseTexture(maskTextureHandle, AccessFlags.Write);
                    maskBuilder.AllowPassCulling(false);
                    maskBuilder.SetRenderFunc(static (PassData data, UnsafeGraphContext context) =>
                    {
                        var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                        CoreUtils.SetRenderTarget(cmd, data.GaussianAlphaMaskRT);
                        cmd.ClearRenderTarget(RTClearFlags.Color, Color.black, 1, 0); // 黒(0.0)で初期化
                    });
                }

                if (m_MaskLayer != 0)
                {
                    var maskRendererDesc = new UnityEngine.Rendering.RendererUtils.RendererListDesc(new ShaderTagId("GaussianAlphaMask"), renderingData.cullResults, cameraData.camera)
                    {
                        layerMask = m_MaskLayer,
                        renderQueueRange = RenderQueueRange.all,
                        sortingCriteria = SortingCriteria.CommonTransparent,
                    };
                    RendererListHandle maskRendererList = renderGraph.CreateRendererList(maskRendererDesc);

                    using (var maskRenderBuilder = renderGraph.AddUnsafePass("GaussianRenderMaskObjects", out PassData maskRenderData))
                    {
                        maskRenderData.GaussianAlphaMaskRT = maskTextureHandle;
                        maskRenderData.MaskRendererList = maskRendererList;
                        maskRenderBuilder.UseRendererList(maskRendererList);
                        maskRenderBuilder.UseTexture(maskTextureHandle, AccessFlags.Write);
                        maskRenderBuilder.SetRenderFunc(static (PassData data, UnsafeGraphContext context) =>
                        {
                            var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                            CoreUtils.SetRenderTarget(cmd, data.GaussianAlphaMaskRT);
                            cmd.DrawRendererList(data.MaskRendererList);
                        });
                    }
                }

                // --- Blur Pass ---
                TextureHandle finalMaskHandle = maskTextureHandle;
                if (m_MaskBlurRadius > 0 && m_MatBlur != null)
                {
                    var blurTextureHandle = UniversalRenderer.CreateRenderGraphTexture(renderGraph, maskDesc, GaussianAlphaMaskBlurRTName, true);
                    using (var blurBuilder = renderGraph.AddUnsafePass("GaussianAlphaMaskBlurPass", out PassData blurPassData))
                    {
                        blurPassData.GaussianAlphaMaskRT = maskTextureHandle;
                        blurPassData.GaussianAlphaMaskBlurRT = blurTextureHandle;
                        blurPassData.MaskBlurRadius = m_MaskBlurRadius;
                        blurPassData.MatBlur = m_MatBlur;

                        blurBuilder.UseTexture(maskTextureHandle, AccessFlags.Read);
                        blurBuilder.UseTexture(blurTextureHandle, AccessFlags.Write);
                        blurBuilder.SetRenderFunc(static (PassData data, UnsafeGraphContext context) =>
                        {
                            var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                            data.MatBlur.SetFloat(s_blurRadius, data.MaskBlurRadius);
                            Blitter.BlitCameraTexture(cmd, data.GaussianAlphaMaskRT, data.GaussianAlphaMaskBlurRT, data.MatBlur, 0);
                        });
                        finalMaskHandle = blurTextureHandle;
                    }
                }

                // --- Splat Pass ---
                using (var builder = renderGraph.AddUnsafePass(ProfilerTag, out PassData passData))
                {
                    RenderTextureDescriptor rtDesc = cameraData.cameraTargetDescriptor;
                    rtDesc.depthBufferBits = 0;
                    rtDesc.msaaSamples = 1;
                    rtDesc.graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;
                    var textureHandle = UniversalRenderer.CreateRenderGraphTexture(renderGraph, rtDesc, GaussianSplatRTName, true);

                    passData.CameraData = cameraData;
                    passData.SourceTexture = resourceData.activeColorTexture;
                    passData.SourceDepth = resourceData.activeDepthTexture;
                    passData.GaussianSplatRT = textureHandle;
                    passData.GaussianAlphaMaskRT = finalMaskHandle;

                    builder.UseTexture(resourceData.activeColorTexture, AccessFlags.ReadWrite);
                    builder.UseTexture(resourceData.activeDepthTexture, AccessFlags.Read);
                    builder.UseTexture(textureHandle, AccessFlags.Write);
                    builder.UseTexture(finalMaskHandle, AccessFlags.Read);
                    builder.AllowPassCulling(false);

                    builder.SetRenderFunc(static (PassData data, UnsafeGraphContext context) =>
                    {
                        var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                        using var _ = new ProfilingScope(cmd, s_profilingSampler);
                        
                        cmd.SetGlobalTexture(s_gaussianSplatRT, data.GaussianSplatRT);
                        cmd.SetGlobalTexture(s_gaussianAlphaMaskRT, data.GaussianAlphaMaskRT);
                        
                        // Use SourceDepth which should contain the stencil from opaque objects
                        CoreUtils.SetRenderTarget(cmd, data.GaussianSplatRT, data.SourceDepth);
                        cmd.ClearRenderTarget(RTClearFlags.Color, Color.clear, 1, 0);

                        Material matComposite = GaussianSplatRenderSystem.instance.SortAndRenderSplats(data.CameraData.camera, cmd);
                        
                        cmd.BeginSample(GaussianSplatRenderSystem.s_ProfCompose);
                        Blitter.BlitCameraTexture(cmd, data.GaussianSplatRT, data.SourceTexture, matComposite, 0);
                        cmd.EndSample(GaussianSplatRenderSystem.s_ProfCompose);
                    });
                }
            }
        }

        GSRenderPass m_Pass;
        Material m_MatBlur;
        bool m_HasCamera;

        public override void Create()
        {
            if (m_ShaderBlur != null)
                m_MatBlur = CoreUtils.CreateEngineMaterial(m_ShaderBlur);

            m_Pass = new GSRenderPass
            {
                renderPassEvent = RenderPassEvent.BeforeRenderingTransparents,
                m_MaskLayer = m_MaskLayer,
                m_MaskBlurRadius = m_MaskBlurRadius,
                m_MatBlur = m_MatBlur
            };
        }

        public override void OnCameraPreCull(ScriptableRenderer renderer, in CameraData cameraData)
        {
            m_HasCamera = false;
            var system = GaussianSplatRenderSystem.instance;
            if (!system.GatherSplatsForCamera(cameraData.camera))
                return;

            m_HasCamera = true;
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (!m_HasCamera)
                return;
            
            m_Pass.m_MaskLayer = m_MaskLayer;
            m_Pass.m_MaskBlurRadius = m_MaskBlurRadius;
            m_Pass.m_MatBlur = m_MatBlur;
            
            renderer.EnqueuePass(m_Pass);
        }

        protected override void Dispose(bool disposing)
        {
            m_Pass = null;
            CoreUtils.Destroy(m_MatBlur);
        }
    }
}

#endif // #if GS_ENABLE_URP
