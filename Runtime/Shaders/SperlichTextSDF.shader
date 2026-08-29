Shader "Sperlich/Text SDF"
{
    Properties
    {
        [PerRendererData] _MainTex ("Glyph Atlas (A = SDF)", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _FaceDilate ("Face Dilate", Range(-0.5,0.5)) = 0.0
        _Sharpness ("Sharpness", Range(0,2)) = 1.0

        _OutlineColor ("Outline Color", Color) = (0,0,0,1)
        _OutlineWidth ("Outline Width", Range(0,0.5)) = 0.0
        [Enum(Inner,0,Middle,1,Outer,2)] _OutlineMode ("Outline Placement", Float) = 2.0

        _UnderlayColor ("Shadow Color", Color) = (0,0,0,0.5)
        _UnderlayOffset ("Shadow Offset (xy) / Softness (z)", Vector) = (0,0,0.05,0)
        _UnderlayDilate ("Shadow Dilate", Range(-0.5,0.5)) = 0.0

        _GlowColor ("Glow Color", Color) = (0.3,0.6,1,0)
        _GlowPower ("Glow Power", Range(0,1)) = 0.0
        _GlowOuter ("Glow Outer", Range(0,0.5)) = 0.25

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
            fixed4 _GlowColor;
            float _GlowPower;
            float _GlowOuter;
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

                float4 fieldTex = tex2D(_MainTex, i.uv0.xy);
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
                    fixed4 oc = i.color;
                    oc.a *= coverage(dSoft, -ow, i.uv0.z, _Sharpness);
                    #ifdef UNITY_UI_CLIP_RECT
                    oc.a *= UnityGet2DClipping(i.worldPos.xy, _ClipRect);
                    #endif
                    #ifdef UNITY_UI_ALPHACLIP
                    clip(oc.a - 0.001);
                    #endif
                    return oc;
                }

                // per-tag glow: a ring blur of the glyph coverage, spreading into the transparent
                // padding baked around the cell (Msdf Glow Padding). i.uv1.y = blur radius in UV units,
                // i.uv1.z = intensity, i.cellRect = (u0,v0,u1,v1) of this glyph's cell so taps that reach
                // past it fold back onto its transparent border instead of a neighbour glyph.
                if (fxMode > 1.5)
                {
                    float radUv = max(i.uv1.y, 1e-5);
                    float gi    = i.uv1.z;
                    float4 cr   = i.cellRect;
                    bool  bloom = i.uv1.w > 0.5;

                    // Two rings of 8 taps. bloom spreads the outer ring further out and weights it
                    // heavier for a wide soft skirt. Each tap is glyph coverage, clamped into this cell.
                    float outer = bloom ? 1.35 : 1.0;
                    float falloff = bloom ? 1.1 : 2.3;
                    float acc = 0.0, wsum = 0.0;
                    [unroll] for (int k = 0; k < 16; k++)
                    {
                        float rr   = (k < 8) ? 0.5 : outer;
                        float ang  = (k - (k < 8 ? 0.0 : 8.0)) * 0.78539816; // 45° steps
                        float2 off = float2(cos(ang), sin(ang)) * radUv * rr;
                        float2 uv  = clamp(i.uv0.xy + off, cr.xy, cr.zw);
                        float samp = tex2D(_MainTex, uv).a;          // true SDF / SDF alpha: 0.5 = edge
                        float cov  = smoothstep(0.42, 0.58, samp);
                        float wt   = exp(-falloff * rr * rr);
                        acc += cov * wt; wsum += wt;
                    }
                    float halo = acc / max(wsum, 1e-4);

                    fixed4 gc = i.color;
                    if (bloom)
                    {
                        // blown-out neon: wide soft skirt, and a white-hot core where coverage is densest
                        float lift = 1.0 - pow(1.0 - saturate(halo), 1.7);
                        gc.rgb = lerp(gc.rgb, float3(1.0, 1.0, 1.0), saturate((halo - 0.55) * 2.4));
                        gc.a  *= saturate(lift * gi);
                    }
                    else
                    {
                        gc.a *= saturate((1.0 - pow(1.0 - saturate(halo), 2.2)) * gi);
                    }
                    #ifdef UNITY_UI_CLIP_RECT
                    gc.a *= UnityGet2DClipping(i.worldPos.xy, _ClipRect);
                    #endif
                    #ifdef UNITY_UI_ALPHACLIP
                    clip(gc.a - 0.001);
                    #endif
                    return gc;
                }

                // component-level outline as two solid-colour layers (fill composited over outline), so the
                // outline area stays EXACTLY _OutlineColor and the fill stays exactly i.color — only the ~1px
                // AA seam between them blends. _OutlineMode positions the band relative to the glyph edge
                // (positive = inside the glyph):  Outer (2) [-W,0] · Middle (1) [-W/2,+W/2] · Inner (0) [0,+W].
                float ow      = _OutlineWidth;
                float bandIn  = (ow > 0.0) ? ((2.0 - _OutlineMode) * 0.5 * ow) : 0.0; // where the fill starts
                float bandOut = bandIn - ow;                                          // outer edge of the outline

                float faceA = coverage(d, bandIn, i.uv0.z, _Sharpness);
                bool realGlyph = (i.uv1.y <= 0.001); // per-tag shadow copies carry a softness in uv1.y
                if (!realGlyph) faceA = saturate(dSoft / i.uv1.y + 0.5); // soft edge for the per-tag shadow copy
                fixed4 col = i.color;
                col.a *= faceA;

                if (realGlyph && ow > 0.0)
                {
                    float aFill = col.a; // fill coverage * fill alpha
                    float aOut  = coverage(dSoft, bandOut, i.uv0.z, _Sharpness) * _OutlineColor.a;
                    float outA  = aFill + aOut * (1.0 - aFill);
                    col.rgb = (col.rgb * aFill + _OutlineColor.rgb * aOut * (1.0 - aFill)) / max(outA, 1e-5);
                    col.a   = outA;
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

                // component-level underlay (drop shadow): sample the field at an offset (real glyph only)
                if (realGlyph && _UnderlayColor.a > 0.0)
                {
                    float2 uo = _UnderlayOffset.xy * _MainTex_TexelSize.xy * 64.0;
                    float ssd = tex2D(_MainTex, i.uv0.xy - uo).a;
                    float sdShadow = ssd - 0.5 + _UnderlayDilate * 0.5 + i.uv0.w;
                    float softness = max(_UnderlayOffset.z, 1e-4);
                    float shadowA = saturate((sdShadow) / softness + 0.5) * _UnderlayColor.a;
                    fixed4 sh = fixed4(_UnderlayColor.rgb, shadowA * (1.0 - col.a));
                    col.rgb = lerp(sh.rgb, col.rgb, col.a);
                    col.a = max(col.a, sh.a);
                }

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
