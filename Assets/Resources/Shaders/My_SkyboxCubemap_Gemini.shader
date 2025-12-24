// Source: Gemini
Shader "Custom/My_SkyboxCubemap_Gemini"
{
    Properties
    {
        _Tint ("Tint Color", Color) = (0.5, 0.5, 0.5, 0.5)
        [Gamma] _Exposure ("Exposure", Range(0, 8)) = 1.0
        _Rotation ("Rotation", Range(0, 360)) = 0.0
        [NoScaleOffset] _Tex ("Cubemap (HDR)", Cube) = "grey" {}
        [NoScaleOffset] _OverlayTex ("Overlay Texture (2D)", 2D) = "white" {}
        [Toggle] _OverlayFlipX ("Overlay Flip X", Float) = 0
        _OverlayStrength ("Overlay Strength", Range(0, 1)) = 0
        _OverlayCenterDir ("Overlay Center Direction", Vector) = (0, 0, 1, 0)
        _OverlayRightDir ("Overlay Right Direction", Vector) = (1, 0, 0, 0)
        _OverlayUpDir ("Overlay Up Direction", Vector) = (0, 1, 0, 0)
        _OverlayHalfAngles ("Overlay Half Angles (Radians)", Vector) = (0.785398, 0.392699, 0, 0)
        [NoScaleOffset] _CursorTex ("Cursor Texture", 2D) = "white" {}
        [Toggle] _CursorEnabled ("Cursor Enabled", Float) = 0
        _CursorUV ("Cursor UV", Vector) = (0.5, 0.5, 0, 0)
        _CursorSize ("Cursor Size", Vector) = (0.05, 0.05, 0, 0)
        [Range(0.0, 0.5)] _CursorAlphaCutoff("Cursor Alpha Cutoff", Range(0, 0.5)) = 0.08
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }

        Pass
        {
            Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }

            ZWrite Off
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            samplerCUBE _Tex;
            sampler2D _OverlayTex;
            half4 _Tex_HDR;
            half4 _Tint;
            half _Exposure;
            float _Rotation;
            half _OverlayStrength;
            float _OverlayFlipX;
            float4 _OverlayCenterDir;
            float4 _OverlayRightDir;
            float4 _OverlayUpDir;
            float4 _OverlayHalfAngles;
            sampler2D _CursorTex;
            float _CursorEnabled;
            float4 _CursorUV;
            float4 _CursorSize;
            float _CursorAlphaCutoff;

            struct appdata_t
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 texcoord : TEXCOORD0;
            };

            v2f vert(appdata_t v)
            {
                v2f o;

                // Create a rotation matrix for the y-axis
                float rot = _Rotation * 3.14159265359 / 180.0;
                float s = sin(rot);
                float c = cos(rot);
                float3x3 rotationMatrix = float3x3(
                    c, 0, -s,
                    0, 1, 0,
                    s, 0, c
                );

                // Apply rotation to the vertex position
                float3 rotatedPos = mul(rotationMatrix, v.vertex.xyz);

                // Apply the transformation from object space to clip space
                o.pos = UnityObjectToClipPos(float4(rotatedPos, 1.0));
                
                // Pass the vertex position as the cubemap texture coordinate
                o.texcoord = v.vertex.xyz;
                
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                half4 tex = texCUBE(_Tex, i.texcoord);
                
                // Apply HDR decoding (same logic as inferred from disassembly)
                // This is a common part of Unity's built-in skybox shaders
                half3 c = DecodeHDR(tex, _Tex_HDR);

                // Apply tint and exposure
                c *= _Tint.rgb * _Exposure;

                half overlayActive = (_OverlayStrength > 0.0001);
                float3 direction = normalize(i.texcoord);
                float3 centerDir = normalize(_OverlayCenterDir.xyz);
                float3 rightDir = normalize(_OverlayRightDir.xyz);
                float3 upDir = normalize(_OverlayUpDir.xyz);
                float2 halfAngles = max(_OverlayHalfAngles.xy, float2(0.0001, 0.0001));

                float x = dot(direction, rightDir);
                float y = dot(direction, upDir);
                float z = dot(direction, centerDir);

                float angleRight = atan2(x, z);
                float angleUp = atan2(y, z);

                float2 normalizedAngles = float2(
                    angleRight / halfAngles.x,
                    angleUp / halfAngles.y);

                float inside =
                    step(-1.0, normalizedAngles.x) * step(normalizedAngles.x, 1.0) *
                    step(-1.0, normalizedAngles.y) * step(normalizedAngles.y, 1.0);

                float2 overlayUV = saturate(normalizedAngles * 0.5 + 0.5);
                float flipMask = step(0.5, _OverlayFlipX);
                overlayUV.x = lerp(overlayUV.x, 1.0 - overlayUV.x, flipMask);

                if (overlayActive > 0.0)
                {
                    half4 overlaySample = tex2D(_OverlayTex, overlayUV);
                    half overlayFactor = overlaySample.a * _OverlayStrength * inside;
                    c = lerp(c, overlaySample.rgb, overlayFactor);
                }

                if (_CursorEnabled > 0.5)
                {
                    float2 cursorSize = max(_CursorSize.xy, float2(1e-4, 1e-4));
                    float2 cursorUV = (overlayUV - _CursorUV.xy) / cursorSize;
                    float cursorMask =
                        step(0.0, cursorUV.x) * step(cursorUV.x, 1.0) *
                        step(0.0, cursorUV.y) * step(cursorUV.y, 1.0) *
                        inside;
                    if (cursorMask > 0.0)
                    {
                        half4 cursorSample = tex2D(_CursorTex, cursorUV);
                        half cursorAlpha = cursorSample.a * cursorMask;
                        half cutoff = saturate(_CursorAlphaCutoff);
                        cursorAlpha = saturate((cursorAlpha - cutoff) / max(1e-4h, 1.0h - cutoff));
                        if (cursorAlpha > 0.0001)
                        {
                            half3 cursorColor = cursorSample.rgb * cursorAlpha;
                            c = cursorColor + c * (1.0 - cursorAlpha);
                        }
                    }
                }
                
                return half4(c, 1.0);
            }
            ENDCG
        }
    }
}
