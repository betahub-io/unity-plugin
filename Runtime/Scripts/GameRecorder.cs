using UnityEngine;
using System.Collections;
using System.IO;
using UnityEngine.Serialization;

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

        private Texture2D _screenShot;

        public bool IsRecording { get; private set; }
        public bool IsPaused { get { return _videoEncoder.IsPaused; } set { _videoEncoder.IsPaused = value; } }

        private VideoEncoder _videoEncoder;
        private TexturePainter _texturePainter;

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
            // Adjust the game resolution to be divisible by 2
            _gameWidth = Screen.width % 2 == 0 ? Screen.width : Screen.width - 1;
            _gameHeight = Screen.height % 2 == 0 ? Screen.height : Screen.height - 1;

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
                // Ensure both dimensions are even
                _outputWidth -= _outputWidth % 2;
                _outputHeight -= _outputHeight % 2;

                UnityEngine.Debug.Log($"Video is to be downscaled to {_outputWidth}x{_outputHeight}");
            }

            // Create a Texture2D with the adjusted resolution
            _screenShot = new Texture2D(_gameWidth, _gameHeight, TextureFormat.RGB24, false);
            _texturePainter = new TexturePainter(_screenShot);
            IsRecording = false;

            string outputDirectory = Path.Combine(Application.persistentDataPath, "BH_Recording");
            if (DebugMode)
            {
                outputDirectory = "BH_Recording";
            }

            // Initialize the video encoder with the output resolution
            _videoEncoder = new VideoEncoder(_outputWidth, _outputHeight, FrameRate, RecordingDuration, outputDirectory);

            _captureInterval = 1.0f / FrameRate;
            _nextCaptureTime = Time.time;
        }

        void OnDestroy()
        {
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
            IsRecording = false;
            return _videoEncoder.StopEncoding();
        }

        private IEnumerator CaptureFrames()
        {
            UnityEngine.Debug.Log($"Game resolution: {_gameWidth}x{_gameHeight}");
            UnityEngine.Debug.Log($"Output resolution: {_outputWidth}x{_outputHeight}");

            RenderTexture scaledRT = null;
            Texture2D scaledTexture = null;
            if (_outputWidth != _gameWidth || _outputHeight != _gameHeight)
            {
                scaledRT = new RenderTexture(_outputWidth, _outputHeight, 0, RenderTextureFormat.ARGB32);
                scaledTexture = new Texture2D(_outputWidth, _outputHeight, TextureFormat.RGB24, false);
            }

            while (IsRecording)
            {
                yield return new WaitForEndOfFrame();

                if (Time.time >= _nextCaptureTime)
                {
                    _nextCaptureTime += _captureInterval;

                    // 1. Capture the full-res frame
                    _screenShot.ReadPixels(new Rect(0, 0, _gameWidth, _gameHeight), 0, 0);
                    _screenShot.Apply();

                    byte[] frameData;

                    if (scaledRT != null)
                    {
                        // 2a. Blit (scale) to RT
                        Graphics.Blit(_screenShot, scaledRT);

                        // 2b. Read pixels from scaled RT
                        RenderTexture.active = scaledRT;
                        scaledTexture.ReadPixels(new Rect(0, 0, _outputWidth, _outputHeight), 0, 0);
                        scaledTexture.Apply();
                        RenderTexture.active = null;

                        // 3a. Draw overlays on the scaled texture
                        var painter = new TexturePainter(scaledTexture);
                        painter.DrawNumber(5, 5, (int)_fps, Color.white, 2);

                        frameData = scaledTexture.GetRawTextureData();
                    }
                    else
                    {
                        // 2b. Draw overlays on the original screenshot
                        _texturePainter.DrawNumber(5, 5, (int)_fps, Color.white, 2);
                        frameData = _screenShot.GetRawTextureData();
                    }

                    _videoEncoder.AddFrame(frameData);
                }
            }

            // Clean up if needed
            if (scaledTexture != null) Destroy(scaledTexture);
            if (scaledRT != null) scaledRT.Release();
        }
    }
}