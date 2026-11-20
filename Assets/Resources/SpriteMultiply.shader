// 絵を「乗算」で重ねる(下にあるものの色に、絵の色を掛ける。白い所は変わらず、灰色の所は暗くなる)。
// 透明な所は掛けない(白と同じ)。色(SpriteRenderer の Color)の A で、掛ける強さを弱められる。
// TwoPlayerShop2D の手前のレジ袋の絵(Bag Front Multiply)で使う。Resources に置いて、ビルドにも入るようにしている
Shader "Shop2D/SpriteMultiply"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
        Cull Off
        ZWrite Off
        Lighting Off
        Blend DstColor Zero

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };

            sampler2D _MainTex;
            fixed4 _Color;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv) * i.color;
                return fixed4(lerp(fixed3(1, 1, 1), c.rgb, c.a), 1);   // 透明な所・A が小さい所は白(= 掛けても変わらない)
            }
            ENDCG
        }
    }
}
