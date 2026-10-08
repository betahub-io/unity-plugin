using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;

namespace BetaHub.Tests
{
    [TestFixture]
    public class BugReportSessionTests
    {
        private const string Description = "It crashed";
        private const string Steps = "1. Do it";
        private const string ShotPath = "/tmp/screenshot.png";
        private const string PlayerLogPath = "/tmp/BH_Player.log";
        private const long TwoMegabytes = 2L * 1024 * 1024;

        private FakeBetaHubLog _log;
        private BugReportSession _session;

        [SetUp]
        public void SetUp()
        {
            _log = new FakeBetaHubLog();
            _session = new BugReportSession(_log);
        }

        private static AttachmentSources NoSources() => new AttachmentSources();

        private static AttachmentSources AllBuiltIns() => new AttachmentSources
        {
            RecordingSeconds = 60,
            PlayerLog = new PlayerLogSource(PlayerLogPath, TwoMegabytes),
            LocationCountry = true,
            LocationNetwork = true,
            Latency = true,
        };

        private static string[] Ids(AttachmentSet set) => set.Items.Select(i => i.Id).ToArray();

        private static AttachmentDescriptor Item(AttachmentSet set, string id) => set.Items.Single(i => i.Id == id);

        private class FakeProvider : IAttachmentProvider
        {
            private readonly Func<IEnumerable<AttachmentDescriptor>> _items;
            public FakeProvider(params AttachmentDescriptor[] items) { _items = () => items; }
            public FakeProvider(Func<IEnumerable<AttachmentDescriptor>> items) { _items = items; }
            public IEnumerable<AttachmentDescriptor> GetAttachments() => _items();
        }

        // --- recorder policy ---

        [Test]
        public void ShouldRecord_WhenIdleAndVideoEnabled_ReturnsTrue()
        {
            Assert.IsTrue(BugReportSession.ShouldRecord(videoEnabled: true, reportWindowVisible: false, uploadInProgress: false));
        }

        [Test]
        public void ShouldRecord_WhenReportWindowVisible_ReturnsFalse()
        {
            Assert.IsFalse(BugReportSession.ShouldRecord(videoEnabled: true, reportWindowVisible: true, uploadInProgress: false));
        }

        [Test]
        public void ShouldRecord_WhenUploadInProgress_ReturnsFalse()
        {
            Assert.IsFalse(BugReportSession.ShouldRecord(videoEnabled: true, reportWindowVisible: false, uploadInProgress: true));
        }

        [Test]
        public void ShouldRecord_WhenVideoDisabled_ReturnsFalse()
        {
            Assert.IsFalse(BugReportSession.ShouldRecord(videoEnabled: false, reportWindowVisible: false, uploadInProgress: false));
        }

        // --- CollectAttachments: built-ins ---

        [Test]
        public void Collect_NothingAvailable_IsEmpty()
        {
            var set = _session.CollectAttachments(NoSources());

            Assert.AreEqual(0, set.Items.Count);
        }

        [Test]
        public void Collect_BuiltIns_InDesignOrder_AllIncluded()
        {
            _session.SetFrameScreenshot(ShotPath);

            var set = _session.CollectAttachments(AllBuiltIns());

            CollectionAssert.AreEqual(new[]
            {
                BuiltInAttachments.Screenshot, BuiltInAttachments.Recording, BuiltInAttachments.PlayerLog,
                BuiltInAttachments.Latency, BuiltInAttachments.Location,
            }, Ids(set));
            CollectionAssert.AreEqual(Ids(set), set.IncludedIds.ToArray());
        }

        [Test]
        public void Collect_BuiltIns_CarryKindsPathsAndMeta()
        {
            _session.SetFrameScreenshot(ShotPath);

            var set = _session.CollectAttachments(AllBuiltIns());

            var shot = Item(set, BuiltInAttachments.Screenshot);
            Assert.AreEqual(AttachmentKind.Image, shot.Kind);
            Assert.AreEqual(ShotPath, shot.FilePath);
            Assert.IsTrue(shot.RemoveAfterUpload);

            var clip = Item(set, BuiltInAttachments.Recording);
            Assert.AreEqual(AttachmentKind.Clip, clip.Kind);
            StringAssert.Contains("60", clip.Name);

            var log = Item(set, BuiltInAttachments.PlayerLog);
            Assert.AreEqual(AttachmentKind.Log, log.Kind);
            Assert.AreEqual(PlayerLogPath, log.FilePath);
            Assert.IsFalse(log.RemoveAfterUpload);
            Assert.AreEqual("2 MB", log.Meta);

            Assert.AreEqual(AttachmentKind.Data, Item(set, BuiltInAttachments.Latency).Kind);
            Assert.AreEqual(AttachmentKind.Data, Item(set, BuiltInAttachments.Location).Kind);
        }

