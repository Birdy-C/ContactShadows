using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using System.Collections.Generic;

namespace PostEffects
{
    class InsertBlankShadowmapPass : ScriptableRenderPass
    {
        RenderTargetIdentifier _rendertarget;
        public InsertBlankShadowmapPass(RenderPassEvent temp_renderPassEvent)
        {
            renderPassEvent = temp_renderPassEvent;
        }

        // This isn't part of the ScriptableRenderPass class and is our own addition.
        // For this custom pass we need the camera's color target, so that gets passed in.
        public void Setup(RenderTargetIdentifier rt)
        {
            _rendertarget = rt;
        }

        // called each frame before Execute, use it to set up things the pass will need
        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
        }

        // Execute is called for every eligible camera every frame. It's not called at the moment that
        // rendering is actually taking place, so don't directly execute rendering commands here.
        // Instead use the methods on ScriptableRenderContext to set up instructions.
        // RenderingData provides a bunch of (not very well documented) information about the scene
        // and what's being rendered.
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer _command = CommandBufferPool.Get("Insert Blank Contact Shadow");
            _command.SetGlobalTexture(Shader.PropertyToID("_ContactShadowsMask"), _rendertarget);
            context.ExecuteCommandBuffer(_command);
            _command.Clear();
            CommandBufferPool.Release(_command);
        }

        // called after Execute, use it to clean up anything allocated in Configure
        public override void FrameCleanup(CommandBuffer cmd)
        {
        }
    }
}