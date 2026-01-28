// SPDX-License-Identifier: MIT
Shader "Gaussian Splatting/Alpha Mask"
{
    Properties
    {
        _MainTex ("Mask Texture (A)", 2D) = "white" {}
        _Color ("Tint (Alpha is mask strength)", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        
        Pass
        {
            Name "GaussianAlphaMask"
            Tags { "LightMode"="GaussianAlphaMask" }
            
            ColorMask R
            ZWrite Off
            Blend One Zero // 上書きモードに変更

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float4 _Color;

            Varyings vert (Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            half4 frag (Varyings input) : SV_Target
            {
                float mask = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).a;
                mask *= input.color.a * _Color.a;
                // Output to R channel. When background is black, this will 'show' the splats.
                return half4(mask, 0, 0, 1);
            }
            ENDHLSL
        }
    }
}
