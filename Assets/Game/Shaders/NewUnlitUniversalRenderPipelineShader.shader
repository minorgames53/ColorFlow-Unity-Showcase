Shader "Game/Connected Box Two Color"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}

        _ColorA ("Color A", Color) = (1, 1, 1, 1)
        _ColorB ("Color B", Color) = (1, 1, 1, 1)

        _Split ("Split Position", Range(0, 1)) = 0.5

        _StrokeColor ("Stroke Color", Color) = (0.15, 0.18, 0.25, 1)
        _StrokeThickness ("Stroke Thickness", Range(0, 8)) = 1
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "CanUseSpriteAtlas" = "True"
        }

        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "TwoColorSpriteWithStroke"

            Tags
            {
                "LightMode" = "Universal2D"
            }

            HLSLPROGRAM

            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            // Unity tarafından otomatik sağlanır:
            // x = 1 / texture width
            // y = 1 / texture height
            // z = texture width
            // w = texture height
            float4 _MainTex_TexelSize;

            CBUFFER_START(UnityPerMaterial)
                half4 _ColorA;
                half4 _ColorB;

                float _Split;

                half4 _StrokeColor;
                float _StrokeThickness;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;

                output.positionCS =
                    TransformObjectToHClip(input.positionOS.xyz);

                output.uv = input.uv;
                output.color = input.color;

                return output;
            }

            half SampleAlpha(float2 uv)
            {
                return SAMPLE_TEXTURE2D(
                    _MainTex,
                    sampler_MainTex,
                    uv
                ).a;
            }

            half GetExpandedAlpha(float2 uv, float2 offset)
            {
                half expandedAlpha = 0;

                // Yalnız üst ve alt yönde genişlet.
                expandedAlpha = max(
                    expandedAlpha,
                    SampleAlpha(uv + float2(0, offset.y))
                );

                expandedAlpha = max(
                    expandedAlpha,
                    SampleAlpha(uv - float2(0, offset.y))
                );

                return expandedAlpha;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 textureColor =
                    SAMPLE_TEXTURE2D(
                        _MainTex,
                        sampler_MainTex,
                        input.uv
                    );

                // Keskin iki renk ayrımı.
                half rightSide = step(_Split, input.uv.x);

                half4 splitColor =
                    lerp(_ColorA, _ColorB, rightSide);

                half centerAlpha = textureColor.a;

                // Stroke kalınlığını texture pixel değerinden UV offset'e çevir.
                float2 strokeOffset =
                    _MainTex_TexelSize.xy *
                    max(_StrokeThickness, 0);

                half expandedAlpha = centerAlpha;

                if (_StrokeThickness > 0.001)
                {
                    expandedAlpha = max(
                        centerAlpha,
                        GetExpandedAlpha(
                            input.uv,
                            strokeOffset
                        )
                    );
                }

                // Yalnız sprite'ın dışında kalan genişletilmiş alan.
                half strokeMask =
                    saturate(expandedAlpha - centerAlpha);

                // Alpha kenarlarında daha belirgin bir stroke sağlar.
                strokeMask = saturate(strokeMask * 4);

                half fillAlpha =
                    centerAlpha *
                    splitColor.a *
                    input.color.a;

                half strokeAlpha =
                    strokeMask *
                    _StrokeColor.a *
                    input.color.a;

                half finalAlpha =
                    saturate(
                        fillAlpha +
                        strokeAlpha * (1 - fillAlpha)
                    );

                half3 fillColor =
                    textureColor.rgb *
                    splitColor.rgb *
                    input.color.rgb;

                // Straight-alpha blending için renkleri doğru birleştir.
                half3 premultipliedColor =
                    fillColor * fillAlpha +
                    _StrokeColor.rgb *
                    strokeAlpha *
                    (1 - fillAlpha);

                half3 finalColor =
                    premultipliedColor /
                    max(finalAlpha, 0.0001);

                return half4(
                    finalColor,
                    finalAlpha
                );
            }

            ENDHLSL
        }
    }
}
