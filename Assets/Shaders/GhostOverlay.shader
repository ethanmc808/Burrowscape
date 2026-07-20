Shader "Custom/GhostOverlay"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Overlay" "RenderPipeline"="UniversalPipeline" }
        LOD 100

        Pass
        {
            Name "GhostOverlay"
            Tags { "LightMode"="UniversalForward" }

            // The whole point of this shader: ZTest Always means this pass draws regardless of what's
            // already in the depth buffer (dirt tiles, real rooms, anything nearer the camera) — the
            // ghost preview should never be hideable by ordinary scene geometry. ZWrite stays off so the
            // ghost itself still doesn't block anything drawn after it. Cull Off so it's visible from any
            // camera angle, since a preview object should never have an "invisible" side.
            ZTest Always
            ZWrite Off
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                return _Color;
            }
            ENDHLSL
        }
    }
}
