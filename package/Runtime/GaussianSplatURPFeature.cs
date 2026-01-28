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

        class GSRenderPass : ScriptableRenderPass
        {
            const string GaussianSplatRTName = "_GaussianSplatRT";
            const string GaussianAlphaMaskRTName = "_GaussianAlphaMaskRT";

            const string ProfilerTag = "GaussianSplatRenderGraph";
            static readonly ProfilingSampler s_profilingSampler = new(ProfilerTag);
            static readonly int s_gaussianSplatRT = Shader.PropertyToID(GaussianSplatRTName);
            static readonly int s_gaussianAlphaMaskRT = Shader.PropertyToID(GaussianAlphaMaskRTName);

            public LayerMask m_MaskLayer;

            class PassData
            {
                internal UniversalCameraData CameraData;
                internal TextureHandle SourceTexture;
                internal TextureHandle SourceDepth;
                internal TextureHandle GaussianSplatRT;
                internal TextureHandle GaussianAlphaMaskRT;
                internal RendererListHandle MaskRendererList;
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
                    // Use custom LightMode tag to avoid rendering into main scene
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
                            cmd.DrawRendererList(data.MaskRendererList); // ここで実際に描画！
                        });
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
                    passData.GaussianAlphaMaskRT = maskTextureHandle;

                    builder.UseTexture(resourceData.activeColorTexture, AccessFlags.ReadWrite);
                    builder.UseTexture(resourceData.activeDepthTexture, AccessFlags.Read);
                    builder.UseTexture(textureHandle, AccessFlags.Write);
                    builder.UseTexture(maskTextureHandle, AccessFlags.Read);
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
        bool m_HasCamera;

        public override void Create()
        {
            m_Pass = new GSRenderPass
            {
                renderPassEvent = RenderPassEvent.BeforeRenderingTransparents,
                m_MaskLayer = m_MaskLayer
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
            renderer.EnqueuePass(m_Pass);
        }

        protected override void Dispose(bool disposing)
        {
            m_Pass = null;
        }
    }
}

#endif // #if GS_ENABLE_URP
