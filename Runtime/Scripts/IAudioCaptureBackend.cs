namespace BetaHub
{
    public interface IAudioCaptureBackend
    {
        void StartCapture(string outputDirectory);
        string StopCapture();
        bool IsCapturing { get; }
        void PauseCapture();
        void ResumeCapture();
    }
}
