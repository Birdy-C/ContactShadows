using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PostEffects
{
    class ContactShadowsRenderPass : ScriptableRenderPass
    {
        bool _enable = true;
        Light _light;
        float _rejectionDepth = 0.5f;
        int _sampleCount = 16;
        float _temporalFilter = 0.5f;
        bool _downsample = false;

        Shader _shader;
        NoiseTextureSet _noiseTextures;
        Texture _ContactShadowTexture;
        static Camera _currentCamera;

        #region Temporary objects

        Material _material;
        RenderTexture _prevMaskRT1, _prevMaskRT2;
        CommandBuffer _command1, _command2;

        // We track the VP matrix without using previousViewProjectionMatrix
        // because it's not available for use in OnPreCull.
        Matrix4x4 _previousVP = Matrix4x4.identity;

        #endregion

        public ContactShadowsRenderPass(RenderPassEvent temp_renderPassEvent, NoiseTextureSet noiseTextures)
        {
            renderPassEvent = temp_renderPassEvent;
            _shader = Shader.Find("Hidden/PostEffects/ContactShadows");
            _noiseTextures = noiseTextures;
        }

        // This isn't part of the ScriptableRenderPass class and is our own addition.
        // For this custom pass we need the camera's color target, so that gets passed in.
        public void Setup(bool enable, float rejectionDepth, int sampleCount, float temporalFilter, Texture ContactShadowTexture)
        {
            _enable = enable;
            _rejectionDepth = rejectionDepth;
            _sampleCount = sampleCount;
            _temporalFilter = temporalFilter;
            _ContactShadowTexture = ContactShadowTexture;
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
            if (renderingData.cameraData.camera.cameraType == CameraType.Preview)
            {
                return;
            }
            _currentCamera = renderingData.cameraData.camera;
            _light = renderingData.lightData.visibleLights[renderingData.lightData.mainLightIndex].light;
            if (_light != null && _currentCamera != null)
            {
                UpdateTempObjects();
                BuildCommandBuffer();
                context.ExecuteCommandBuffer(_command1);
                context.ExecuteCommandBuffer(_command2);
            }
        }

        // called after Execute, use it to clean up anything allocated in Configure
        public override void FrameCleanup(CommandBuffer cmd)
        {
        }

        #region Internal methods

        // Calculates the view-projection matrix for GPU use.
        static Matrix4x4 CalculateVPMatrix()
        {
            var cam = _currentCamera;
            var p = cam.nonJitteredProjectionMatrix;
            var v = cam.worldToCameraMatrix;
            return GL.GetGPUProjectionMatrix(p, true) * v;
        }

        // Get the screen dimensions.
        Vector2Int GetScreenSize()
        {
            var cam = _currentCamera;
            var div = _downsample ? 2 : 1;
            return new Vector2Int(cam.pixelWidth / div, cam.pixelHeight / div);
        }

        // Update the temporary objects for the current frame.
        void UpdateTempObjects()
        {
            if (_prevMaskRT2 != null)
            {
                RenderTexture.ReleaseTemporary(_prevMaskRT2);
                _prevMaskRT2 = null;
            }

            // Do nothing below if the target light is not set.
            if (_light == null) return;

            // Lazy initialization of temporary objects.
            if (_material == null)
            {
                _material = new Material(_shader);
                _material.hideFlags = HideFlags.DontSave;
            }

            if (_command1 == null)
            {
                _command1 = new CommandBuffer();
                _command2 = new CommandBuffer();
                _command1.name = "Contact Shadow Ray Tracing";
                _command2.name = "Contact Shadow Temporal Filter";
            }
            else
            {
                _command1.Clear();
                _command2.Clear();
            }

            // Update the common shader parameters.
            if(_enable)
                _material.SetFloat("_RejectionDepth", _rejectionDepth);
            else
                _material.SetFloat("_RejectionDepth", 0);

            _material.SetInt("_SampleCount", _sampleCount);

            var convergence = Mathf.Pow(1 - _temporalFilter, 2);
            _material.SetFloat("_Convergence", convergence);

            // Calculate the light vector in the view space.
            _material.SetVector("_LightVector",
              _currentCamera.transform.InverseTransformDirection(-_light.transform.forward) *
                _light.shadowBias / (_sampleCount - 1.5f)
            );

            // Noise texture and its scale factor
            var noiseTexture = _noiseTextures.GetTexture();
            var noiseScale = (Vector2)GetScreenSize() / noiseTexture.width;
            _material.SetVector("_NoiseScale", noiseScale);
            _material.SetTexture("_NoiseTex", noiseTexture);

            // "Reproject into the previous view" matrix
            _material.SetMatrix("_Reprojection", _previousVP * _currentCamera.transform.localToWorldMatrix);
            _previousVP = CalculateVPMatrix();
        }

        // Build the command buffer for the current frame.
        void BuildCommandBuffer()
        {
            // Allocate the temporary shadow mask RT.
            var maskSize = GetScreenSize();
            var maskFormat = RenderTextureFormat.R8;
            var tempMaskRT = RenderTexture.GetTemporary(maskSize.x, maskSize.y, 0, maskFormat);

            // Command buffer 1: raytracing and temporal filter
            if (_temporalFilter == 0)
            {
                // Do raytracing and output to the temporary shadow mask RT.
                _command1.SetRenderTarget(tempMaskRT);
                _command1.DrawProcedural(Matrix4x4.identity, _material, 0, MeshTopology.Triangles, 3);
            }
            else
            {
                // Do raytracing and output to the unfiltered mask RT.
                var unfilteredMaskID = Shader.PropertyToID("_UnfilteredMask");
                _command1.GetTemporaryRT(unfilteredMaskID, maskSize.x, maskSize.y, 0, FilterMode.Point, maskFormat);
                _command1.SetRenderTarget(unfilteredMaskID);
                _command1.DrawProcedural(Matrix4x4.identity, _material, 0, MeshTopology.Triangles, 3);

                // Apply the temporal filter and output to the temporary shadow mask RT.
                _command1.SetGlobalTexture(Shader.PropertyToID("_PrevMask"), _prevMaskRT1);
                _command1.SetRenderTarget(tempMaskRT);
                _command1.DrawProcedural(Matrix4x4.identity, _material, 1 + (Time.frameCount & 1), MeshTopology.Triangles, 3);
            }

            // Command buffer 2: shadow mask composition
            if (_downsample)
            {
                // Downsample enabled: Use upsampler for the composition.
                _command2.SetRenderTarget(_ContactShadowTexture);
                _command2.SetGlobalTexture(Shader.PropertyToID("_TempMask"), tempMaskRT);
                _command2.DrawProcedural(Matrix4x4.identity, _material, 3, MeshTopology.Triangles, 3);
            }
            else
            {
                // No downsample: Use simple blit.
                _command2.Blit(tempMaskRT, _ContactShadowTexture);
            }
            _command2.SetGlobalTexture(Shader.PropertyToID("_ContactShadowsMask"), _ContactShadowTexture);

            // Update the filter history.
            _prevMaskRT2 = _prevMaskRT1;
            _prevMaskRT1 = tempMaskRT;
        }
        #endregion
    }
}