// SPDX-License-Identifier: MIT
Shader "Gaussian Splatting/Stencil Mask"
{
    Properties
    {
        _StencilRef ("Stencil Ref", Int) = 1
        [Enum(UnityEngine.Rendering.CompareFunction)] _StencilComp ("Stencil Comparison", Int) = 8 // Always
    }
    SubShader
    {
        // ジオメトリより先に描画してステンシルを仕込む
        Tags { "RenderType"="Opaque" "Queue"="Geometry-1" }
        
        Pass
        {
            ColorMask 0    // 透明に戻す
            ZWrite Off     
            
            Stencil
            {
                Ref [_StencilRef]
                Comp [_StencilComp]
                Pass Replace
            }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                return fixed4(0, 0, 0, 0);
            }
            ENDCG
        }
    }
}
