using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using System.Collections.Generic;

namespace PostEffects
{
    class ContactShadowsRenderPass : ScriptableRenderPass
    {
        Light _light;
        float _rejectionDepth = 0.5f;
        int _sampleCount = 16;
        float _temporalFilter = 0.5f;
        bool _downsample = false;

        Shader _shader;
        NoiseTextureSet _noiseTextures;
        Texture _DefaultTexture;
        Camera _currentCamera;
        int _currentTextureWidth, _currentTextureHeight;
        #region Temporary objects

        Material _material;
        class CameraVariable
        {
            public RenderTexture _prevMaskRT1 = null, _prevMaskRT2 = null;
            public Matrix4x4 _previousVP = Matrix4x4.identity;
        }

        CommandBuffer _command;
        RenderTargetIdentifier _cameraDepth;
        RenderTexture tempRTHalf, tempRTFull;
        RenderTexture unfilteredMask;

        // We track the VP matrix without using previousViewProjectionMatrix
        // because it's not available for use in OnPreCull.
        static Dictionary<Camera, CameraVariable> CameraDictionary = new Dictionary<Camera, CameraVariable>();
        #endregion

        public ContactShadowsRenderPass(RenderPassEvent temp_renderPassEvent, NoiseTextureSet noiseTextures)
        {
            renderPassEvent = temp_renderPassEvent;
            _shader = Shader.Find("Hidden/PostEffects/ContactShadows");
            _noiseTextures = noiseTextures;
        }

        public RenderTargetIdentifier GetRenderTexture(Camera camera)
        {
            if (CameraDictionary.ContainsKey(camera))
            {
                return CameraDictionary[camera]._prevMaskRT1;
            }
            else
            {
                return _DefaultTexture;
            }
        }
        
        // This isn't part of the ScriptableRenderPass class and is our own addition.
        // For this custom pass we need the camera's color target, so that gets passed in.
        public void Setup(ContactShadowsFeature.ContactShadowsFeatureSettings setting)
        {
            _rejectionDepth = setting._rejectionDepth;
            _sampleCount = setting._sampleCount;
            _temporalFilter = setting._temporalFilter;
            _DefaultTexture = setting._DefaultTexture;
            _downsample = setting._downsample;
        }

        // called each frame before Execute, use it to set up things the pass will need
        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            _currentTextureWidth = cameraTextureDescriptor.width;
            _currentTextureHeight = cameraTextureDescriptor.height;
            var maskFormat = RenderTextureFormat.R8;
            tempRTFull = RenderTexture.GetTemporary(cameraTextureDescriptor.width, cameraTextureDescriptor.height, 0, maskFormat);
            if (_downsample)
            {
                tempRTHalf = RenderTexture.GetTemporary(cameraTextureDescriptor.width / 2, cameraTextureDescriptor.height / 2, 0, maskFormat);
            }
            else
            {
                tempRTHalf = RenderTexture.GetTemporary(cameraTextureDescriptor.width, cameraTextureDescriptor.height, 0, maskFormat);
            }
            unfilteredMask = RenderTexture.GetTemporary(cameraTextureDescriptor.width, cameraTextureDescriptor.height, 0, maskFormat);
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
            if (!CameraDictionary.ContainsKey(_currentCamera))
            {
                CameraDictionary.Add(_currentCamera, new CameraVariable());
            }

            if(renderingData.lightData.mainLightIndex == -1)
            {
                if(renderingData.lightData.visibleLights.Length > 0)
                {
                    _light = renderingData.lightData.visibleLights[0].light;
                }
                else
                {
                    return;
                }
            }
            else
            {
                _light = renderingData.lightData.visibleLights[renderingData.lightData.mainLightIndex].light;
            }

            if (_light != null && _currentCamera != null)
            {
                UpdateTempObjects();
                BuildCommandBuffer();
                context.ExecuteCommandBuffer(_command);
            }
        }

        // called after Execute, use it to clean up anything allocated in Configure
        public override void FrameCleanup(CommandBuffer cmd)
        {
            RenderTexture.ReleaseTemporary(tempRTHalf);
            RenderTexture.ReleaseTemporary(unfilteredMask);
        }

