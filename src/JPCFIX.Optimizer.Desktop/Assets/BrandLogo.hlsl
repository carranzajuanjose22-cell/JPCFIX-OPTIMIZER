// Native WPF presentation of the original opaque artwork on a dark header.
// White paper becomes transparent; the original ink silhouette becomes white.
sampler2D source : register(s0);
float4 main(float2 uv : TEXCOORD) : COLOR
{
    float4 color = tex2D(source, uv);
    float alpha = saturate((1.0 - min(color.r, min(color.g, color.b))) * 6.0) * color.a;
    return float4(alpha, alpha, alpha, alpha);
}
