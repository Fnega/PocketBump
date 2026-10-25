Shader "PocketBump/DashGroundReadiness"
{
    Properties
    {
        _IdentityColor ("Charged colour", Color) = (0.035, 0.72, 0.9, 1)
        _TrackColor ("Empty track colour", Color) = (0.17, 0.23, 0.25, 0.8)
        _OutlineColor ("Edge colour", Color) = (0.025, 0.065, 0.08, 1)
        _RingWidth ("Normalized ring width", Range(0.001, 0.9)) = 0.214286
        _OutlineWidth ("Normalized edge width", Range(0, 0.45)) = 0.022321
        _Progress ("Charged fraction", Range(0, 1)) = 0.65
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" }
        Pass
        {
            Name "GroundReadiness"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            CBUFFER_START(UnityPerMaterial)
                half4 _IdentityColor;
                half4 _TrackColor;
                half4 _OutlineColor;
                float _RingWidth;
                float _OutlineWidth;
                float _Progress;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float2 uvPoint = input.uv * 2.0 - 1.0;
                float radius = length(uvPoint);
                float inner = 1.0 - _RingWidth;
                float antialias = max(fwidth(radius), 0.0001);
                float coverage = (1.0 - smoothstep(1.0 - antialias, 1.0, radius)) * smoothstep(inner, inner + antialias, radius);
                clip(coverage - 0.001);
                // Fixed south start keeps the final gap in front of the robot for
                // both local cameras. It never rotates with robot yaw or the camera.
                float angle = frac(atan2(uvPoint.x, uvPoint.y) / 6.28318530718 + 0.5);
                float charged = _Progress >= 1.0 ? 1.0 : (_Progress <= 0.0 ? 0.0 : step(angle, _Progress));
                half4 colour = lerp(_TrackColor, _IdentityColor, charged);
                float edgeDistance = min(radius - inner, 1.0 - radius);
                float edge = 1.0 - smoothstep(_OutlineWidth, _OutlineWidth + antialias, edgeDistance);
                colour = lerp(colour, _OutlineColor, edge);
                colour.a *= coverage;
                return colour;
            }
            ENDHLSL
        }
    }
}
