// Procedural Demo Buddy shader: opaque, unlit, vertex-colored with a cheap baked-direction shade so low-poly shapes read as 3D.
// One shared material for every buddy part (1 material per buddy). Written for URP incl. Quest single-pass instanced stereo.
Shader "BumpXR/BuddyUnlit"
{
    Properties
    {
        _Tint ("Tint", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                half shade : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                float3 n = normalize(TransformObjectToWorldNormal(input.normalOS));
                half d = saturate(dot(n, normalize(float3(0.35, 0.80, -0.45))) * 0.5 + 0.5);
                o.shade = lerp(0.58h, 1.0h, d);
                o.color = input.color;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                return half4(i.color.rgb * _Tint.rgb * i.shade, 1.0h);
            }
            ENDHLSL
        }
    }
    FallBack Off
}

