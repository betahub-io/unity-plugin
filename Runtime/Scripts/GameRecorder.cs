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

        private Texture2D _screenShot;

        public bool IsRecording { get; private set; }
        public bool IsPaused { get { return _videoEncoder.IsPaused; } set { _videoEncoder.IsPaused = value; } }

        private VideoEncoder _videoEncoder;
        private TexturePainter _texturePainter;

        private int _gameWidth;
        private int _gameHeight;

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

            // Create a Texture2D with the adjusted resolution
            _screenShot = new Texture2D(_gameWidth, _gameHeight, TextureFormat.RGB24, false);
            _texturePainter = new TexturePainter(_screenShot);
            IsRecording = false;

            string outputDirectory = Path.Combine(Application.persistentDataPath, "BH_Recording");
            if (DebugMode)
            {
                outputDirectory = "BH_Recording";
            }

            // Initialize the video encoder with the adjusted resolution
            _videoEncoder = new VideoEncoder(_gameWidth, _gameHeight, FrameRate, RecordingDuration, outputDirectory);

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
            while (IsRecording)
            {
                yield return new WaitForEndOfFrame();

                if (Time.time >= _nextCaptureTime)
                {
                    _nextCaptureTime += _captureInterval;

                    // Capture the screen content into the Texture2D
                    _screenShot.ReadPixels(new Rect(0, 0, _gameWidth, _gameHeight), 0, 0);
                    _screenShot.Apply();

                    // Draw a vertical progress bar as an example
                    // float cpuUsage = 0.5f; // Replace this with actual CPU usage value
                    // texturePainter.DrawVerticalProgressBar(10, 10, 20, 100, cpuUsage, Color.green, Color.black);

                    // Draw a number as an example
                    // texturePainter.DrawNumber(50, 10, (int) fps, Color.white, 4);
                    _texturePainter.DrawNumber(5, 5, (int)_fps, Color.white, 2);

                    byte[] frameData = _screenShot.GetRawTextureData();
                    _videoEncoder.AddFrame(frameData);
                }
            }
        }
    }
}