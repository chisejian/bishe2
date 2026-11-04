#ifndef GAFA_ILLUSTRATED_INK_INCLUDED
#define GAFA_ILLUSTRATED_INK_INCLUDED

// 墨线缓冲（移植自 illustrated/src/materials.js 的 INK_OUT）：每像素写 (八面体编码法线.xy, 物体 / 叶团编号, 线型)
//   线型 0 不描线（草、藤、落叶）
//        1 实体（建筑、树干、地面、物件）：轮廓 + 明显折边
//        2 树叶团：只在不同叶团 / 背景交界描线
// 由 GafaIllustratedFeature 的 "GafaInk" 通道绘制，后期据此只描大轮廓。

float2 GafaOctEncode(float3 n)
{
    n /= (abs(n.x) + abs(n.y) + abs(n.z));
    float2 e = n.z >= 0.0 ? n.xy : (1.0 - abs(n.yx)) * float2(n.x >= 0.0 ? 1.0 : -1.0, n.y >= 0.0 ? 1.0 : -1.0);
    return e;
}

float3 GafaOctDecode(float2 e)
{
    float3 n = float3(e, 1.0 - abs(e.x) - abs(e.y));
    float t = max(-n.z, 0.0);
    n.x += n.x >= 0.0 ? -t : t;
    n.y += n.y >= 0.0 ? -t : t;
    return normalize(n);
}

#endif
