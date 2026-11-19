using System.Linq;
using NUnit.Framework;

namespace BetaHub.Tests
{
    [TestFixture]
    public class ThumbnailRingTests
    {
        [Test]
        public void FirstFrameIsAlwaysDue_ThenOnePerInterval()
        {
            var ring = new ThumbnailRing(capacity: 3, intervalSeconds: 10f);
            Assert.IsTrue(ring.IsDue(0f));
            ring.Claim(0f);
            Assert.IsFalse(ring.IsDue(9.9f));
            Assert.IsTrue(ring.IsDue(10f));
        }

        [Test]
        public void OverwritesTheOldest_AndListsOldestFirst()
        {
            var ring = new ThumbnailRing(3, 10f);
            var slots = new[] { 0f, 10f, 20f, 30f }.Select(t => ring.Claim(t)).ToArray();

            CollectionAssert.AreEqual(new[] { 0, 1, 2, 0 }, slots);
            CollectionAssert.AreEqual(new[] { 1, 2, 0 }, ring.Ordered().Select(e => e.Slot).ToArray());
            CollectionAssert.AreEqual(new[] { 10f, 20f, 30f }, ring.Ordered().Select(e => e.Time).ToArray());
        }

        [Test]
        public void Clear_StartsOver()
        {
            var ring = new ThumbnailRing(3, 10f);
            ring.Claim(0f);
            ring.Claim(10f);
            ring.Clear();

            Assert.AreEqual(0, ring.Count);
            Assert.IsTrue(ring.IsDue(11f));
            Assert.AreEqual(0, ring.Claim(11f));
        }

        [Test]
        public void RejectsNonsense()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new ThumbnailRing(0, 10f));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new ThumbnailRing(3, 0f));
        }
    }
}