        [Test]
        public void Collect_PlayerLogAtOrOverCap_IsLeftOffTheListAndWarns()
        {
            var sources = new AttachmentSources { PlayerLog = new PlayerLogSource(PlayerLogPath, BugReportSession.MaxPlayerLogBytes) };

            var set = _session.CollectAttachments(sources);

            Assert.AreEqual(0, set.Items.Count);
            Assert.AreEqual(1, _log.Warnings.Count);
            StringAssert.Contains("log", _log.Warnings[0].ToLowerInvariant());
        }

        [Test]
        public void Collect_LocationMeta_NamesOnlyTheEnabledParts()
        {
            var cases = new[]
            {
                (country: true, network: true, meta: "country, ISP"),
                (country: true, network: false, meta: "country"),
                (country: false, network: true, meta: "ISP"),
            };
            foreach (var c in cases)
            {
                var set = _session.CollectAttachments(new AttachmentSources { LocationCountry = c.country, LocationNetwork = c.network });
                Assert.AreEqual(c.meta, Item(set, BuiltInAttachments.Location).Meta, $"country={c.country} network={c.network}");
            }
        }

        [Test]
        public void SetFrameScreenshot_ReplacesThePreviousOne()
        {
            _session.SetFrameScreenshot("/tmp/old.png");
            _session.SetFrameScreenshot(ShotPath);

            var set = _session.CollectAttachments(NoSources());

            Assert.AreEqual(1, set.Items.Count);
            Assert.AreEqual(ShotPath, set.Items[0].FilePath);
        }

        [Test]
        public void SetFrameScreenshot_Null_RemovesIt()
        {
            _session.SetFrameScreenshot(ShotPath);
            _session.SetFrameScreenshot(null);

            Assert.AreEqual(0, _session.CollectAttachments(NoSources()).Items.Count);
        }

        // --- CollectAttachments: developer-added files ---

        [Test]
        public void Collect_DeveloperFiles_GetOneRowEach_AfterBuiltIns()
        {
            _session.SetFrameScreenshot(ShotPath);
            _session.AddScreenshot("/tmp/extra.png", removeAfterUpload: true);
            _session.AddVideo("/tmp/clip.mp4", removeAfterUpload: false);
            _session.AddLogFile("/tmp/custom.log", removeAfterUpload: false);
            _session.AddFile("/tmp/slot2.sav", "Save file", removeAfterUpload: false);

            var set = _session.CollectAttachments(NoSources());

            Assert.AreEqual(5, set.Items.Count);
            Assert.AreEqual(BuiltInAttachments.Screenshot, set.Items[0].Id);
            CollectionAssert.AreEqual(
                new[] { AttachmentKind.Image, AttachmentKind.Clip, AttachmentKind.Log, AttachmentKind.File },
                set.Items.Skip(1).Select(i => i.Kind).ToArray());
            CollectionAssert.AreEqual(
                new[] { "/tmp/extra.png", "/tmp/clip.mp4", "/tmp/custom.log", "/tmp/slot2.sav" },
                set.Items.Skip(1).Select(i => i.FilePath).ToArray());
            Assert.AreEqual(set.Items.Count, set.Items.Select(i => i.Id).Distinct().Count(), "ids must be unique");
        }

        [Test]
        public void Collect_DeveloperFiles_NameDefaultsToFileName_AndMetaShowsSize()
        {
            _session.AddFile("/tmp/inventory.json", null, removeAfterUpload: false);
            var sources = new AttachmentSources { FileSize = path => path == "/tmp/inventory.json" ? TwoMegabytes : AttachmentSources.UnknownSize };

            var item = _session.CollectAttachments(sources).Items.Single();

            Assert.AreEqual("inventory.json", item.Name);
            Assert.AreEqual("2 MB", item.Meta);
        }

        [Test]
        public void Collect_DeveloperFile_WithUnknownSize_HasEmptyMeta()
        {
            _session.AddLogFile("/tmp/custom.log", removeAfterUpload: false);

            var item = _session.CollectAttachments(NoSources()).Items.Single();

            Assert.AreEqual("", item.Meta);
        }

        [Test]
        public void AddFile_WithEmptyPath_IsRejectedWithWarning()
        {
            _session.AddFile("", "Save", removeAfterUpload: false);
            _session.AddScreenshot(null, removeAfterUpload: false);

            Assert.AreEqual(0, _session.CollectAttachments(NoSources()).Items.Count);
            Assert.AreEqual(2, _log.Warnings.Count);
        }

