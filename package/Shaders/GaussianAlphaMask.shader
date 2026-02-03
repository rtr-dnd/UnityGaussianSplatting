// SPDX-License-Identifier: MIT
Shader "Gaussian Splatting/Alpha Mask"
{
    Properties
    {
        _MainTex ("Mask Texture (A)", 2D) = "white" {}
        _Color ("Tint (Alpha is mask strength)", Color) = (1,1,1,1)
        _Expand ("Expand Amount", Range(0, 1)) = 0
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
            Blend One Zero // 上書きモード

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
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
            float _Expand;

            Varyings vert (Attributes input)
            {
                Varyings output;
                
                // 頂点を中心からの方向（または法線）に押し出して領域を広げる
                float3 posOS = input.positionOS.xyz;
                
                // 単純な形状（中心が0,0,0）であれば座標そのものの正規化がスムース法線の代わりになる
                float3 expandDir = normalize(posOS);
                // 法線がある程度スムースな場合は法線と混ぜるとより正確
                // float3 expandDir = normalize(input.normalOS + normalize(posOS));
                
                posOS += expandDir * _Expand;
                
                output.positionCS = TransformObjectToHClip(posOS);
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
