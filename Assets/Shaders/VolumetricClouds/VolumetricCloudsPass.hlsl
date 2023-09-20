#ifndef _VOLUMETRICCLOUDS_PASS_INCLUDED
#define _VOLUMETRICCLOUDS_PASS_INCLUDED

#include "../Common/ScreenSpaceLibrary.hlsl"
#include "../Common/AtmosphereLibrary.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"

TEXTURE3D(_CloudsFBMTexture);
SAMPLER(sampler_CloudsFBMTexture);

TEXTURE3D(_CloudsDetailNoiseTexture);
SAMPLER(sampler_CloudsDetailNoiseTexture);

TEXTURE2D(_WeatherTexture);
SAMPLER(sampler_WeatherTexture);

TEXTURE2D(_MaskNoiseTexture);
SAMPLER(sampler_MaskNoiseTexture);

float4 _CloudsBoundsMin;
float4 _CloudsBoundsMax;

float4 _CloudsShapeParams;
#define CLOUDSSHAPETILING _CloudsShapeParams.x
#define HEIGHTWEIGHT _CloudsShapeParams.y
#define NOISESTRENGTH _CloudsShapeParams.z
#define EDGEFADEPROPORTION _CloudsShapeParams.w

float4 _CloudsShapeParams2;
#define DETAILTILING _CloudsShapeParams2.x
#define DETAILWEIGHT _CloudsShapeParams2.y
#define DETAILNOISEWEIGHT _CloudsShapeParams2.z
#define DETAILNOISESTRENGTH _CloudsShapeParams2.w

float4 _CloudsFBMWeights;

float4 _SpeedParams;

float _DensityThreshold;

float4 _StepParams;
#define STEP_COUNT _StepParams.x
#define STRIDE _StepParams.y
#define LIGHT_STEP_COUNT _StepParams.z

float4 _DensityParams;
#define DENSITYOFFSET _DensityParams.x
#define DENSITYMULTIPLIER _DensityParams.y

float4 _SunLightColor;
float _SunLightIntensity;

float4 _Albedo;
float4 _PhaseParams;
#define FORWARDPHASEG _PhaseParams.x
#define BACKPHASEG _PhaseParams.y
#define PHASEBLEND _PhaseParams.x

float4 _ColorA;
float4 _ColorB;
float4 _ColorOffsets;
#define COLOROFFSET1 _ColorOffsets.x
#define COLOROFFSET2 _ColorOffsets.y
float _LightAbsorptionTowardSun;
float _LightAbsorptionThroughClouds;
float _DarknessThreshold;

half4 GetSource(float2 uv) {
    return SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, _BlitMipLevel);
}


float remap(float original_value, float original_min, float original_max, float new_min, float new_max) {
    return new_min + (((original_value - original_min) / (original_max - original_min)) * (new_max - new_min));
}

float SampleDensity(float3 rayPos) {
    float4 boundsCentre = (_CloudsBoundsMax + _CloudsBoundsMin) * 0.5;
    float3 size = _CloudsBoundsMax - _CloudsBoundsMin;

    // 采样形状
    float2 speedShape = _Time.y * _SpeedParams.xy;
    float3 uvwShape = (rayPos + _WorldSpaceCameraPos) * CLOUDSSHAPETILING + float3(speedShape, 0.0);

    // remap rayPos to [0, 1]
    float2 uv = (size.xz * 0.5f + (rayPos.xz - boundsCentre.xz)) / max(size.x, size.y);

    // 天气图，作乘用作云的有无，以及高度渐变
    float4 weatherMap = SAMPLE_TEXTURE2D_LOD(_WeatherTexture, sampler_WeatherTexture, uv + speedShape * 0.5, 0.0);
    // 扰动图
    float4 noise = SAMPLE_TEXTURE2D_LOD(_MaskNoiseTexture, sampler_MaskNoiseTexture, uv + speedShape * 0.4, 0.0);

    // FBM
    float4 shapeNoise = SAMPLE_TEXTURE3D_LOD(_CloudsFBMTexture, sampler_CloudsFBMTexture, uvwShape + noise.r * NOISESTRENGTH, 0.0);
    float4 normalizedShapeWeights = _CloudsFBMWeights / dot(_CloudsFBMWeights, 1);
    float shapeFBM = dot(shapeNoise, normalizedShapeWeights);

    // height
    float heightPercent = (rayPos.y - _CloudsBoundsMin.y) / size.y;
    float gMin = remap(weatherMap.r, 0.0, 1.0, 0.1, 0.6); // 得到云的下部分
    float gMax = remap(weatherMap.r, 0.0, 1.0, gMin, 0.9); // 得到云的上部分
    // 基础高度
    float heightGradient = saturate(remap(heightPercent, 0.0, gMin, 0, 1)) * saturate(remap(heightPercent, 1, gMax, 0, 1));
    // 天气图高度
    float heightGradient2 = saturate(remap(heightPercent, 0.0, weatherMap.r, 1, 0)) * saturate(remap(heightPercent, 0.0, gMin, 0, 1));
    // 两种高度混合
    heightGradient = saturate(lerp(heightGradient, heightGradient2, HEIGHTWEIGHT));

    // edge fade
    float containerEdgeFadeDist = size * EDGEFADEPROPORTION;
    float distFromEdgeX = min(containerEdgeFadeDist, min(rayPos.x - _CloudsBoundsMin.x, _CloudsBoundsMax.x - rayPos.x));
    float distFromEdgeZ = min(containerEdgeFadeDist, min(rayPos.z - _CloudsBoundsMin.z, _CloudsBoundsMax.z - rayPos.z));
    float edgeWeight = min(distFromEdgeZ, distFromEdgeX) / containerEdgeFadeDist;
    heightGradient *= edgeWeight;

    // detail
    float2 speedDetail = _Time.y * _SpeedParams.zw;
    float3 uvwDetail = (rayPos + _WorldSpaceCameraPos) * DETAILTILING + float3(speedDetail, 0.0);
    float4 detailNoise = SAMPLE_TEXTURE3D_LOD(_CloudsDetailNoiseTexture, sampler_CloudsDetailNoiseTexture, uvwDetail + shapeNoise.r * DETAILNOISESTRENGTH, 0.0);

    float density = shapeFBM * heightGradient + DENSITYOFFSET;

    if (density > 0.0) {
        float detailFBM = pow(detailNoise.r, DETAILWEIGHT);

        float oneMinusShape = 1 - density;
        float detailErodeWeight = oneMinusShape * oneMinusShape * oneMinusShape;

        density = density - detailFBM * detailErodeWeight * DETAILNOISEWEIGHT;

        return saturate(density * DENSITYMULTIPLIER);
    }

    return 0.0;
}


