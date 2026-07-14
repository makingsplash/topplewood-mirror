#ifndef EFFECTS_INCLUDED
#define EFFECTS_INCLUDED

half Blend(const half value, const half blend, const half factor)
{
    const half ov = 1;
    const half hv = 0.5;
    const half st = step(hv, value);

    return lerp(value, ((ov - st) * (hv - (ov - value) * (ov - blend)) + st * value * blend) * 2, factor);
}

half3 Blend(const half3 value, const half3 blend, const half factor)
{
    const half3 ov = fixed3(1, 1, 1);
    const half3 hv = fixed3(0.5, 0.5, 0.5);
    const half3 st = step(hv, value);

    return lerp(value, ((ov - st) * (hv - (ov - value) * (ov - blend)) + st * value * blend) * 2, factor);
}

half3 Overlay(const half3 color, const half3 blend, const half factor)
{
    return Blend(color, blend, factor);
}

half3 Overlay(const half3 color, const half3 blend)
{
    return Overlay(color, blend, 1);
}

half3 Grayscale(const half3 color, const half3 tint, const half factor)
{
    const half gray = color.r * 0.299 + color.g * 0.587 + color.b * 0.114;
    return lerp(color, half3(gray, gray, gray) * tint, factor);
}

half3 Grayscale(const half3 color, const half factor)
{
    const half3 tint = half3(1, 1, 1);
    return Grayscale(color, tint, factor);
}

#endif // EFFECTS_INCLUDED