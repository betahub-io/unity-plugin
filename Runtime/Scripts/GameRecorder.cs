using UnityEngine;
using System.Collections;
using System.IO;
using UnityEngine.Serialization;
using Unity.Collections;
using UnityEngine.Rendering;

namespace BetaHub
{
    public class GameRecorder : MonoBehaviour
    {
        [FormerlySerializedAs("frameRate")]
        public int FrameRate = 30;

        public int RecordingDuration = 60;

        [Tooltip("If true, the video will be downscaled if the screen resolution is higher than the maximum video height and width.")]
        public bool DownscaleVideo = false;

        [Tooltip("The maximum height of the video. The video will be downscaled if the screen resolution is higher.")]
        public int MaxVideoHeight = 1080;

        [Tooltip("The maximum width of the video. The video will be downscaled if the screen resolution is higher.")]
        public int MaxVideoWidth = 1920;

        [Tooltip("If enabled, renders the FPS overlay on the video.")]
        public bool RenderFPSOverlay = true;

        [Tooltip("If enabled, renders the mouse cursor position in the recorded video.")]
        public bool RenderCursor = false;

        [Tooltip("If enabled, mirrors the video vertically (flips it upside down).")]
        public bool MirrorVertically = false;

        // set this to a render texture to capture a specific render texture instead of the screen
        [HideInInspector]
        public RenderTexture CaptureRenderTexture;

        // Optimized: Use RenderTextures for efficient GPU-based capture
        private RenderTexture _captureRT;
        private RenderTexture _fullScreenRT; // For capturing full screen when downscaling
        private byte[] _rgbConversionBuffer; // Reusable buffer for RGBA to RGB conversion

        // GC Optimization: Reusable buffers to avoid allocations in capture loop
        private byte[] _frameDataBuffer; // Reusable buffer for frame data from GPU
        private byte[] _rgbaDataBuffer; // Reusable buffer for RGBA texture data

        public bool IsRecording { get; private set; }
#if ENABLE_IL2CPP && !ENABLE_BETAHUB_FFMPEG
        public bool IsPaused { get; set; }
#else
        public bool IsPaused { get { return _videoEncoder?.IsPaused ?? false; } set { if (_videoEncoder != null) _videoEncoder.IsPaused = value; } }
#endif

        private VideoEncoder _videoEncoder;

        private int _gameWidth;
        private int _gameHeight;
        private int _outputWidth;
        private int _outputHeight;

        private float _captureInterval;
        private float _nextCaptureTime;

        public bool DebugMode = false;

        private float _fps;
        private float _deltaTime = 0.0f;

        void Start()
        {
#if ENABLE_IL2CPP && !ENABLE_BETAHUB_FFMPEG
            Debug.LogWarning("Video recording is disabled in IL2CPP builds without ENABLE_BETAHUB_FFMPEG. " +
                            "Please enable ENABLE_BETAHUB_FFMPEG in your scripting define symbols " +
                            "or disable video recording features in your game.");
            return;
#endif

            // Adjust the game resolution to be divisible by 4
            _gameWidth  = Screen.width  - (Screen.width  % 4);
            _gameHeight = Screen.height - (Screen.height % 4);

            // if custom render texture is not set and the screen w and h is not divisible by 4, print a warning
            if (CaptureRenderTexture == null && (_gameWidth % 4 != 0 || _gameHeight % 4 != 0))
            {
                UnityEngine.Debug.LogWarning("Current screen width and height are not divisible by 4. " +
                    "This may cause severe performance issues.");
            }

            // Determine target (output) resolution
            _outputWidth = _gameWidth;
            _outputHeight = _gameHeight;
            float aspect = (float)_gameWidth / _gameHeight;
            if (DownscaleVideo && (_gameWidth > MaxVideoWidth || _gameHeight > MaxVideoHeight))
            {
                _outputHeight = Mathf.Min(_gameHeight, MaxVideoHeight);
                _outputWidth = Mathf.RoundToInt(_outputHeight * aspect);
                if (_outputWidth > MaxVideoWidth)
                {
                    _outputWidth = MaxVideoWidth;
                    _outputHeight = Mathf.RoundToInt(_outputWidth / aspect);
                }
                // Ensure both dimensions are multiples of 4 (for AsyncGPUReadback)
                _outputWidth -= _outputWidth % 4;
                _outputHeight -= _outputHeight % 4;

                UnityEngine.Debug.Log($"Video is to be downscaled to {_outputWidth}x{_outputHeight}");
            }

            // Optimized: Create RenderTexture once at output resolution (handles downscaling automatically)
            _captureRT = new RenderTexture(_outputWidth, _outputHeight, 0, RenderTextureFormat.ARGB32);
            _captureRT.Create();

            // If we're downscaling, we need a full-screen texture to capture from first
            if (DownscaleVideo && (_gameWidth != _outputWidth || _gameHeight != _outputHeight))
            {
                _fullScreenRT = new RenderTexture(_gameWidth, _gameHeight, 0, RenderTextureFormat.ARGB32);
                _fullScreenRT.Create();
            }

            // Initialize RGB conversion buffer
            _rgbConversionBuffer = new byte[_outputWidth * _outputHeight * 3]; // RGB24 format
            
            // GC Optimization: Initialize reusable buffers and textures
            int expectedFrameSize = _outputWidth * _outputHeight * 4; // RGBA32 format
            _frameDataBuffer = new byte[expectedFrameSize];
            _rgbaDataBuffer = new byte[expectedFrameSize];
            
            IsRecording = false;

            string outputDirectory = Path.Combine(Application.persistentDataPath, "BH_Recording");
            if (DebugMode)
            {
                outputDirectory = "BH_Recording";
            }

            // Initialize the video encoder with the output resolution
            _videoEncoder = new VideoEncoder(_outputWidth, _outputHeight, FrameRate, RecordingDuration, outputDirectory, MirrorVertically);

            _captureInterval = 1.0f / FrameRate;
            _nextCaptureTime = Time.time;
        }

