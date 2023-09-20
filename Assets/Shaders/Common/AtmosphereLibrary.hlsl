#ifndef _ATMOSPHERE_LIBRARY_INCLUDED
#define _ATMOSPHERE_LIBRARY_INCLUDED

// HG相位函数
float HenyeyGreenstein(float G, float CosTheta) {
    return ((1.0 - G * G) / pow(abs(1.0 + G * G - 2.0 * G * CosTheta), 1.5)) / (4 * 3.1415);
}

// 云相位函数
float Phase(float3 lightDir, float3 viewDir, float forwardPhaseG, float backPhaseG, float phaseBlend) {
    float cosTheta = dot(lightDir, viewDir);

    float inScatterHg = HenyeyGreenstein(forwardPhaseG, cosTheta);
    float outScatterHg = HenyeyGreenstein(backPhaseG, cosTheta);

    return lerp(inScatterHg, outScatterHg, phaseBlend);
}

// 内散射
// rho = s/(s+a)
// rho = s/t
float3 InScattering(float3 lightDir, float3 viewDir, float3 extinction, float3 albedo, float forwardPhaseG, float backPhaseG, float phaseBlend) {
    float phase = Phase(lightDir, viewDir, forwardPhaseG, backPhaseG, phaseBlend);
    float3 scattering = albedo * extinction;
    return scattering * phase;
}

// TODO: 测试下面函数
// 糖分内散射
// SIG 2015中的“糖粉效果”
float GetPowderEffect(float Density, float CosTheta) {
    float powd = 1.0 - exp(-Density * 2.0); // 这里的常数2.0可以根据需要暴露出去
    return lerp(1.0, powd, saturate((-CosTheta * 0.5) + 0.5)); // [-1,1]->[0,1]
}
#endif
