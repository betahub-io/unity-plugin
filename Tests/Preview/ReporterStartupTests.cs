using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BetaHub.Tests
{
    // What the reporter offers depends on its neighbours on the object: the recorder and the diagnostic providers.
    public class ReporterStartupTests
    {
        // nothing listens here, so no test reaches BetaHub
        private const string UnreachableEndpoint = "http://127.0.0.1:9";
        // configured, so the reporter's startup check has nothing to log
        private const string ProjectId = "pr-0000000000";
        private const string AuthToken = "tkn-0000000000";
        private const int StartFrames = 2;

        private GameObject _object;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_object != null) Object.Destroy(_object);
            // the next test's reporter would otherwise meet this one and stand down as a duplicate
            yield return null;
        }

        // inactive, so the test decides when Awake, OnEnable and Start run
        private BetaHubReporter NewReporter(params System.Type[] neighbours)
        {
            _object = new GameObject("Reporter");
            _object.SetActive(false);
            var reporter = _object.AddComponent<BetaHubReporter>();
            reporter.SubmitEndpoint = UnreachableEndpoint;
            reporter.ProjectID = ProjectId;
            reporter.AuthToken = AuthToken;
            foreach (var type in neighbours) _object.AddComponent(type);
            return reporter;
        }

        private static List<string> Rows(BetaHubReporter reporter) => reporter.CollectAttachments().Items.Select(item => item.Id).ToList();

        // A recorder that never started (disabled here; no FFmpeg or IL2CPP without the define in a game) records nothing,
        // so the form must not offer a video whose upload can only fail.
        [UnityTest]
        public IEnumerator RecorderThatNeverStarted_OffersNoRecording()
        {
            var reporter = NewReporter(typeof(GameRecorder));
            _object.GetComponent<GameRecorder>().enabled = false;
            _object.SetActive(true);
            for (int i = 0; i < StartFrames; i++) yield return null;

            CollectionAssert.DoesNotContain(Rows(reporter), BuiltInAttachments.Recording);
        }

        [UnityTest]
        public IEnumerator RecorderWithFfmpeg_OffersTheRecording()
        {
            if (!VideoEncoder.IsFFmpegAvailable()) Assert.Ignore("FFmpeg isn't in StreamingAssets/BetaHub; Window > BetaHub > Download FFmpeg");
            var reporter = NewReporter(typeof(GameRecorder));
            _object.SetActive(true);
            for (int i = 0; i < StartFrames; i++) yield return null;

            CollectionAssert.Contains(Rows(reporter), BuiltInAttachments.Recording);
        }

        // The reporter checks the providers' custom fields in its Start; the providers must be attached by then,
        // whichever Start Unity runs first.
        [Test]
        public void Providers_AttachBeforeAnyStart()
        {
            var reporter = NewReporter(typeof(GeolocationProvider), typeof(LatencyProvider));
            _object.SetActive(true); // Awake and OnEnable run now, Start on the next frame

            var rows = Rows(reporter);
            CollectionAssert.Contains(rows, BuiltInAttachments.Location);
            CollectionAssert.Contains(rows, BuiltInAttachments.Latency);
        }
    }
}
