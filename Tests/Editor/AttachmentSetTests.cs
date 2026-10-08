using System.Linq;
using NUnit.Framework;

namespace BetaHub.Tests
{
    [TestFixture]
    public class AttachmentSetTests
    {
        private static AttachmentDescriptor Desc(string id, AttachmentKind kind = AttachmentKind.Data, bool on = true)
        {
            return new AttachmentDescriptor { Id = id, Name = id, Meta = id + " meta", Kind = kind, IncludedByDefault = on };
        }

        [Test]
        public void PreservesOrderGiven()
        {
            var set = new AttachmentSet(new[] { Desc("shot", AttachmentKind.Image), Desc("clip", AttachmentKind.Clip), Desc("log", AttachmentKind.Log), Desc("net") });
            Assert.AreEqual(new[] { "shot", "clip", "log", "net" }, set.Items.Select(i => i.Id).ToArray());
        }

        [Test]
        public void RespectsIncludedByDefault()
        {
            var set = new AttachmentSet(new[] { Desc("shot"), Desc("perf", on: false) });
            Assert.IsTrue(set.IsIncluded("shot"));
            Assert.IsFalse(set.IsIncluded("perf"));
            CollectionAssert.AreEqual(new[] { "shot" }, set.IncludedIds.ToArray());
        }

        [Test]
        public void ToggleAndSetFlipInclusion()
        {
            var set = new AttachmentSet(new[] { Desc("shot") });
            set.Toggle("shot");
            Assert.IsFalse(set.IsIncluded("shot"));
            set.SetIncluded("shot", true);
            Assert.IsTrue(set.IsIncluded("shot"));
        }

        [Test]
        public void UnknownIdIsSafe()
        {
            var set = new AttachmentSet(new[] { Desc("shot") });
            Assert.IsFalse(set.IsIncluded("nope"));
            Assert.DoesNotThrow(() => set.SetIncluded("nope", true));
            Assert.DoesNotThrow(() => set.Toggle("nope"));
        }

        [Test]
        public void IncludedIdsFollowItemOrder()
        {
            var set = new AttachmentSet(new[] { Desc("a"), Desc("b"), Desc("c") });
            set.SetIncluded("b", false);
            CollectionAssert.AreEqual(new[] { "a", "c" }, set.IncludedIds.ToArray());
        }

        [Test]
        public void DuplicateIdKeepsTheFirst()
        {
            var first = Desc("shot", AttachmentKind.Image);
            var set = new AttachmentSet(new[] { first, Desc("shot", AttachmentKind.Log, on: false) });
            Assert.AreEqual(1, set.Items.Count);
            Assert.AreSame(first, set.Items[0]);
            Assert.IsTrue(set.IsIncluded("shot"));
        }

        [Test]
        public void EmptySetIsValid()
        {
            var set = new AttachmentSet(System.Array.Empty<AttachmentDescriptor>());
            Assert.AreEqual(0, set.Items.Count);
            CollectionAssert.IsEmpty(set.IncludedIds.ToArray());
        }

        // column layout decision lives in the widget model
        [Test]
        public void ColumnCountIsOneUpToSixThenTwo()
        {
            Assert.AreEqual(1, BugReportWidgetModel.ColumnCountFor(1));
            Assert.AreEqual(1, BugReportWidgetModel.ColumnCountFor(6));
            Assert.AreEqual(2, BugReportWidgetModel.ColumnCountFor(7));
            Assert.AreEqual(2, BugReportWidgetModel.ColumnCountFor(9));
        }
    }
}
