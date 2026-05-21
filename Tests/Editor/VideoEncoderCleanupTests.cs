using System.IO;
using NUnit.Framework;
using BetaHub;

namespace BetaHub.Tests
{
    [TestFixture]
    public class VideoEncoderCleanupTests
    {
        private string _tempBaseDir;

        [SetUp]
        public void SetUp()
        {
            _tempBaseDir = Path.Combine(Path.GetTempPath(), "BH_CleanupTest_" + Path.GetRandomFileName());
            Directory.CreateDirectory(_tempBaseDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempBaseDir))
            {
                Directory.Delete(_tempBaseDir, true);
            }
        }

        [Test]
        public void Dispose_ShouldDeleteWavFiles()
        {
            var encoder = new VideoEncoder(320, 240, 30, 60, _tempBaseDir);
            string outputDir = encoder.OutputDirectory;

            File.WriteAllText(Path.Combine(outputDir, "audio_20260521_120000.wav"), "fake wav data");

            encoder.Dispose();

            bool wavSurvived = Directory.Exists(outputDir)
                && Directory.GetFiles(outputDir, "*.wav").Length > 0;
            Assert.IsFalse(wavSurvived,
                "Dispose should clean up .wav files but they were left behind");
        }

        [Test]
        public void Dispose_ShouldDeleteConcatTxt()
        {
            var encoder = new VideoEncoder(320, 240, 30, 60, _tempBaseDir);
            string outputDir = encoder.OutputDirectory;

            File.WriteAllText(Path.Combine(outputDir, "concat.txt"), "file 'segment_000.mp4'");

            encoder.Dispose();

            Assert.IsFalse(File.Exists(Path.Combine(outputDir, "concat.txt")),
                "Dispose should clean up concat.txt but it was left behind");
        }

        [Test]
        public void Dispose_ShouldDeleteTempConcatMp4()
        {
            var encoder = new VideoEncoder(320, 240, 30, 60, _tempBaseDir);
            string outputDir = encoder.OutputDirectory;

            File.WriteAllText(Path.Combine(outputDir, "temp_concat.mp4"), "fake video data");

            encoder.Dispose();

            Assert.IsFalse(File.Exists(Path.Combine(outputDir, "temp_concat.mp4")),
                "Dispose should clean up temp_concat.mp4 but it was left behind");
        }

        [Test]
        public void Dispose_ShouldNotDeleteGameplayMp4()
        {
            var encoder = new VideoEncoder(320, 240, 30, 60, _tempBaseDir);
            string outputDir = encoder.OutputDirectory;

            string gameplayFile = Path.Combine(outputDir, "Gameplay_20260521_120000.mp4");
            File.WriteAllText(gameplayFile, "fake gameplay video");

            encoder.Dispose();

            Assert.IsTrue(File.Exists(gameplayFile),
                "Dispose must NOT delete Gameplay_*.mp4 output files");
        }

        [Test]
        public void Dispose_ShouldDeleteInstanceDirWhenOnlyTempFilesRemain()
        {
            var encoder = new VideoEncoder(320, 240, 30, 60, _tempBaseDir);
            string outputDir = encoder.OutputDirectory;

            File.WriteAllText(Path.Combine(outputDir, "audio_20260521_120000.wav"), "fake wav");
            File.WriteAllText(Path.Combine(outputDir, "concat.txt"), "fake concat");

            encoder.Dispose();

            Assert.IsFalse(Directory.Exists(outputDir),
                "Dispose should delete the instance directory when only temp files were present");
        }
    }

    [TestFixture]
    public class StaleDirectoryCleanupTests
    {
        private string _tempBaseDir;

        [SetUp]
        public void SetUp()
        {
            _tempBaseDir = Path.Combine(Path.GetTempPath(), "BH_StaleTest_" + Path.GetRandomFileName());
            Directory.CreateDirectory(_tempBaseDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempBaseDir))
            {
                Directory.Delete(_tempBaseDir, true);
            }
        }

        [Test]
        public void CleanupStaleRecordingDirectories_ShouldRemoveOldDirectories()
        {
            string staleDir = Path.Combine(_tempBaseDir, "abcd1234");
            Directory.CreateDirectory(staleDir);
            File.WriteAllText(Path.Combine(staleDir, "audio_20260101_120000.wav"), "stale wav");
            Directory.SetLastWriteTime(staleDir, System.DateTime.Now.AddSeconds(-120));

            VideoEncoder.CleanupStaleRecordingDirectories(_tempBaseDir, maxAgeSeconds: 60);

            Assert.IsFalse(Directory.Exists(staleDir),
                "Stale directories older than maxAgeSeconds should be removed");
        }

        [Test]
        public void CleanupStaleRecordingDirectories_ShouldPreserveRecentDirectories()
        {
            string recentDir = Path.Combine(_tempBaseDir, "efgh5678");
            Directory.CreateDirectory(recentDir);
            File.WriteAllText(Path.Combine(recentDir, "segment_000.mp4"), "active segment");

            VideoEncoder.CleanupStaleRecordingDirectories(_tempBaseDir, maxAgeSeconds: 60);

            Assert.IsTrue(Directory.Exists(recentDir),
                "Recent directories (within maxAgeSeconds) should be preserved");
        }

        [Test]
        public void CleanupStaleRecordingDirectories_ShouldHandleNonExistentBaseDir()
        {
            string nonExistent = Path.Combine(_tempBaseDir, "does_not_exist");

            Assert.DoesNotThrow(() =>
                VideoEncoder.CleanupStaleRecordingDirectories(nonExistent, maxAgeSeconds: 60),
                "Should not throw when base directory does not exist");
        }
    }
}
