// SPDX-License-Identifier: MIT
Shader "Hidden/Gaussian Splatting/Alpha Mask Blur"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline" = "UniversalPipeline"}
        LOD 100

        Pass
        {
            Name "AlphaMaskBlur"
            ZTest Always
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _BlurRadius;

            half4 frag (Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float totalMask = 0;
                float totalWeight = 0;

                float radius = _BlurRadius;
                
                // 動的なステップ幅の決定（負荷軽減）
                // 半径が大きくなるほどサンプリングを間引く
                int stepSize = 1;
                if (radius > 12) stepSize = 3;
                else if (radius > 6) stepSize = 2;

                int iterLimit = (int)(radius / stepSize);

                for (int x = -iterLimit; x <= iterLimit; x++)
                {
                    for (int y = -iterLimit; y <= iterLimit; y++)
                    {
                        float2 pixelOffset = float2(x, y) * stepSize;
                        float2 uvOffset = pixelOffset * _BlitTexture_TexelSize.xy;
                        float distSq = dot(pixelOffset, pixelOffset);
                        
                        if (distSq <= radius * radius)
                        {
                            // ガウス重みの計算
                            float sigma = radius * 0.5 + 0.001;
                            float weight = exp(-distSq / (2 * sigma * sigma));
                            
                            totalMask += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + uvOffset).r * weight;
                            totalWeight += weight;
                        }
                    }
                }

                return half4(totalMask / (totalWeight + 0.0001), 0, 0, 1);
            }
            ENDHLSL
        }
    }
}
