Shader "Hidden/Atmosphere/VolumetricClouds"{
    Properties{}
    SubShader{
        Tags{
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
        }

        LOD 200
        Cull Off ZWrite Off ZTest Always

        Pass{
            Name "Volumetric Clouds Pass"


            HLSLPROGRAM
            
            #include "VolumetricCloudsPass.hlsl"
            
            #pragma vertex Vert
            #pragma fragment VolumetricCloudsPassFragment
            
            ENDHLSL
        }
    }
}