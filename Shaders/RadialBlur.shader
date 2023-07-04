Shader "Hidden/Volumetric/RadialBlur"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _BlurWidth("Blur Width", Range(0,1)) = 0.85
        _Intensity("Intensity", Range(0,1)) = 1
        _Center("Center", Vector) = (0.5,0.5,0,0)
    }
    SubShader
    {
        Blend One One

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            sampler2D _MainTex;
            #define NUM_SAMPLES 100

            float _BlurWidth;
            float _Intensity;
            float4 _Center;

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 color = fixed4(0.0f, 0.0f, 0.0f, 1.0f);
                
                float2 ray = i.uv - _Center.xy;
                
                for (int i = 0; i < NUM_SAMPLES; i++)
                {
                    float scale = 1.0f - _BlurWidth * (float(i) /
                        float(NUM_SAMPLES - 1));
                    color.xyz += tex2D(_MainTex, (ray * scale) +
                                       _Center.xy).xyz / float(NUM_SAMPLES);
                }
                
                return color * _Intensity;
            }
            ENDCG
        }
    }
}
