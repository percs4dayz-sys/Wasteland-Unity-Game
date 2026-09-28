Shader "Wasteland/BiomeTerrain"
{
    // Recolors a mesh "ground" into regional biomes using a top-down mask:
    //   BLACK (unpainted) = Grass   Red = Desert   Green = Swamp   Blue = Snow
    // Steep faces automatically become rock/cliff. Edit the mask PNG in any image
    // editor (MS Paint works) to move biomes around — no in-Unity brush needed.
    //
    // Renders on BOTH URP renderers: the 2D Renderer (this project's active one, via the
    // Universal2D pass) and the 3D Forward renderer (via UniversalForward). That's why an
    // earlier Forward-only version went invisible under the 2D renderer.
    Properties
    {
        [MainTexture] _BiomeMask ("Biome Mask (black=Grass R=Desert G=Swamp B=Snow)", 2D) = "black" {}
        _GrassColor ("Grass Color",        Color) = (0.26,0.40,0.15,1)
        _SandColor  ("Desert / Sand Color",Color) = (0.78,0.69,0.46,1)
        _SwampColor ("Swamp Color",        Color) = (0.17,0.24,0.13,1)
        _SnowColor  ("Snow Color",         Color) = (0.93,0.95,0.98,1)
        _RockColor  ("Rock / Cliff Color", Color) = (0.36,0.34,0.32,1)
        _RockTex    ("Rock / Building Texture (steep faces)", 2D) = "gray" {}
        _RockTexScale ("Rock Texture Tiling", Float) = 0.2
        _WorldMin   ("World Min (xz)",   Vector) = (-300,0,-300,0)
        _WorldSize  ("World Size (xz)",  Vector) = (600,1,600,0)
        _RockStart  ("Cliff Start (slope)", Range(0,1)) = 0.50
        _RockEnd    ("Cliff Full (slope)",  Range(0,1)) = 0.78
        _Smoothness ("Smoothness",          Range(0,1)) = 0.06
        _SunShade   ("Sun Shading (depth)", Range(0,1)) = 0.78
        _BiomeSharpness ("Biome Edge Sharpness", Range(1,6)) = 2.5
        _DetailScale    ("Surface Detail Scale", Float) = 0.12
        _DetailStrength ("Surface Detail Strength", Range(0,1)) = 0.35
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        // Shared declarations for every pass.
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_BiomeMask); SAMPLER(sampler_BiomeMask);
        TEXTURE2D(_RockTex);   SAMPLER(sampler_RockTex);

        CBUFFER_START(UnityPerMaterial)
            float4 _GrassColor, _SandColor, _SwampColor, _SnowColor, _RockColor;
            float4 _WorldMin, _WorldSize;
            float  _RockStart, _RockEnd, _Smoothness, _SunShade;
            float  _BiomeSharpness, _DetailScale, _DetailStrength, _RockTexScale;
        CBUFFER_END

        // --- Cheap procedural value noise (no texture needed) for surface detail ---
        float  Hash21 (float2 p) { p = frac(p * float2(123.34, 345.45)); p += dot(p, p + 34.345); return frac(p.x * p.y); }
        float  VNoise (float2 p)
        {
            float2 i = floor(p), f = frac(p), u = f * f * (3.0 - 2.0 * f);
            float a = Hash21(i), b = Hash21(i + float2(1,0)), c = Hash21(i + float2(0,1)), d = Hash21(i + float2(1,1));
            return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
        }
        float  FBM (float2 p)
        {
            float s = 0.0, a = 0.5;
            [unroll] for (int i = 0; i < 4; i++) { s += a * VNoise(p); p *= 2.0; a *= 0.5; }
            return s;
        }

        struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            float3 normalWS   : TEXCOORD1;
            float  fogCoord   : TEXCOORD2;
        };

        Varyings vert (Attributes IN)
        {
            Varyings OUT;
            VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS.xyz);
            VertexNormalInputs   n = GetVertexNormalInputs(IN.normalOS);
            OUT.positionCS = p.positionCS;
            OUT.positionWS = p.positionWS;
            OUT.normalWS   = n.normalWS;
            OUT.fogCoord   = ComputeFogFactor(p.positionCS.z);
            return OUT;
        }

        // Regional biome (R=Desert G=Swamp B=Snow, black=Grass) + auto rock on steep slopes.
        float3 BiomeAlbedo (float3 positionWS, float3 nWS)
        {
            float2 uv = (positionWS.xz - _WorldMin.xz) / max(_WorldSize.xz, float2(1.0, 1.0));
            float3 m  = SAMPLE_TEXTURE2D(_BiomeMask, sampler_BiomeMask, uv).rgb;
            float desertW = m.r;
            float swampW  = m.g;
            float snowW   = m.b;
            float grassW  = saturate(1.0 - (m.r + m.g + m.b));

            // Sharpen so each zone reads as itself instead of a muddy gradient.
            grassW  = pow(grassW,  _BiomeSharpness);
            desertW = pow(desertW, _BiomeSharpness);
            swampW  = pow(swampW,  _BiomeSharpness);
            snowW   = pow(snowW,   _BiomeSharpness);

            float wsum = grassW + desertW + swampW + snowW + 1e-4;
            float3 biome = (_GrassColor.rgb * grassW + _SandColor.rgb * desertW +
                            _SwampColor.rgb * swampW + _SnowColor.rgb * snowW) / wsum;
            float slope = 1.0 - saturate(nWS.y);
            float rock  = smoothstep(_RockStart, _RockEnd, slope);

            // Triplanar rock/concrete texture so steep faces (buildings, cliffs) show real surface
            // detail and wrap correctly onto vertical walls. Default "gray" texture = no change.
            float3 bw = pow(abs(nWS), 4.0);
            bw /= (bw.x + bw.y + bw.z + 1e-4);
            float3 tX = SAMPLE_TEXTURE2D(_RockTex, sampler_RockTex, positionWS.zy * _RockTexScale).rgb;
            float3 tY = SAMPLE_TEXTURE2D(_RockTex, sampler_RockTex, positionWS.xz * _RockTexScale).rgb;
            float3 tZ = SAMPLE_TEXTURE2D(_RockTex, sampler_RockTex, positionWS.xy * _RockTexScale).rgb;
            float3 rockTex = tX * bw.x + tY * bw.y + tZ * bw.z;
            float3 rockCol = _RockColor.rgb * rockTex * 2.0;   // gray(0.5)*2 = unchanged; texture = detailed
            float3 col  = lerp(biome, rockCol, rock);

            // Procedural detail so the ground looks like terrain, not flat paint.
            float2 wpx = positionWS.xz;
            float n1 = FBM(wpx * _DetailScale);
            float n2 = FBM(wpx * _DetailScale * 5.3 + 31.7);
            float detail = lerp(1.0 - _DetailStrength, 1.0 + _DetailStrength, n1)
                         * lerp(1.0 - _DetailStrength * 0.5, 1.0 + _DetailStrength * 0.5, n2);
            return col * detail;
        }
        ENDHLSL

        // --- 2D Renderer path (this project's active renderer) ---
        Pass
        {
            Name "Universal2D"
            Tags { "LightMode"="Universal2D" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag2d

            half4 frag2d (Varyings IN) : SV_Target
            {
                float3 nWS = normalize(IN.normalWS);
                float3 albedo = BiomeAlbedo(IN.positionWS, nWS);
                // Fixed key + soft fill so hills/craters read (the 2D renderer has no 3D lights).
                float3 sun  = normalize(float3(0.4, 0.85, 0.35));
                float  ndl  = saturate(dot(nWS, sun));
                float  fill = saturate(dot(nWS, normalize(float3(-0.4, 0.5, -0.35)))) * 0.25;
                float  shade = lerp(1.0 - _SunShade, 1.0, ndl) + fill;
                return half4(albedo * shade, 1.0);
            }
            ENDHLSL
        }

        // --- 3D Forward renderer path (if the project ever switches to it) ---
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            half4 frag (Varyings IN) : SV_Target
            {
                float3 nWS = normalize(IN.normalWS);
                float3 albedo = BiomeAlbedo(IN.positionWS, nWS);

                InputData inputData = (InputData)0;
                inputData.positionWS = IN.positionWS;
                inputData.normalWS   = nWS;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(IN.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                inputData.fogCoord = IN.fogCoord;
                inputData.bakedGI  = SampleSH(nWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.positionCS);

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo     = albedo;
                surfaceData.smoothness = _Smoothness;
                surfaceData.occlusion  = 1.0;
                surfaceData.alpha      = 1.0;

                half4 color = UniversalFragmentPBR(inputData, surfaceData);
                color.rgb = MixFog(color.rgb, IN.fogCoord);
                return color;
            }
            ENDHLSL
        }

        // Shadows (used by the Forward renderer).
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0

            HLSLPROGRAM
            #pragma vertex shadowVert
            #pragma fragment shadowFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            struct VarS { float4 positionCS : SV_POSITION; };

            VarS shadowVert (Attributes IN)
            {
                VarS OUT;
                float3 posWS  = TransformObjectToWorld(IN.positionOS.xyz);
                float3 normWS = TransformObjectToWorldNormal(IN.normalOS);
                float4 cs = TransformWorldToHClip(ApplyShadowBias(posWS, normWS, _LightDirection));
                #if UNITY_REVERSED_Z
                    cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                OUT.positionCS = cs;
                return OUT;
            }
            half4 shadowFrag (VarS IN) : SV_Target { return 0; }
            ENDHLSL
        }

        // Depth prepass / depth texture.
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask 0

            HLSLPROGRAM
            #pragma vertex depthVert
            #pragma fragment depthFrag
            struct VarD { float4 positionCS : SV_POSITION; };
            VarD depthVert (Attributes IN) { VarD o; o.positionCS = TransformObjectToHClip(IN.positionOS.xyz); return o; }
            half4 depthFrag (VarD IN) : SV_Target { return 0; }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Lit"
}
