using NUnit.Framework;
using UnityEngine;

namespace BetaHub.Tests
{
    // Unity only (not in the dotnet loop): needs a live GameObject.
    [TestFixture]
    public class BetaHubReporterLifecycleTests
    {
        private GameObject _go;

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
        }

        // In edit mode Awake never runs, the same as calling in from another component's Awake.
        [Test]
        public void SessionCalls_WorkBeforeAwake()
        {
            _go = new GameObject("Reporter");
            var reporter = _go.AddComponent<BetaHubReporter>();

            Assert.DoesNotThrow(() => reporter.SetCustomField("level", "3"));
            Assert.DoesNotThrow(() => reporter.AddFile("save.dat"));
            Assert.DoesNotThrow(() => reporter.ClearAttachments());
        }
    }
}
