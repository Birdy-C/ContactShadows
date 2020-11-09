
using UnityEngine;
using UnityEngine.Rendering.Universal;
using PostEffects;
public class ContactShadowsFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class ContactShadowsFeatureSettings
    {
        public RenderPassEvent WhenToInsert = RenderPassEvent.AfterRendering;
        [Range(0, 5)] public float _rejectionDepth = 0.5f;
        [Range(4, 32)] public int _sampleCount = 16;
        [Range(0, 1)] public float _temporalFilter = 0.5f;
        public bool _downsample = false;
        public NoiseTextureSet _noiseTextures;
        public Texture _ContactShadowTexture;
    }

    // MUST be named "settings" (lowercase) to be shown in the Render Features inspector
    public ContactShadowsFeatureSettings settings = new ContactShadowsFeatureSettings();
    ContactShadowsRenderPass myRenderPass;

    public override void Create()
    {
        myRenderPass = new ContactShadowsRenderPass(
          settings.WhenToInsert,
          settings._noiseTextures
        );
    }

    // called every frame once per camera
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        // Gather up and pass any extra information our pass will need.
        // In this case we're getting the camera's color buffer target
        myRenderPass.Setup(settings._rejectionDepth, settings._sampleCount, settings._temporalFilter, settings._ContactShadowTexture);
        // Ask the renderer to add our pass.
        // Could queue up multiple passes and/or pick passes to use
        renderer.EnqueuePass(myRenderPass);
    }
}