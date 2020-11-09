// Experimental implementation of contact shadows for Unity
// https://github.com/keijiro/ContactShadows

#include "Common.cginc"

// Shadow mask texture

// Noise texture (used for dithering)
sampler2D _NoiseTex;
float2 _NoiseScale;

// Light vector
// (reversed light direction in view space) * (ray-trace sample interval)
float3 _LightVector;

// Depth rejection threshold that determines the depth of each pixels.
float _RejectionDepth;

// Total sample count
uint _SampleCount;

// Fragment shader - Screen space ray-trancing shadow pass
half4 FragmentShadow(float2 uv : TEXCOORD) : SV_Target
{
    // Temporal distributed noise offset
    float offs = tex2D(_NoiseTex, uv * _NoiseScale).a;

    // View space position of the origin
    float z0 = SampleRawDepth(uv);
    if (z0 > 0.999999) return 1.0; // BG early-out
    float3 vp0 = InverseProjectUVZ(uv, z0);

    // Ray-tracing loop from the origin along the reverse light direction
    UNITY_LOOP for (uint i = 0; i < _SampleCount; i++)
    {
        // View space position of the ray sample
        float3 vp_ray = vp0 + _LightVector * (i + offs * 2);

        // View space position of the depth sample
        float3 vp_depth = InverseProjectUV(ProjectVP(vp_ray));

        // Depth difference between ray/depth sample
        // Negative: Ray sample is closer to the camera (not occluded)
        // Positive: Ray sample is beyond the depth sample (possibly occluded)
        float diff = vp_ray.z - vp_depth.z;

        // Occlusion test
        if (diff > 0.01 * (1 - offs) && diff < _RejectionDepth) return 0;
    }

    return 1.0;
}