        #region Internal methods

        // Calculates the view-projection matrix for GPU use.
        Matrix4x4 CalculateVPMatrix()
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
            var div = 1;
            //return new Vector2Int(cam.pixelWidth / div, cam.pixelHeight / div);
            return new Vector2Int(_currentTextureWidth/ div, _currentTextureHeight / div);
        }

        // Update the temporary objects for the current frame.
        void UpdateTempObjects()
        {
            if (CameraDictionary[_currentCamera]._prevMaskRT2 != null)
            {
                RenderTexture.ReleaseTemporary(CameraDictionary[_currentCamera]._prevMaskRT2);
                CameraDictionary[_currentCamera]._prevMaskRT2 = null;
            }

            // Do nothing below if the target light is not set.
            if (_light == null) return;

            // Lazy initialization of temporary objects.
            if (_material == null)
            {
                _material = new Material(_shader);
                _material.hideFlags = HideFlags.DontSave;
            }

            if (_command == null)
            {
                _command = new CommandBuffer();
                _command.name = "Contact Shadow Ray Tracing";
            }
            else
            {
                _command.Clear();
            }

            // Update the common shader parameters.
            _material.SetFloat("_RejectionDepth", _rejectionDepth);

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
            _material.SetMatrix("_Reprojection", CameraDictionary[_currentCamera]._previousVP * _currentCamera.transform.localToWorldMatrix);
            CameraDictionary[_currentCamera]._previousVP = CalculateVPMatrix();
        }

        // Build the command buffer for the current frame.
        void BuildCommandBuffer()
        {
            // Allocate the temporary shadow mask RT.
            var maskSize = GetScreenSize();
            //var maskFormat = RenderTextureFormat.R8;

            // Command buffer 1: raytracing and temporal filter
            if (_temporalFilter == 0)
            {
                // Do raytracing and output to the temporary shadow mask RT.
                _command.SetRenderTarget(tempRTHalf);
                _command.DrawProcedural(Matrix4x4.identity, _material, 0, MeshTopology.Triangles, 3);
            }
            else
            {
                // Do raytracing and output to the unfiltered mask RT.
                //var unfilteredMaskID = Shader.PropertyToID("");
                //_command.GetTemporaryRT(unfilteredMaskID, maskSize.x, maskSize.y, 0, FilterMode.Point, maskFormat);
                _command.SetRenderTarget(unfilteredMask);
                _command.DrawProcedural(Matrix4x4.identity, _material, 0, MeshTopology.Triangles, 3);
                _command.SetGlobalTexture(Shader.PropertyToID("_UnfilteredMask"), unfilteredMask);

                // Apply the temporal filter and output to the temporary shadow mask RT.
                _command.SetGlobalTexture(Shader.PropertyToID("_PrevMask"), CameraDictionary[_currentCamera]._prevMaskRT1);
                _command.SetRenderTarget(tempRTHalf);
                _command.DrawProcedural(Matrix4x4.identity, _material, 1 + (Time.frameCount & 1), MeshTopology.Triangles, 3);
            }

            if (_downsample)
            {
                // Downsample enabled: Use upsampler for the composition.
                // TODO why??
                _command.Blit(_DefaultTexture, tempRTFull);
                _command.SetRenderTarget(tempRTFull);
                _command.SetGlobalTexture(Shader.PropertyToID("_TempMask"), tempRTHalf);
                _command.DrawProcedural(Matrix4x4.identity, _material, 3, MeshTopology.Triangles, 3);
            }
            else
            {
                // No downsample: Use simple blit.
                _command.Blit(tempRTHalf, tempRTFull);
            }
            _command.SetGlobalTexture(Shader.PropertyToID("_ContactShadowsMask"), _DefaultTexture);

            // Update the filter history.
            CameraDictionary[_currentCamera]._prevMaskRT2 = CameraDictionary[_currentCamera]._prevMaskRT1;
            CameraDictionary[_currentCamera]._prevMaskRT1 = tempRTFull;
        }
        #endregion
    }
}