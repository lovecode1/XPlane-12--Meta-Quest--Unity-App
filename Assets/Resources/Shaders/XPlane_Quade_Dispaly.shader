// Source: ChatGPT
Shader "Custom/XPlane_Quade_Dispaly"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        [MainColor]   _BaseColor("Base Color", Color) = (1,1,1,1)
        [NoScaleOffset] _CursorTex("Cursor Texture", 2D) = "white" {}
        [Toggle] _CursorEnabled("Cursor Enabled", Float) = 0
        _CursorUV("Cursor UV", Vector) = (0.5, 0.5, 0, 0)
        _CursorSize("Cursor Size", Vector) = (0.05, 0.05, 0, 0)
        [Range(0.0, 0.5)] _CursorAlphaCutoff("Cursor Alpha Cutoff", Range(0, 0.5)) = 0.08
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target   4.5

            // Lighting variants (safe minimal set)
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _SHADOWS_SOFT

            // URP includes
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // Material params
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_CursorTex); SAMPLER(sampler_CursorTex);
            float4 _BaseColor;
            float _CursorEnabled;
            float4 _CursorUV;
            float4 _CursorSize;
            float _CursorAlphaCutoff;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv         = v.uv;
                return o;
            }

            float4 frag(Varyings i) : SV_Target
            {
                // Sample base color and output directly to keep it fully lit/unlit
                float2 flippedUV = float2(i.uv.x, 1.0 - i.uv.y); // flip only the base map
                float4 baseTex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, flippedUV);
                float3 albedo  = (baseTex.rgb * _BaseColor.rgb);
                float alpha = baseTex.a * _BaseColor.a;

                if (_CursorEnabled > 0.5)
                {
                    float2 cursorSize = max(_CursorSize.xy, float2(1e-4, 1e-4));
                    float2 cursorUV = (i.uv - _CursorUV.xy) / cursorSize;
                    float cursorMask =
                        step(0.0, cursorUV.x) * step(cursorUV.x, 1.0) *
                        step(0.0, cursorUV.y) * step(cursorUV.y, 1.0);

                    if (cursorMask > 0.0)
                    {
                        float4 cursorSample = SAMPLE_TEXTURE2D(_CursorTex, sampler_CursorTex, cursorUV);
                        float cursorAlpha = cursorSample.a * cursorMask;
                        float cutoff = saturate(_CursorAlphaCutoff);
                        cursorAlpha = saturate((cursorAlpha - cutoff) / max(1e-4, 1.0 - cutoff)); // suppress fringe based on cutoff
                        if (cursorAlpha > 0.0001)
                        {
                            float3 cursorColor = cursorSample.rgb * cursorAlpha;
                            albedo = cursorColor + albedo * (1.0 - cursorAlpha);
                            alpha = saturate(alpha + cursorAlpha * (1.0 - alpha));
                        }
                    }
                }

                return float4(albedo, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