float3 TransmittanceToSun(float3 spos, float3 dir) {
    float distInsideBox = rayBoxDst(_CloudsBoundsMin, _CloudsBoundsMax, spos, rcp(dir)).y;
    float stride = distInsideBox / LIGHT_STEP_COUNT;
    float totalDensity = 0.0;
    
    UNITY_LOOP
    for (int i = 0; i < LIGHT_STEP_COUNT; i++) {
        spos += dir * stride;
        totalDensity += max(0.0, SampleDensity(spos));
    }
    float transmittance = exp(-totalDensity * _LightAbsorptionTowardSun);

    float3 lightColor = GetMainLight().color;
    float3 cloudsColor = lerp(_ColorA, lightColor, saturate(transmittance * COLOROFFSET1));
    cloudsColor = lerp(_ColorB, cloudsColor, saturate(pow(transmittance * COLOROFFSET2, 3)));
    return _DarknessThreshold + transmittance * (1 - _DarknessThreshold) * cloudsColor;
}

// float3 cloudRaymarch(float3 spos, float3 dir, float stepDist) {
//     float3 sunLuminance = _SunLightColor * _SunLightIntensity;
//
//     float dist = 0.0;
//     float opticalDepth = 0.0f;
//
//     float3 color = 0.0;
//
//     float3 lightDir = normalize(GetMainLight().direction);
//
//     UNITY_LOOP
//     for (int i = 0; i < STEP_COUNT && dist < stepDist; i++) {
//         float3 p = spos + dist * dir;
//
//         float density = SampleDensity(p, TEXTURE3D_ARGS(_CloudsDensityNoiseTexture, sampler_CloudsDensityNoiseTexture));
//         opticalDepth += density * STRIDE;
//
//         float3 t1 = TransmittanceToSun(p, lightDir); // transmittance to light
//         float3 s = InScattering(lightDir, GetWorldSpaceNormalizeViewDir(p), density, _Albedo.rgb, FORWARDPHASEG, BACKPHASEG, PHASEBLEND); // In-Scattering
//         float t2 = exp(-opticalDepth); // transmittance to camera
//
//         color += sunLuminance * t1 * s * t2 * STRIDE;
//
//         dist += STRIDE;
//     }
//
//     return color;
// }


float4 cloudRaymarch(float3 spos, float3 dir, float stepDist) {
    float dist = 0.0;
    float sumDensity = 1.0;
    float3 lightDir = normalize(GetMainLight().direction);

    float3 color = 0.0;
    
    UNITY_LOOP
    for (int i = 0; i < STEP_COUNT && dist < stepDist; i++) {
        float3 p = spos + dist * dir;

        float density = SampleDensity(p);

        if (density > 0) {
            float3 t1 = TransmittanceToSun(p, lightDir);
            color += density * STRIDE * sumDensity * t1;
            sumDensity *= exp(-density * STRIDE * _LightAbsorptionThroughClouds);
        
            if (sumDensity < 0.01) break;
        }

        dist += STRIDE;
    }

    return float4(color, sumDensity);
}


half4 VolumetricCloudsPassFragment(Varyings input) : SV_Target {
    float2 uv = input.texcoord;
    float rawDepth = SampleSceneDepth(uv);
    float linearDepth = LinearEyeDepth(rawDepth, _ZBufferParams);

    float3 wpos = ReconstructWorldSpaceViewPos(uv, linearDepth) + _WorldSpaceCameraPos;
    float3 vDir = normalize(wpos);

    half3 sceneColor = GetSource(uv);

    // 计算与bounds相交信息
    float2 hitBoundsInfo = rayBoxDst(_CloudsBoundsMin, _CloudsBoundsMax, rcp(vDir));
    float distToBox = hitBoundsInfo.x;
    float distInsideBox = hitBoundsInfo.y;
    float sceneDist = length(wpos);
    // 计算到物体距离减到bounds距离，即真正需要步近的距离
    // 和distInsideBox 取最小判断是否在box内 或被遮挡
    float stepDist = min(sceneDist - distToBox, distInsideBox);

    // 不与bounds相交 直接返回
    if (stepDist < 0.0001) return half4(sceneColor, 1.0);

    float3 hitPoint = vDir * distToBox;

    float4 cloudColor = cloudRaymarch(hitPoint, vDir, stepDist);

    half3 finalCol = cloudColor.rgb + sceneColor * cloudColor.a;

    return half4(finalCol, 1.0);
}

#endif
