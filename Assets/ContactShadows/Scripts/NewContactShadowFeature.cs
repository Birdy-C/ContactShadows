using System.Collections.Generic;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering;
using UnityEngine.Scripting.APIUpdating;
using UnityEngine;
using UnityEngine.Rendering.Universal.Internal;

namespace UnityEngine.Experimental.Rendering.Universal
{
    public class NewContactShadowFeature : ScriptableRendererFeature
    {

        [System.Serializable]
        public class ContactShadowsFeatureSettings : RenderObjects.RenderObjectsSettings
        {
            [Range(0, 10.05f)] public float _rejectionDepth = 0.5f;
            [Range(4, 32)] public int _sampleCount = 16;
            [Range(0, 1)] public float _temporalFilter = 0.5f;
            public PostEffects.NoiseTextureSet _noiseTextures;
            [HideInInspector] public Texture _DefaultTexture;
            public bool _downsample = false;
        }

        public ContactShadowsFeatureSettings settings = new ContactShadowsFeatureSettings();

        ContactShadowRenderObjectsPass renderObjectsPass;
        //UnityEngine.Rendering.Universal.Internal.DrawObjectsPass r;
        public override void Create()
        {
            RenderObjects.FilterSettings filter = settings.filterSettings;
            renderObjectsPass = new ContactShadowRenderObjectsPass(settings.passTag, settings.Event, filter.PassNames,
                filter.RenderQueueType, filter.LayerMask, settings.cameraSettings,
                settings._noiseTextures);

            renderObjectsPass.overrideMaterial = settings.overrideMaterial;
            renderObjectsPass.overrideMaterialPassIndex = settings.overrideMaterialPassIndex;

            if (settings.overrideDepthState)
                renderObjectsPass.SetDetphState(settings.enableWrite, settings.depthCompareFunction);

            if (settings.stencilSettings.overrideStencilState)
                renderObjectsPass.SetStencilState(settings.stencilSettings.stencilReference,
                    settings.stencilSettings.stencilCompareFunction, settings.stencilSettings.passOperation,
                    settings.stencilSettings.failOperation, settings.stencilSettings.zFailOperation);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            renderObjectsPass.Setup(settings, renderer.cameraColorTarget, renderer.cameraDepth);

            renderer.EnqueuePass(renderObjectsPass);
        }
    }
}

