using System;
using NUnit.Framework;
using UnityEngine;

namespace BetaHub.Tests
{
    [TestFixture]
    public class PreviewFrameSizeTests
    {
        [Test]
        public void KeepsTheGameAspect_AtAFixedHeight()
        {
            Assert.AreEqual(new Vector2Int(320, 180), GameRecorder.PreviewFrameSize(1920, 1080));
            Assert.AreEqual(new Vector2Int(229, 180), GameRecorder.PreviewFrameSize(804, 632));
            Assert.AreEqual(new Vector2Int(288, 180), GameRecorder.PreviewFrameSize(1280, 800));
            Assert.AreEqual(new Vector2Int(180, 180), GameRecorder.PreviewFrameSize(1000, 1000));
        }

        [Test]
        public void RejectsAnEmptySource()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => GameRecorder.PreviewFrameSize(0, 1080));
            Assert.Throws<ArgumentOutOfRangeException>(() => GameRecorder.PreviewFrameSize(1920, 0));
        }
    }
}