        void OnDestroy()
        {
            // Optimized: Proper cleanup of RenderTextures
            if (_captureRT != null)
            {
                _captureRT.Release();
                _captureRT = null;
            }
            if (_fullScreenRT != null)
            {
                _fullScreenRT.Release();
                _fullScreenRT = null;
            }
            _rgbConversionBuffer = null;
            _frameDataBuffer = null;
            _rgbaDataBuffer = null;
            if (_videoEncoder != null) // can be null since Start() may not have been called
            {
                _videoEncoder.Dispose();
            }
        }

        void Update()
        {
            // Accumulate the time since the last frame
            _deltaTime += (Time.unscaledDeltaTime - _deltaTime) * 0.1f;

            // Calculate FPS as the reciprocal of the averaged delta time
            _fps = 1.0f / _deltaTime;
        }

        public void StartRecording()
        {
#if ENABLE_IL2CPP && !ENABLE_BETAHUB_FFMPEG
            return; // no log here as it would spam the log file
#endif

            if (IsPaused)
            {
                IsPaused = false; // this will unpause
            }
            else if (!IsRecording)
            {
                _videoEncoder.StartEncoding();
                IsRecording = true;
                StartCoroutine(CaptureFrames());
            }
            else
            {
                Debug.LogWarning("Cannot start recording when already recording.");
            }
        }

        public void PauseRecording()
        {
#if ENABLE_IL2CPP && !ENABLE_BETAHUB_FFMPEG
            return;
#endif

            if (IsRecording)
            {
                IsPaused = true;
            }
            else
            {
                Debug.LogWarning("Cannot pause recording when not recording.");
            }
        }

        public string StopRecordingAndSaveLastMinute()
        {
#if ENABLE_IL2CPP && !ENABLE_BETAHUB_FFMPEG
            return null;
#endif

            IsRecording = false;
            return _videoEncoder.StopEncoding();
        }

        private IEnumerator CaptureFrames()
        {
            #if BETAHUB_DEBUG
            UnityEngine.Debug.Log($"Game resolution: {_gameWidth}x{_gameHeight}");
            UnityEngine.Debug.Log($"Output resolution: {_outputWidth}x{_outputHeight}");
            #endif

            while (IsRecording)
            {
                yield return new WaitForEndOfFrame();

                if (Time.time >= _nextCaptureTime)
                {
                    _nextCaptureTime += _captureInterval;

                    // Check if we should use a custom render texture or capture from screen
                    if (CaptureRenderTexture != null)
                    {
                        // Use the specified render texture instead of screen capture
                        if (CaptureRenderTexture.width == _outputWidth && CaptureRenderTexture.height == _outputHeight)
                        {
                            // Direct readback if dimensions match output resolution
                            AsyncGPUReadback.Request(CaptureRenderTexture, 0, OnCompleteReadback);
                        }
                        else
                        {
                            // Scale to output resolution if dimensions don't match
                            Graphics.Blit(CaptureRenderTexture, _captureRT);
                            AsyncGPUReadback.Request(_captureRT, 0, OnCompleteReadback);
                        }
                    }
                    else
                    {
                        // Original screen capture logic
                        if (_fullScreenRT != null)
                        {
                            // First capture full screen
                            Graphics.Blit(null, _fullScreenRT);
                            // Then scale down to output resolution
                            Graphics.Blit(_fullScreenRT, _captureRT);
                        }
                        else
                        {
                            // Direct capture when no scaling needed
                            Graphics.Blit(null, _captureRT);
                        }
                        
                        // Use AsyncGPUReadback for non-blocking frame capture
                        AsyncGPUReadback.Request(_captureRT, 0, OnCompleteReadback);
                    }
                }
            }
        }

