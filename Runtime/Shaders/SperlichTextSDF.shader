Shader "Sperlich/Text SDF"
{
    Properties
    {
        [PerRendererData] _MainTex ("Glyph Atlas (A = SDF)", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _FaceDilate ("Face Dilate", Range(-0.5,0.5)) = 0.0
        _Sharpness ("Sharpness", Range(0,2)) = 1.0

        _OutlineColor ("Outline Color", Color) = (0,0,0,1)
        _OutlineWidth ("Outline Width (px)", Range(0,32)) = 0.0
        [Enum(Inner,0,Middle,1,Outer,2)] _OutlineMode ("Outline Placement", Float) = 2.0

        _UnderlayColor ("Shadow Color", Color) = (0,0,0,0.5)
        _UnderlayOffset ("Shadow Offset (xy) / Softness (z)", Vector) = (0,0,0.05,0)
        _UnderlayDilate ("Shadow Dilate", Range(0,1)) = 0.0
        _ShadowTaps ("Shadow Taps", Float) = 24

        _GlowColor ("Glow Color", Color) = (0.3,0.6,1,0)
        _GlowPower ("Glow Power", Range(0,1)) = 0.0
        _GlowOuter ("Glow Outer", Range(0,0.5)) = 0.25
        _GlowTaps ("Glow Taps", Float) = 24

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
        CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #pragma multi_compile_local _ SPERLICH_MTSDF

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float3 normal   : NORMAL;
                float4 tangent  : TANGENT;
                float4 color    : COLOR;
                float4 uv0      : TEXCOORD0; // xy = atlas uv, z = sdf scale, w = weight bias
                float4 uv1      : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex    : SV_POSITION;
                fixed4 color     : COLOR;
                float4 uv0       : TEXCOORD0;
                float4 worldPos  : TEXCOORD1;
                float4 uv1       : TEXCOORD2;
                float4 cellRect  : TEXCOORD3; // per-tag glow: (u0,v0,u1,v1) of this glyph's atlas cell
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _MainTex_TexelSize;
            fixed4 _Color;
            float _FaceDilate;
            float _Sharpness;
            fixed4 _OutlineColor;
            float _OutlineWidth;
            float _OutlineMode; // 0 = Inner, 1 = Middle, 2 = Outer
            fixed4 _UnderlayColor;
            float4 _UnderlayOffset;
            float _UnderlayDilate;
            float _ShadowTaps;
            fixed4 _GlowColor;
            float _GlowPower;
            float _GlowOuter;
            float _GlowTaps;
            float4 _ClipRect;

            v2f vert(appdata_t v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.worldPos = v.vertex;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv0 = float4(TRANSFORM_TEX(v.uv0.xy, _MainTex), v.uv0.z, v.uv0.w);
                o.uv1 = v.uv1;
                o.cellRect = v.tangent;
                o.color = v.color * _Color;
                return o;
            }

            // median of the 3 MTSDF colour channels = the reconstructed shape field, which keeps
            // sharp corners a single-channel SDF rounds off.
            float median3(float3 c) { return max(min(c.r, c.g), min(max(c.r, c.g), c.b)); }

            // raw-SDF value -> coverage, anti-aliased in screen space via screen-space derivatives.
            // `dist` is (sampled_field - 0.5 + dilate + bias); `threshold` shifts the edge outward
            // for the outline bands. fwidth(dist) is the per-screen-pixel change of the field at this
            // fragment, so (dist - threshold) / fwidth(dist) is the signed distance to that edge in
            // screen pixels -> a ~1 px AA band at ANY glyph size, with no dependency on a per-vertex
            // scale (uGUI does not reliably deliver uv0.z/.w to the fragment stage). `screenPxRange`
            // is kept in the signature but unused; `sharpen` (1 = natural) tightens/loosens the band.
            float coverage(float dist, float threshold, float screenPxRange, float sharpen)
            {
                float w = max(fwidth(dist), 1e-5);
                float px = (dist - threshold) / w;
                float sharpMul = max(0.15, sharpen);
                return saturate(px * sharpMul + 0.5);
            }

            float ditheredSpiralBlur(float radPx, float2 centerUV, float4 cellRect, float bias, float sharpness, float2 screenPos, int numTaps, float uv_per_local)
            {
                float2 uvClamp = clamp(centerUV, cellRect.xy, cellRect.zw);
                float4 centerField = tex2D(_MainTex, uvClamp);
                float centerDist = centerField.a - 0.5 + bias;
                
                if (radPx <= 0.01 || numTaps <= 1)
                {
                    return coverage(centerDist, 0.0, 1.0, sharpness);
                }

                float w = max(fwidth(centerDist), 1e-5);
                float sharpMul = max(0.15, sharpness);

                float noise = frac(sin(dot(screenPos, float2(12.9898, 78.233))) * 43758.5453);
                float angleOffset = noise * 6.2831853;
                
                float accum = 0.0;
                int safeTaps = min(numTaps, 64);
                
                [loop]
                for (int j = 0; j < safeTaps; j++)
                {
                    float t = (j + 0.5) / (float)safeTaps;
                    float r = sqrt(t) * radPx;
                    float theta = j * 2.39996323 + angleOffset;
                    
                    float2 offsetPx = r * float2(cos(theta), sin(theta));
                    float2 uvOffset = offsetPx * uv_per_local;
                    float2 uvTap = clamp(centerUV + uvOffset, cellRect.xy, cellRect.zw);
                    
                    float tapDist = tex2D(_MainTex, uvTap).a - 0.5 + bias;
                    float px = tapDist / w;
                    accum += saturate(px * sharpMul + 0.5);
                }
                return accum / (float)safeTaps;
            }

            float sampleOutlineMorphology(float radiusPx, float2 centerUV, float4 cellRect, float bias, float sharpness, float uv_per_local, bool isErosion)
            {
                float2 uvClamp = clamp(centerUV, cellRect.xy, cellRect.zw);
                float4 centerField = tex2D(_MainTex, uvClamp);
                float centerDist = centerField.a - 0.5 + bias;
                
                if (radiusPx <= 0.01)
                {
                    return coverage(centerDist, 0.0, 1.0, sharpness);
                }

                float w = max(fwidth(centerDist), 1e-5);
                float sharpMul = max(0.15, sharpness);
                float targetDist = centerDist;

                // 2 concentric rings: 8 taps at 0.5 R, 16 taps at 1.0 R
                // Sampling continuous distance field produces a perfectly smooth contour without scalloped bumps
                [loop]
                for (int j = 0; j < 8; j++)
                {
                    float angle = j * (6.2831853 / 8.0);
                    float2 offset = float2(cos(angle), sin(angle)) * (radiusPx * 0.5 * uv_per_local);
                    float2 uvTap = clamp(centerUV + offset, cellRect.xy, cellRect.zw);
                    float tapDist = tex2D(_MainTex, uvTap).a - 0.5 + bias;
                    targetDist = isErosion ? min(targetDist, tapDist) : max(targetDist, tapDist);
                }

                [loop]
                for (int k = 0; k < 16; k++)
                {
                    float angle = (k + 0.5) * (6.2831853 / 16.0);
                    float2 offset = float2(cos(angle), sin(angle)) * (radiusPx * uv_per_local);
                    float2 uvTap = clamp(centerUV + offset, cellRect.xy, cellRect.zw);
                    float tapDist = tex2D(_MainTex, uvTap).a - 0.5 + bias;
                    targetDist = isErosion ? min(targetDist, tapDist) : max(targetDist, tapDist);
                }

                float px = targetDist / w;
                return saturate(px * sharpMul + 0.5);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // solid-fill path for mark / underline / selection quads (uv0.z flagged negative)
                if (i.uv0.z < 0.0)
                {
                    fixed4 solid = i.color;
                    #ifdef UNITY_UI_CLIP_RECT
                    solid.a *= UnityGet2DClipping(i.worldPos.xy, _ClipRect);
                    #endif
                    #ifdef UNITY_UI_ALPHACLIP
                    clip(solid.a - 0.001);
                    #endif
                    return solid;
                }
                float2 uvClamp = clamp(i.uv0.xy, i.cellRect.xy, i.cellRect.zw);
                float4 fieldTex = tex2D(_MainTex, uvClamp);
                #ifdef SPERLICH_MTSDF
                float sd     = median3(fieldTex.rgb); // multi-channel median: sharp corners, 0.5 = edge
                float sdSoft = fieldTex.a;            // true SDF: smooth, valid far from the edge
                #else
                float sd     = fieldTex.a; // single-channel SDF, 0..1, 0.5 = edge
                float sdSoft = fieldTex.a;
                #endif
                float d     = sd     - 0.5 + _FaceDilate * 0.5 + i.uv0.w; // face field (sharp corners)
                float dSoft = sdSoft - 0.5 + _FaceDilate * 0.5 + i.uv0.w; // outline / glow / shadow field
                // Alpha the field can still express once fully outside the shape (it clamps there). The
                // glow falloffs subtract this so the flat clamped region reads as zero, not a faint box.
                float dFloor = -0.5 + _FaceDilate * 0.5 + i.uv0.w;

                float fxMode = i.uv1.x;   // 0 = face ; 1 = per-tag outline ; 2 = per-tag glow

                // per-tag outline: a dilated copy of the glyph, drawn behind the face
                if (fxMode > 0.5 && fxMode < 1.5)
                {
                    float ow = i.uv1.y;
                    float uv_per_local = i.uv1.w;
                    float ocCov = sampleOutlineMorphology(ow, i.uv0.xy, i.cellRect, i.uv0.w, _Sharpness, uv_per_local, false);
                    fixed4 oc = i.color;
                    oc.a *= ocCov;
                    #ifdef UNITY_UI_CLIP_RECT
                    oc.a *= UnityGet2DClipping(i.worldPos.xy, _ClipRect);
                    #endif
                    #ifdef UNITY_UI_ALPHACLIP
                    clip(oc.a - 0.001);
                    #endif
                    return oc;
                }

                // fxMode 2: smooth dithered multi-tap glow / bloom (true Gaussian-like blur, no clamping)
                if (fxMode > 1.5 && fxMode < 2.5)
                {
                    float radPx    = max(i.uv1.y, 1.0);
                    bool  bloom    = i.uv1.z < 0.0;
                    float gi       = abs(i.uv1.z);
                    float uv_per_local = i.uv1.w;

                    float blurAlpha = ditheredSpiralBlur(radPx, i.uv0.xy, i.cellRect, i.uv0.w, _Sharpness, i.vertex.xy, (int)_GlowTaps, uv_per_local);

                    fixed4 gc = i.color;
                    if (bloom)
                    {
                        // Blown-out neon: wide soft skirt, hot white core where density is highest
                        float lift = 1.0 - pow(1.0 - blurAlpha, 1.7);
                        gc.rgb = lerp(gc.rgb, float3(1.0, 1.0, 1.0), saturate((blurAlpha - 0.55) * 2.4));
                        gc.a  *= saturate(lift * gi);
                    }
                    else
                    {
                        gc.a *= saturate(pow(blurAlpha, 1.5) * gi);
                    }

                    #ifdef UNITY_UI_CLIP_RECT
                    gc.a *= UnityGet2DClipping(i.worldPos.xy, _ClipRect);
                    #endif
                    #ifdef UNITY_UI_ALPHACLIP
                    clip(gc.a - 0.001);
                    #endif
                    return gc;
                }

                // fxMode 3: smooth dithered multi-tap shadow (true Gaussian-like blur, no clamping)
                if (fxMode > 2.5)
                {
                    float radPx = i.uv1.y;
                    float uv_per_local = i.uv1.w;
                    float shadowCov = ditheredSpiralBlur(radPx, i.uv0.xy, i.cellRect, i.uv0.w, _Sharpness, i.vertex.xy, (int)_ShadowTaps, uv_per_local);

                    fixed4 sc = i.color;
                    sc.a *= saturate(shadowCov);
                    #ifdef UNITY_UI_CLIP_RECT
                    sc.a *= UnityGet2DClipping(i.worldPos.xy, _ClipRect);
                    #endif
                    #ifdef UNITY_UI_ALPHACLIP
                    clip(sc.a - 0.001);
                    #endif
                    return sc;
                }

                // component-level outline as two solid-colour layers (fill composited over outline), so the
                // outline area stays EXACTLY _OutlineColor and the fill stays exactly i.color — only the ~1px
                // AA seam between them blends. _OutlineMode positions the band relative to the glyph edge:
                // Outer (2) [0,+W] · Middle (1) [-W/2,+W/2] · Inner (0) [-W,0].
                float ow = _OutlineWidth;
                float uv_per_local = i.uv1.w;
                float faceBaseA = coverage(d, 0.0, i.uv0.z, _Sharpness);

                float faceA = faceBaseA;
                bool realGlyph = (i.uv1.y <= 0.001); // per-tag shadow copies carry a softness in uv1.y
                if (!realGlyph) faceA = saturate(dSoft / i.uv1.y + 0.5); // soft edge for the per-tag shadow copy

                fixed4 col = i.color;

                if (realGlyph && ow > 0.01)
                {
                    float rOut = (_OutlineMode >= 1.0) ? (_OutlineMode == 2.0 ? ow : ow * 0.5) : 0.0;
                    float rIn  = (_OutlineMode <= 1.0) ? (_OutlineMode == 0.0 ? ow : ow * 0.5) : 0.0;

                    float erodedFillA = (rIn > 0.01) ? sampleOutlineMorphology(rIn, i.uv0.xy, i.cellRect, i.uv0.w, _Sharpness, uv_per_local, true) : faceBaseA;
                    float outerDilatedA = (rOut > 0.01) ? sampleOutlineMorphology(rOut, i.uv0.xy, i.cellRect, i.uv0.w, _Sharpness, uv_per_local, false) : faceBaseA;

                    faceA = erodedFillA;
                    col.a *= faceA;

                    float aFill = col.a; // fill coverage * fill alpha
                    float outlineCov = saturate(outerDilatedA - erodedFillA);
                    float aOut = outlineCov * _OutlineColor.a;
                    float outA = aFill + aOut * (1.0 - aFill);
                    col.rgb = (col.rgb * aFill + _OutlineColor.rgb * aOut * (1.0 - aFill)) / max(outA, 1e-5);
                    col.a   = outA;
                }
                else
                {
                    col.a *= faceA;
                }

                // component-level glow: soft falloff beyond the outline (real glyph only)
                if (realGlyph && _GlowPower > 0.0)
                {
                    float go  = max(_GlowOuter, 1e-4);
                    float g   = 1.0 - saturate((-dSoft)  / go);
                    float gp  = 1.0 - saturate((-dFloor) / go); // pedestal where the field has clamped
                    g = saturate((g - gp) / max(1e-4, 1.0 - gp));
                    g = pow(saturate(g), 1.6) * _GlowPower;
                    fixed4 glow = _GlowColor;
                    // gate by the composited coverage (fill + outline), not the fill alone — otherwise an
                    // Inner/Middle outline lets the glow bleed into the glyph interior and muddies the colour.
                    glow.a *= g * (1.0 - col.a);
                    col.rgb = lerp(col.rgb, glow.rgb, saturate(glow.a));
                    col.a = max(col.a, glow.a);
                }

                // component-level underlay (drop shadow) removed: now handled by emitting a separate quad in TextMeshBuilder

                #ifdef UNITY_UI_CLIP_RECT
                col.a *= UnityGet2DClipping(i.worldPos.xy, _ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(col.a - 0.001);
                #endif

                return col;
            }
        ENDCG
        }
    }
    Fallback "UI/Default"
}