        [Test]
        public void ClearAttachments_RemovesDeveloperFilesAndFrameScreenshot()
        {
            _session.SetFrameScreenshot(ShotPath);
            _session.AddScreenshot("/tmp/shot.png", true);
            _session.AddLogFile("/tmp/custom.log", true);
            _session.AddVideo("/tmp/clip.mp4", true);
            _session.AddFile("/tmp/slot2.sav", "Save", true);

            _session.ClearAttachments();

            Assert.AreEqual(0, _session.CollectAttachments(NoSources()).Items.Count);
        }

        // --- CollectAttachments: providers ---

        [Test]
        public void Collect_ProviderItems_FollowEverythingElse_InRegistrationOrder()
        {
            _session.SetFrameScreenshot(ShotPath);
            _session.RegisterAttachmentProvider(new FakeProvider(
                new AttachmentDescriptor { Id = "hw", Name = "Hardware", Kind = AttachmentKind.Data, Fields = new[] { Pair("gpu", "APU") } }));
            _session.RegisterAttachmentProvider(new FakeProvider(
                new AttachmentDescriptor { Id = "save", Name = "Save file", Kind = AttachmentKind.File, FilePath = "/tmp/slot2.sav", IncludedByDefault = false }));

            var set = _session.CollectAttachments(NoSources());

            CollectionAssert.AreEqual(new[] { BuiltInAttachments.Screenshot, "hw", "save" }, Ids(set));
            Assert.IsFalse(set.IsIncluded("save"), "provider's IncludedByDefault is respected");
        }

        [Test]
        public void Collect_ProviderIsAskedAtEveryOpen()
        {
            int calls = 0;
            _session.RegisterAttachmentProvider(new FakeProvider(() =>
            {
                calls++;
                return new[] { new AttachmentDescriptor { Id = "perf", Kind = AttachmentKind.Data, Fields = new[] { Pair("open", calls.ToString()) } } };
            }));

            _session.CollectAttachments(NoSources());
            var second = _session.CollectAttachments(NoSources());

            Assert.AreEqual(2, calls);
            Assert.AreEqual("2", second.Items.Single().Fields[0].Value);
        }

        [Test]
        public void Collect_ProviderItem_WithBuiltInOrDuplicateId_IsSkippedAndWarned()
        {
            _session.SetFrameScreenshot(ShotPath);
            _session.RegisterAttachmentProvider(new FakeProvider(
                new AttachmentDescriptor { Id = BuiltInAttachments.Screenshot, Kind = AttachmentKind.Image, FilePath = "/tmp/a.png" },
                new AttachmentDescriptor { Id = "save", Kind = AttachmentKind.File, FilePath = "/tmp/a.sav" },
                new AttachmentDescriptor { Id = "save", Kind = AttachmentKind.File, FilePath = "/tmp/b.sav" }));

            var set = _session.CollectAttachments(NoSources());

            CollectionAssert.AreEqual(new[] { BuiltInAttachments.Screenshot, "save" }, Ids(set));
            Assert.AreEqual(ShotPath, set.Items[0].FilePath);
            Assert.AreEqual("/tmp/a.sav", set.Items[1].FilePath);
            Assert.AreEqual(2, _log.Warnings.Count);
        }

        [Test]
        public void Collect_ProviderItem_WithoutPayload_IsSkippedAndWarned()
        {
            _session.RegisterAttachmentProvider(new FakeProvider(
                new AttachmentDescriptor { Id = "nofile", Kind = AttachmentKind.File },
                new AttachmentDescriptor { Id = "nodata", Kind = AttachmentKind.Data },
                new AttachmentDescriptor { Id = "", Kind = AttachmentKind.File, FilePath = "/tmp/a.sav" },
                null));

            var set = _session.CollectAttachments(NoSources());

            Assert.AreEqual(0, set.Items.Count);
            Assert.AreEqual(4, _log.Warnings.Count);
        }

        [Test]
        public void Collect_ProviderItem_WithoutName_IsNamedAfterItsId()
        {
            _session.RegisterAttachmentProvider(new FakeProvider(
                new AttachmentDescriptor { Id = "inventory", Kind = AttachmentKind.File, FilePath = "/tmp/inv.json" }));

            Assert.AreEqual("inventory", _session.CollectAttachments(NoSources()).Items.Single().Name);
        }

