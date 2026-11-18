using System;
using NUnit.Framework;

namespace BetaHub.Tests
{
    [TestFixture]
    public class DescriptionMinimumsTests
    {
        [Test]
        public void For_PicksTheReportTypesMinimum()
        {
            var minimums = new DescriptionMinimums(bug: 50, suggestion: 80);
            Assert.AreEqual(50, minimums.For(ReportType.Bug));
            Assert.AreEqual(80, minimums.For(ReportType.Suggestion));
        }

        // BetaHub keeps both at 1 or more.
        [Test]
        public void Constructor_RejectsAMinimumBelowOne()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new DescriptionMinimums(0, 80));
            Assert.Throws<ArgumentOutOfRangeException>(() => new DescriptionMinimums(50, 0));
        }

        // A private project hides itself from a project token: asking again gets the same answer.
        // No reply or a server error may pass.
        [Test]
        public void IsRefusal_OnlyForClientErrors()
        {
            foreach (long code in new long[] { 401, 403, 404 }) Assert.IsTrue(DescriptionMinimums.IsRefusal(code), code.ToString());
            foreach (long code in new long[] { 0, 500, 502, 503 }) Assert.IsFalse(DescriptionMinimums.IsRefusal(code), code.ToString());
        }
    }
}