        // Optimized: Async callback for GPU readback completion
        private void OnCompleteReadback(AsyncGPUReadbackRequest request)
        {
            if (request.hasError)
            {
                #if BETAHUB_DEBUG
                UnityEngine.Debug.LogError("AsyncGPUReadback request failed");
                #endif
                return;
            }

            if (!IsRecording) return; // Recording might have stopped while waiting for readback

            // Get the raw data from GPU using safe copy to avoid allocation
            var rawData = request.GetData<byte>();
            SafeCopyNativeArrayToByteArray(rawData, ref _frameDataBuffer);

            // Apply FPS overlay and get the processed RGB data
            var processedFrameData = ApplyFPSOverlay(_frameDataBuffer, _outputWidth, _outputHeight);

            // Send frame to encoder
            _videoEncoder.AddFrame(processedFrameData);
        }

        // Helper method to apply FPS overlay to raw frame data
        private byte[] ApplyFPSOverlay(byte[] frameData, int width, int height)
        {
            if (_rgbConversionBuffer == null)
            {
                // no buffer could mean that we're in the middle of shutting down
                return frameData;
            }

            // Copy frameData to _rgbaDataBuffer to avoid modifying the original
            if (_rgbaDataBuffer == null || _rgbaDataBuffer.Length != frameData.Length)
            {
                _rgbaDataBuffer = new byte[frameData.Length];
            }
            System.Array.Copy(frameData, _rgbaDataBuffer, frameData.Length);

            // Create a byte buffer wrapper to work with the frame data directly
            var bufferWrapper = new ByteBufferWrapper(_rgbaDataBuffer, width, height, 4); // RGBA format
            
            // flipY=true means Y=0 is at the bottom, so coordinates work like mathematical coordinates
            TexturePainter painter = null;

            // Draw FPS overlay directly on the buffer
            if (RenderFPSOverlay)
            {
                if (painter == null)
                {
                    painter = new TexturePainter(bufferWrapper, flipY: true);
                }
                painter.DrawNumber(5, 5, (int)_fps, Color.white, 2); // This will draw near bottom-left corner
            }

            // Draw cursor if enabled
            if (RenderCursor)
            {
                if (painter == null)
                {
                    painter = new TexturePainter(bufferWrapper, flipY: true);
                }
                
                // Get mouse position in screen coordinates
                Vector3 mousePos = Input.mousePosition;
                
                // Convert screen coordinates to texture coordinates
                // Screen coordinates: (0,0) at bottom-left, (Screen.width, Screen.height) at top-right
                // Texture coordinates depend on scaling and capture source
                int cursorX, cursorY;
                
                if (CaptureRenderTexture != null)
                {
                    // When using a custom render texture, we need to map mouse position to texture space
                    // This assumes the render texture represents the full screen view
                    cursorX = Mathf.RoundToInt((mousePos.x / Screen.width) * width);
                    cursorY = Mathf.RoundToInt((mousePos.y / Screen.height) * height);
                }
                else
                {
                    // Direct screen capture - account for potential downscaling
                    cursorX = Mathf.RoundToInt((mousePos.x / _gameWidth) * width);
                    cursorY = Mathf.RoundToInt((mousePos.y / _gameHeight) * height);
                }
                
                // Draw cursor with white fill and black outline for visibility
                painter.DrawCursor(cursorX, cursorY, Color.white, Color.black, 1);
            }

            // Convert RGBA32 to RGB24 by removing alpha channel
            for (int i = 0, j = 0; i < _rgbaDataBuffer.Length; i += 4, j += 3)
            {
                _rgbConversionBuffer[j] = _rgbaDataBuffer[i];     // R
                _rgbConversionBuffer[j + 1] = _rgbaDataBuffer[i + 1]; // G
                _rgbConversionBuffer[j + 2] = _rgbaDataBuffer[i + 2]; // B
                // Skip alpha channel (_rgbaDataBuffer[i + 3])
            }

            return _rgbConversionBuffer;
        }

        /// <summary>
        /// Safely copies data from a NativeArray to a byte array, resizing the target array if necessary.
        /// This avoids GC allocations by reusing existing arrays when possible.
        /// </summary>
        /// <param name="source">The source NativeArray to copy from</param>
        /// <param name="target">The target byte array to copy to (will be resized if needed)</param>
        private void SafeCopyNativeArrayToByteArray(NativeArray<byte> source, ref byte[] target)
        {
            int requiredSize = source.Length;
            
            // Resize target array if it's null or too small
            if (target == null || target.Length != requiredSize)
            {
                int oldSize = target?.Length ?? 0;
                
                target = new byte[requiredSize];
                #if BETAHUB_DEBUG
                UnityEngine.Debug.Log($"Resized buffer from {oldSize} to {requiredSize} bytes");
                #endif
            }
            
            // Use CopyTo for efficient copying without allocation
            
            source.CopyTo(target);

        }
    }
}