        [Test]
        public void Collect_ThrowingProvider_IsSkippedAndWarned()
        {
            _session.RegisterAttachmentProvider(new FakeProvider(() => throw new InvalidOperationException("boom")));
            _session.RegisterAttachmentProvider(new FakeProvider(
                new AttachmentDescriptor { Id = "save", Kind = AttachmentKind.File, FilePath = "/tmp/a.sav" }));

            var set = _session.CollectAttachments(NoSources());

            CollectionAssert.AreEqual(new[] { "save" }, Ids(set));
            Assert.AreEqual(1, _log.Warnings.Count);
            StringAssert.Contains("boom", _log.Warnings[0]);
        }

        [Test]
        public void UnregisterAttachmentProvider_StopsAskingIt()
        {
            var provider = new FakeProvider(new AttachmentDescriptor { Id = "save", Kind = AttachmentKind.File, FilePath = "/tmp/a.sav" });
            _session.RegisterAttachmentProvider(provider);

            _session.UnregisterAttachmentProvider(provider);

            Assert.AreEqual(0, _session.CollectAttachments(NoSources()).Items.Count);
        }

        // --- BuildReport ---

        [Test]
        public void BuildReport_CopiesDescriptionAndSteps()
        {
            var draft = _session.BuildReport(Description, Steps, _session.CollectAttachments(NoSources()));

            Assert.AreEqual(Description, draft.Description);
            Assert.AreEqual(Steps, draft.Steps);
        }

        [Test]
        public void BuildReport_RequiresAnAttachmentSet()
        {
            Assert.Throws<ArgumentNullException>(() => _session.BuildReport(Description, Steps, null));
        }

        [Test]
        public void BuildReport_SendsOnlyIncludedItems_InListOrder()
        {
            _session.SetFrameScreenshot(ShotPath);
            _session.AddFile("/tmp/slot2.sav", "Save", false);
            var set = _session.CollectAttachments(AllBuiltIns());
            set.SetIncluded(BuiltInAttachments.Recording, false);
            set.SetIncluded(BuiltInAttachments.Location, false);

            var draft = _session.BuildReport(Description, Steps, set);

            var fileId = set.Items.Single(i => i.Kind == AttachmentKind.File).Id;
            CollectionAssert.AreEqual(new[] { BuiltInAttachments.Screenshot, BuiltInAttachments.PlayerLog, fileId },
                draft.Uploads.Select(u => u.AttachmentId).ToArray());
            CollectionAssert.AreEqual(new[] { BuiltInAttachments.Latency }, draft.InlineAttachmentIds);
        }

        [Test]
        public void BuildReport_MapsEachKindToItsUpload()
        {
            _session.SetFrameScreenshot(ShotPath);
            _session.AddVideo("/tmp/clip.mp4", removeAfterUpload: true);
            _session.AddFile("/tmp/slot2.sav", "Save file", removeAfterUpload: false);
            var set = _session.CollectAttachments(new AttachmentSources { RecordingSeconds = 60, PlayerLog = new PlayerLogSource(PlayerLogPath, 10) });

            var uploads = _session.BuildReport(Description, Steps, set).Uploads;

            Assert.AreEqual(5, uploads.Count);
            Assert.AreEqual((UploadKind.Screenshot, ShotPath, true), (uploads[0].Kind, uploads[0].Path, uploads[0].RemoveAfterUpload));
            Assert.AreEqual(UploadKind.Recording, uploads[1].Kind);
            Assert.IsNull(uploads[1].Path);
            Assert.AreEqual((UploadKind.Log, PlayerLogPath, false), (uploads[2].Kind, uploads[2].Path, uploads[2].RemoveAfterUpload));
            Assert.AreEqual((UploadKind.Video, "/tmp/clip.mp4", true), (uploads[3].Kind, uploads[3].Path, uploads[3].RemoveAfterUpload));
            Assert.AreEqual((UploadKind.File, "/tmp/slot2.sav", "Save file"), (uploads[4].Kind, uploads[4].Path, uploads[4].Name));
        }

        [Test]
        public void BuildReport_ProviderData_IsSentAsATextFile()
        {
            _session.RegisterAttachmentProvider(new FakeProvider(new AttachmentDescriptor
            {
                Id = "hw", Name = "Hardware", Kind = AttachmentKind.Data,
                Fields = new[] { Pair("gpu", "Steam Deck APU"), Pair("ram", "16 GB") },
            }));

            var upload = _session.BuildReport(Description, Steps, _session.CollectAttachments(NoSources())).Uploads.Single();

            Assert.AreEqual(UploadKind.File, upload.Kind);
            Assert.AreEqual("Hardware", upload.Name);
            Assert.AreEqual("hw.txt", upload.FileName);
            Assert.IsNull(upload.Path);
            Assert.AreEqual("gpu: Steam Deck APU\nram: 16 GB\n", Encoding.UTF8.GetString(upload.Content));
        }

