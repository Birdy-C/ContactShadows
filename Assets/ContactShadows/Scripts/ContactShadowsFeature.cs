
using UnityEngine;
using UnityEngine.Rendering.Universal;
using PostEffects;
public class ContactShadowsFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class ContactShadowsFeatureSettings
    {
        [Range(0, 0.05f)] public float _rejectionDepth = 0.5f;
        [Range(4, 32)] public int _sampleCount = 16;
        [Range(0, 1)] public float _temporalFilter = 0.5f;
        public NoiseTextureSet _noiseTextures;
        [HideInInspector] public Texture _DefaultTexture;
        public bool _downsample = false;
    }


    // MUST be named "settings" (lowercase) to be shown in the Render Features inspector
    public ContactShadowsFeatureSettings settings = new ContactShadowsFeatureSettings();
    ContactShadowsRenderPass contactShadowRenderPass;
    InsertContactShadowsRenderPass insertContactShadowRenderPass;
    public override void Create()
    {
        contactShadowRenderPass = new ContactShadowsRenderPass(
          RenderPassEvent.AfterRendering,
          settings._noiseTextures
          );

        insertContactShadowRenderPass = new InsertContactShadowsRenderPass(
            RenderPassEvent.BeforeRenderingPrepasses
            );
    }

    // called every frame once per camera
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        // Gather up and pass any extra information our pass will need.
        // In this case we're getting the camera's color buffer target
        contactShadowRenderPass.Setup(settings);
        insertContactShadowRenderPass.Setup(contactShadowRenderPass.GetRenderTexture(renderingData.cameraData.camera));
        // Ask the renderer to add our pass.
        // Could queue up multiple passes and/or pick passes to use
        renderer.EnqueuePass(contactShadowRenderPass);
        renderer.EnqueuePass(insertContactShadowRenderPass);
    }
}