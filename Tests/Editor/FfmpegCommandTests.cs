using System;
using NUnit.Framework;

namespace BetaHub.Tests
{
    [TestFixture]
    public class FfmpegCommandTests
    {
        // With diagnostics on, temp_concat.mp4 stays after a report. The next report's concat then asked
        // "Overwrite? [y/N]" on stdin, and the game waited on FFmpeg for good.
        [Test]
        public void Concat_OverwritesItsOutput_AndNeverAsks()
        {
            string[] args = VideoEncoder.ConcatCommand("/rec/concat.txt", "/rec/temp_concat.mp4");

            CollectionAssert.Contains(args, "-y");
            CollectionAssert.Contains(args, "-nostdin");
            Assert.AreEqual("/rec/temp_concat.mp4", args[args.Length - 1], "the output comes last");
            Assert.Less(Array.IndexOf(args, "-y"), Array.IndexOf(args, "-i"), "global options lead");
        }
    }
}