        [Test]
        public void BuildReport_NothingIncluded_HasNoUploads()
        {
            _session.SetFrameScreenshot(ShotPath);
            var set = _session.CollectAttachments(AllBuiltIns());
            foreach (var id in set.Items.Select(i => i.Id).ToArray()) set.SetIncluded(id, false);

            var draft = _session.BuildReport(Description, Steps, set);

            Assert.AreEqual(0, draft.Uploads.Count);
            Assert.AreEqual(0, draft.InlineAttachmentIds.Count);
        }

        // --- legacy toggles ---

        [Test]
        public void ApplyOptions_MapsTheThreeLegacyToggles()
        {
            _session.SetFrameScreenshot(ShotPath);
            _session.AddScreenshot("/tmp/extra.png", false);
            _session.AddLogFile("/tmp/custom.log", false);
            _session.AddVideo("/tmp/clip.mp4", false);
            _session.AddFile("/tmp/slot2.sav", "Save", false);
            var set = _session.CollectAttachments(AllBuiltIns());

            BugReportSession.ApplyOptions(set, new BugReportOptions { IncludeScreenshots = false, IncludeLogs = false, IncludeRecording = false });

            var included = set.Items.Where(i => set.IsIncluded(i.Id)).ToArray();
            CollectionAssert.AreEquivalent(
                new[] { AttachmentKind.Clip, AttachmentKind.File, AttachmentKind.Data, AttachmentKind.Data },
                included.Select(i => i.Kind).ToArray(),
                "developer videos, files and data stay; screenshots, logs and the recording go");
            Assert.IsFalse(set.IsIncluded(BuiltInAttachments.Recording));
        }

        [Test]
        public void ApplyOptions_All_ChangesNothing()
        {
            _session.SetFrameScreenshot(ShotPath);
            var set = _session.CollectAttachments(AllBuiltIns());

            BugReportSession.ApplyOptions(set, BugReportOptions.All);

            CollectionAssert.AreEqual(Ids(set), set.IncludedIds.ToArray());
        }

        // --- custom fields ---

        [Test]
        public void CollectCustomFields_ProviderOverridesStaticValue()
        {
            _session.SetCustomField("level", "static");
            _session.RegisterCustomFieldProvider("level", () => "dynamic");

            var fields = _session.CollectCustomFields();

            Assert.AreEqual("dynamic", fields["level"]);
        }

        [Test]
        public void CollectCustomFields_ProviderReturningNull_KeepsStaticValue()
        {
            _session.SetCustomField("level", "static");
            _session.RegisterCustomFieldProvider("level", () => null);

            var fields = _session.CollectCustomFields();

            Assert.AreEqual("static", fields["level"]);
        }

        [Test]
        public void CollectCustomFields_ThrowingProvider_IsSkippedAndWarned()
        {
            _session.SetCustomField("ok", "1");
            _session.RegisterCustomFieldProvider("broken", () => throw new InvalidOperationException("boom"));

            var fields = _session.CollectCustomFields();

            Assert.AreEqual(1, fields.Count);
            Assert.AreEqual(1, _log.Warnings.Count);
            StringAssert.Contains("broken", _log.Warnings[0]);
            StringAssert.Contains("boom", _log.Warnings[0]);
        }

        [Test]
        public void SetCustomField_RejectsInvalidInputWithWarning()
        {
            _session.SetCustomField("", "x");
            _session.SetCustomField("name", null);

            Assert.AreEqual(0, _session.CollectCustomFields().Count);
            Assert.AreEqual(2, _log.Warnings.Count);
        }

        [Test]
        public void RemoveCustomField_AndUnregisterProvider_Work()
        {
            _session.SetCustomField("a", "1");
            _session.RegisterCustomFieldProvider("b", () => "2");

            _session.RemoveCustomField("a");
            _session.UnregisterCustomFieldProvider("b");

            Assert.AreEqual(0, _session.CollectCustomFields().Count);
        }

        [Test]
        public void BuildReport_MergesExtraFieldsOverUserFields()
        {
            _session.SetCustomField("country", "user");
            var extra = new Dictionary<string, string> { { "country", "PL" }, { "latency", "20ms" } };

            var draft = _session.BuildReport(Description, Steps, _session.CollectAttachments(NoSources()), extra);

            Assert.AreEqual("PL", draft.CustomFields["country"]);
            Assert.AreEqual("20ms", draft.CustomFields["latency"]);
        }

        private static KeyValuePair<string, string> Pair(string key, string value) => new KeyValuePair<string, string>(key, value);
    }
}
