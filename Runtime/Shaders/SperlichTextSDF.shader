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
            #pragma target 2.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

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
                o.color = v.color * _Color;
                return o;
            }

            // signed distance -> coverage with screen-space anti-aliasing
            float coverage(float dist, float threshold, float sharpen)
            {
                float aa = fwidth(dist) * max(0.0001, 2.0 - sharpen);
                return saturate((dist - threshold) / max(aa, 1e-5) + 0.5);
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

                float sd = tex2D(_MainTex, i.uv0.xy).a; // 0..1, 0.5 = edge
                float d = sd - 0.5 + _FaceDilate * 0.5 + i.uv0.w; // weight bias from vertex

                float fxMode = i.uv1.x;   // 0 = face ; 1 = per-tag outline ; 2 = per-tag glow

                // per-tag outline: a dilated copy of the glyph, drawn behind the face
                if (fxMode > 0.5 && fxMode < 1.5)
                {
                    float ow = i.uv1.y;
                    fixed4 oc = i.color;
                    oc.a *= coverage(d, -ow, _Sharpness);
                    #ifdef UNITY_UI_CLIP_RECT
                    oc.a *= UnityGet2DClipping(i.worldPos.xy, _ClipRect);
                    #endif
                    #ifdef UNITY_UI_ALPHACLIP
                    clip(oc.a - 0.001);
                    #endif
                    return oc;
                }

                // per-tag glow: soft radial-ish falloff from the glyph edge outward
                if (fxMode > 1.5)
                {
                    float gr = max(i.uv1.y, 1e-4);
                    float gi = i.uv1.z;
                    float t = saturate(1.0 + d / gr);       // 0 far outside .. 1 at/inside the edge
                    float a = pow(t, 2.5) * gi;
                    fixed4 gc = i.color;
                    gc.a *= saturate(a);
                    #ifdef UNITY_UI_CLIP_RECT
                    gc.a *= UnityGet2DClipping(i.worldPos.xy, _ClipRect);
                    #endif
                    #ifdef UNITY_UI_ALPHACLIP
                    clip(gc.a - 0.001);
                    #endif
                    return gc;
                }

                float faceA = coverage(d, 0.0, _Sharpness);
                bool realGlyph = (i.uv1.y <= 0.001); // per-tag shadow copies carry a softness in uv1.y
                if (!realGlyph) faceA = saturate(d / i.uv1.y + 0.5); // soft edge for the per-tag shadow copy
                fixed4 col = i.color;
                col.a *= faceA;

                // component-level outline: band just outside the face edge (real glyph only)
                if (realGlyph && _OutlineWidth > 0.0)
                {
                    float outlineA = coverage(d, -_OutlineWidth, _Sharpness);
                    fixed4 o = _OutlineColor;
                    o.a *= outlineA;
                    col = lerp(o, col, faceA);
                    col.a = max(col.a, o.a);
                }

                // component-level glow: soft falloff beyond the outline (real glyph only)
                if (realGlyph && _GlowPower > 0.0)
                {
                    float g = 1.0 - saturate((-d) / max(_GlowOuter, 1e-4));
                    g = pow(saturate(g), 2.0) * _GlowPower;
                    fixed4 glow = _GlowColor;
                    glow.a *= g * (1.0 - faceA);
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
