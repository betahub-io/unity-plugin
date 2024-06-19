using UnityEngine;
using System.Collections;
using System.IO;

public class BH_GameRecorder : MonoBehaviour
{
    public int frameRate = 30;

    public int RecordingDuration = 60;
    private Texture2D screenShot;
    public bool IsRecording { get; private set; }
    public bool IsPaused { get { return videoEncoder.IsPaused; } set { videoEncoder.IsPaused = value; } }
    private BH_VideoEncoder videoEncoder;
    private BH_TexturePainter texturePainter;

    private int gameWidth;
    private int gameHeight;

    private float captureInterval;
    private float nextCaptureTime;

    public bool DebugMode = false;

    private float fps;
    private float deltaTime = 0.0f;

    void Start()
    {
        // Adjust the game resolution to be divisible by 2
        gameWidth = Screen.width % 2 == 0 ? Screen.width : Screen.width - 1;
        gameHeight = Screen.height % 2 == 0 ? Screen.height : Screen.height - 1;

        // Create a Texture2D with the adjusted resolution
        screenShot = new Texture2D(gameWidth, gameHeight, TextureFormat.RGB24, false);
        texturePainter = new BH_TexturePainter(screenShot);
        IsRecording = false;

        string outputDirectory = Path.Combine(Application.persistentDataPath, "BH_Recording");
        if (DebugMode)
        {
            outputDirectory = "BH_Recording";
        }

        // Initialize the video encoder with the adjusted resolution
        videoEncoder = new BH_VideoEncoder(gameWidth, gameHeight, frameRate, RecordingDuration, outputDirectory);

        captureInterval = 1.0f / frameRate;
        nextCaptureTime = Time.time;
    }

    void OnDestroy()
    {
        if (videoEncoder != null) // can be null since Start() may not have been called
        {
            videoEncoder.Dispose();
        }
    }

     void Update()
    {
        // Accumulate the time since the last frame
        deltaTime += (Time.unscaledDeltaTime - deltaTime) * 0.1f;

        // Calculate FPS as the reciprocal of the averaged delta time
        fps = 1.0f / deltaTime;
    }

    public void StartRecording()
    {
        if (IsPaused)
        {
            IsPaused = false; // this will unpause
        }
        else if (!IsRecording)
        {
            videoEncoder.StartEncoding();
            IsRecording = true;
            StartCoroutine(CaptureFrames());
        } else
        {
            Debug.LogWarning("Cannot start recording when already recording.");
        }
    }

    public void PauseRecording()
    {
        if (IsRecording)
        {
            IsPaused = true;
        } else
        {
            Debug.LogWarning("Cannot pause recording when not recording.");
        }
    }

    public string StopRecordingAndSaveLastMinute()
    {
        IsRecording = false;
        return videoEncoder.StopEncoding();
    }

    private IEnumerator CaptureFrames()
    {
        while (IsRecording)
        {
            yield return new WaitForEndOfFrame();

            if (Time.time >= nextCaptureTime)
            {
                nextCaptureTime += captureInterval;

                // Capture the screen content into the Texture2D
                screenShot.ReadPixels(new Rect(0, 0, gameWidth, gameHeight), 0, 0);
                screenShot.Apply();

                // Draw a vertical progress bar as an example
                // float cpuUsage = 0.5f; // Replace this with actual CPU usage value
                // texturePainter.DrawVerticalProgressBar(10, 10, 20, 100, cpuUsage, Color.green, Color.black);

                // Draw a number as an example
                // texturePainter.DrawNumber(50, 10, (int) fps, Color.white, 4);
                texturePainter.DrawNumber(5, 5, (int) fps, Color.white, 2);

                byte[] frameData = screenShot.GetRawTextureData();
                videoEncoder.AddFrame(frameData);
            }
        }
    }
}