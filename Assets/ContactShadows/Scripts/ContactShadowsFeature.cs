
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using PostEffects;
using UnityEngine.Rendering.Universal.Internal;

public class ContactShadowsFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class ContactShadowsFeatureSettings
    {
        [Range(0, 10.05f)] public float _rejectionDepth = 0.5f;
        [Range(4, 32)] public int _sampleCount = 16;
        [Range(0, 1)] public float _temporalFilter = 0.5f;
        public NoiseTextureSet _noiseTextures;
        [HideInInspector] public Texture _DefaultTexture;
        public bool _downsample = false;
        public LayerMask _opaqueMask = ~0;
        public RenderPassEvent _insertPosition;
    }

    RenderTargetHandle m_DepthTextureTemp;
    RenderTargetHandle m_DepthTexture;

    // MUST be named "settings" (lowercase) to be shown in the Render Features inspector
    public ContactShadowsFeatureSettings settings = new ContactShadowsFeatureSettings();
    ContactShadowsRenderPass contactShadowRenderPass;
    InsertContactShadowsRenderPass insertContactShadowRenderPass;
    DepthOnlyPass m_DepthPrepass;

    public override void Create()
    {        
        // 
        m_DepthPrepass = new DepthOnlyPass(settings._insertPosition - 10, RenderQueueRange.opaque, settings._opaqueMask);

        contactShadowRenderPass = new ContactShadowsRenderPass(
          settings._insertPosition - 9,
          settings._noiseTextures
          );

        insertContactShadowRenderPass = new InsertContactShadowsRenderPass(
            RenderPassEvent.AfterRenderingPostProcessing
            );

        m_DepthTextureTemp.Init("_CameraDepthTextureTemp");
        m_DepthTexture.Init("_CameraDepthTexture");

    }

    // called every frame once per camera
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        // Gather up and pass any extra information our pass will need.
        // In this case we're getting the camera's color buffer target
        // Ask the renderer to add our pass.
        // Could queue up multiple passes and/or pick passes to use
        RenderTextureDescriptor cameraTargetDescriptor = renderingData.cameraData.cameraTargetDescriptor;
        m_DepthPrepass.Setup(cameraTargetDescriptor, m_DepthTextureTemp);
        renderer.EnqueuePass(m_DepthPrepass);

        contactShadowRenderPass.Setup(settings, renderer.cameraColorTarget, m_DepthTextureTemp.Identifier());
        renderer.EnqueuePass(contactShadowRenderPass);

        insertContactShadowRenderPass.Setup(contactShadowRenderPass.GetBlank());
        renderer.EnqueuePass(insertContactShadowRenderPass);
    }
}