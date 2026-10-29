using System.Linq;
using NUnit.Framework;

namespace BetaHub.Tests
{
    [TestFixture]
    public class BugReportWidgetModelTests
    {
        private static readonly BugReportWidgetStrings S = new BugReportWidgetStrings();

        // --- which screen for which submission state ---

        // A plain test with a table: NUnit3TestAdapter 3.11 drops [TestCase] methods with enum arguments.
        [Test]
        public void ScreenFor_MapsEverySubmissionState()
        {
            var expected = new[]
            {
                (ReportSubmissionState.Preparing, WidgetScreen.Sending),
                (ReportSubmissionState.CreatingDraft, WidgetScreen.Sending),
                (ReportSubmissionState.UploadingMedia, WidgetScreen.Uploading),
                (ReportSubmissionState.AwaitingPublish, WidgetScreen.Sent),
                (ReportSubmissionState.Publishing, WidgetScreen.Sent),
                (ReportSubmissionState.Published, WidgetScreen.Sent),
                (ReportSubmissionState.Failed, WidgetScreen.Compose),
            };
            foreach (var (state, screen) in expected)
            {
                Assert.AreEqual(screen, BugReportWidgetModel.ScreenFor(state), state.ToString());
            }
            Assert.AreEqual(expected.Length, System.Enum.GetValues(typeof(ReportSubmissionState)).Length, "every state is covered");
        }

        // --- send gating ---

        [Test]
        public void CanSend_RequiresNonBlankDescription()
        {
            Assert.IsFalse(BugReportWidgetModel.CanSend("", termsRequired: false, termsAccepted: false, configured: true));
            Assert.IsFalse(BugReportWidgetModel.CanSend("   \n", false, false, true));
            Assert.IsTrue(BugReportWidgetModel.CanSend("It broke", false, false, true));
        }

        [Test]
        public void CanSend_TermsGateOnlyWhenRequired()
        {
            Assert.IsFalse(BugReportWidgetModel.CanSend("It broke", termsRequired: true, termsAccepted: false, configured: true));
            Assert.IsTrue(BugReportWidgetModel.CanSend("It broke", termsRequired: true, termsAccepted: true, configured: true));
        }

        [Test]
        public void CanSend_NeverWhenNotConfigured()
        {
            Assert.IsFalse(BugReportWidgetModel.CanSend("It broke", false, false, configured: false));
        }

        [Test]
        public void CanSend_NeverOverTheLimit()
        {
            Assert.IsFalse(BugReportWidgetModel.CanSend(new string('x', BugReportWidgetModel.DescriptionLimit + 1), false, false, true));
        }

        // --- columns ---

        [Test]
        public void ColumnCount_IsOneUpToFive_ThenTwo()
        {
            Assert.AreEqual(1, BugReportWidgetModel.ColumnCountFor(1));
            Assert.AreEqual(1, BugReportWidgetModel.ColumnCountFor(5));
            Assert.AreEqual(2, BugReportWidgetModel.ColumnCountFor(6));
            Assert.AreEqual(2, BugReportWidgetModel.ColumnCountFor(9));
        }

        // --- text helpers ---

        [Test]
        public void FormatDuration_UsesMinutesAndSeconds()
        {
            Assert.AreEqual("1:00", BugReportWidgetModel.FormatDuration(60));
            Assert.AreEqual("0:45", BugReportWidgetModel.FormatDuration(45));
            Assert.AreEqual("2:05", BugReportWidgetModel.FormatDuration(125));
        }

        [Test]
        public void FormatFileSize_PicksUnit()
        {
            Assert.AreEqual("312 KB", BugReportWidgetModel.FormatFileSize(312L * 1024));
            Assert.AreEqual("2 MB", BugReportWidgetModel.FormatFileSize(2L * 1024 * 1024));
            Assert.AreEqual("900 B", BugReportWidgetModel.FormatFileSize(900));
        }

        [Test]
        public void LogTail_ReturnsLastNonEmptyLines()
        {
            string log = "a\nb\n\nc\nd\n";
            Assert.AreEqual("b\nc\nd", BugReportWidgetModel.LogTail(log, 3));
        }

        [Test]
        public void LogTail_HandlesShortAndEmptyInput()
        {
            Assert.AreEqual("only", BugReportWidgetModel.LogTail("only", 4));
            Assert.AreEqual("", BugReportWidgetModel.LogTail("", 4));
            Assert.AreEqual("", BugReportWidgetModel.LogTail(null, 4));
        }

        [Test]
        public void Truncate_KeepsWithinLimitWithEllipsis()
        {
            Assert.AreEqual("abcdef", BugReportWidgetModel.Truncate("abcdef", 6));
            Assert.AreEqual("abcde…", BugReportWidgetModel.Truncate("abcdefgh", 6));
        }

        // --- headers: errors replace the title, never a banner ---

        [Test]
        public void ComposeHeader_Anonymous_SaysWhereItGoes()
        {
            var header = BugReportWidgetModel.ComposeHeader(configured: true, isDevelopmentBuild: false, configurationError: null, submissionError: null, signedInAs: null, S);
            Assert.AreEqual((BugReportWidgetModel.HeaderKind.Normal, S.Title, S.Subtitle), (header.Kind, header.Title, header.Subtitle));
        }

        [Test]
        public void ComposeHeader_SignedIn_NamesTheReporter()
        {
            var header = BugReportWidgetModel.ComposeHeader(true, false, null, null, signedInAs: "Kasia_P", S);
            Assert.AreEqual("Reporting as Kasia_P", header.Subtitle);
        }

        [Test]
        public void ComposeHeader_SubmissionError_TakesTheTitle_AndKeepsText()
        {
            var header = BugReportWidgetModel.ComposeHeader(true, false, null, submissionError: "No connection.", signedInAs: null, S);
            Assert.AreEqual(BugReportWidgetModel.HeaderKind.Error, header.Kind);
            Assert.AreEqual(S.ErrorTitle, header.Title);
            Assert.AreEqual("No connection. Your text is kept.", header.Subtitle);
        }

        [Test]
        public void ComposeHeader_Misconfiguration_DetailOnlyInDevelopmentBuilds()
        {
            var dev = BugReportWidgetModel.ComposeHeader(configured: false, isDevelopmentBuild: true, configurationError: "Project ID is not set.", submissionError: null, signedInAs: null, S);
            var player = BugReportWidgetModel.ComposeHeader(configured: false, isDevelopmentBuild: false, configurationError: "Project ID is not set.", submissionError: null, signedInAs: null, S);

            Assert.AreEqual((BugReportWidgetModel.HeaderKind.Warning, S.UnavailableTitle), (dev.Kind, dev.Title));
            StringAssert.Contains("Project ID", dev.Subtitle);
            StringAssert.Contains("console", dev.Subtitle);
            Assert.AreEqual(S.UnavailableForPlayers, player.Subtitle);
        }

        [Test]
        public void SentHeader_NamesTheReportAndWhereItLives()
        {
            Assert.AreEqual("#4821 · saved anonymously", BugReportWidgetModel.SentHeader("4821", signedIn: false, S).Subtitle);
            Assert.AreEqual("#4821 · under your BetaHub reports", BugReportWidgetModel.SentHeader("4821", signedIn: true, S).Subtitle);
            Assert.AreEqual(S.SentTitle, BugReportWidgetModel.SentHeader("4821", false, S).Title);
        }

        // --- attachment row values ---

        [Test]
        public void ComposeRow_ShowsIncludedOrLeftOut()
        {
            Assert.AreEqual((S.Included, RowTone.Accent), Pair(BugReportWidgetModel.ComposeRowStatus(true, S)));
            Assert.AreEqual((S.LeftOut, RowTone.Muted), Pair(BugReportWidgetModel.ComposeRowStatus(false, S)));
        }

        [Test]
        public void UploadRow_FollowsTheUploadState()
        {
            var cases = new[]
            {
                (new AttachmentUploadState(AttachmentUploadStatus.Queued, 0f), S.Queued, RowTone.Muted, -1f),
                (new AttachmentUploadState(AttachmentUploadStatus.Uploading, 0.45f), "45%", RowTone.Accent, 0.45f),
                (new AttachmentUploadState(AttachmentUploadStatus.Sent, 1f), S.Sent, RowTone.Ok, -1f),
                (new AttachmentUploadState(AttachmentUploadStatus.Failed, 1f), S.Failed, RowTone.Error, -1f),
            };
            foreach (var (state, text, tone, progress) in cases)
            {
                var row = BugReportWidgetModel.UploadRowStatus(included: true, state, S);
                Assert.AreEqual((text, tone, progress), (row.Text, row.Tone, row.Progress), state.Status.ToString());
            }
        }

        [Test]
        public void UploadRow_LeftOutItemsStayLeftOut()
        {
            var row = BugReportWidgetModel.UploadRowStatus(included: false, default, S);
            Assert.AreEqual((S.LeftOut, RowTone.Muted), Pair(row));
        }

        [Test]
        public void FormatPercent_RoundsDown_SoNothingShows100BeforeItIsSent()
        {
            Assert.AreEqual("45%", BugReportWidgetModel.FormatPercent(0.459f, S));
            Assert.AreEqual("99%", BugReportWidgetModel.FormatPercent(0.999f, S));
            Assert.AreEqual("0%", BugReportWidgetModel.FormatPercent(-1f, S));
            for (int i = 0; i <= 100; i++)
            {
                Assert.AreEqual(i, BugReportWidgetModel.Percent(i / 100f), "exact percent " + i);
            }
        }

        private static (string, RowTone) Pair(RowStatus row) => (row.Text, row.Tone);
    }

    [TestFixture]
    public class BugReportWidgetPromptTests
    {
        private static readonly BugReportWidgetStrings S = new BugReportWidgetStrings();

        private static PromptContext Compose(WidgetFocusKind focus = WidgetFocusKind.Field) => new PromptContext
        {
            Screen = WidgetScreen.Compose, Focus = focus, CanSend = true, Configured = true, FocusedIncluded = true,
        };

        private static string Describe(PromptBar bar)
        {
            string Side(System.Collections.Generic.IEnumerable<Prompt> prompts) => string.Join(", ", prompts.Select(p =>
                p.Action + ":" + p.Label + (p.Primary ? "*" : "") + (p.Disabled ? "(off)" : "") + (p.Busy ? "(busy)" : "")));
            return Side(bar.Left) + " | " + Side(bar.Right);
        }

        [Test]
        public void Compose_FieldFocused()
        {
            Assert.AreEqual("Confirm:Write, Back:Close | Send:Send report*", Describe(BugReportWidgetModel.Prompts(Compose(), S)));
        }

        [Test]
        public void Compose_SendDisabledUntilAllowed()
        {
            var context = Compose();
            context.CanSend = false;
            Assert.AreEqual("Confirm:Write, Back:Close | Send:Send report(off)", Describe(BugReportWidgetModel.Prompts(context, S)));
        }

        [Test]
        public void Compose_Typing_OffersDoneAndNextField_OnKeyboardOnly()
        {
            var keyboard = Compose();
            keyboard.Typing = true;
            var gamepad = keyboard;
            gamepad.Gamepad = true;

            Assert.AreEqual("Back:Done, Next:Next field | Send:Send report*", Describe(BugReportWidgetModel.Prompts(keyboard, S)));
            Assert.AreEqual("Back:Done | Send:Send report*", Describe(BugReportWidgetModel.Prompts(gamepad, S)));
        }

        [Test]
        public void Compose_AttachmentFocused_TogglesAndPreviews()
        {
            var included = Compose(WidgetFocusKind.Attachment);
            var leftOut = included;
            leftOut.FocusedIncluded = false;

            Assert.AreEqual("Confirm:Leave out, Alt:Full screen, Back:Close | Send:Send report*", Describe(BugReportWidgetModel.Prompts(included, S)));
            Assert.AreEqual("Confirm:Include, Alt:Full screen, Back:Close | Send:Send report*", Describe(BugReportWidgetModel.Prompts(leftOut, S)));
        }

        [Test]
        public void Compose_TermsFocused()
        {
            var context = Compose(WidgetFocusKind.Terms);
            Assert.AreEqual("Confirm:Accept, Alt:Read terms, Back:Close | Send:Send report*", Describe(BugReportWidgetModel.Prompts(context, S)));
            context.TermsAccepted = true;
            Assert.AreEqual("Confirm:Withdraw, Alt:Read terms, Back:Close | Send:Send report*", Describe(BugReportWidgetModel.Prompts(context, S)));
        }

        [Test]
        public void Error_SendBecomesTryAgain()
        {
            var context = Compose();
            context.HasError = true;
            Assert.AreEqual("Confirm:Write, Back:Close | Send:Try again*", Describe(BugReportWidgetModel.Prompts(context, S)));
        }

        [Test]
        public void Unavailable_OnlyCloses()
        {
            var context = Compose();
            context.Configured = false;
            context.CanSend = false;
            Assert.AreEqual("Back:Close | Send:Send report(off)", Describe(BugReportWidgetModel.Prompts(context, S)));
        }

        [Test]
        public void Preview()
        {
            var context = new PromptContext { Screen = WidgetScreen.Preview, FocusedIncluded = true };
            Assert.AreEqual("Navigate:Item, Confirm:Leave out, Back:Back to form | ", Describe(BugReportWidgetModel.Prompts(context, S)));
        }

        [Test]
        public void Sending_IsOneBusyPrompt()
        {
            var context = new PromptContext { Screen = WidgetScreen.Sending };
            Assert.AreEqual(" | Send:Sending…(busy)", Describe(BugReportWidgetModel.Prompts(context, S)));
        }

        [Test]
        public void Uploading_CanLeave()
        {
            var context = new PromptContext { Screen = WidgetScreen.Uploading };
            Assert.AreEqual("Back:Back to game · the upload continues | ", Describe(BugReportWidgetModel.Prompts(context, S)));
        }

        [Test]
        public void Sent_Anonymous_OffersEmail_SignedIn_DoesNot()
        {
            var anonymous = new PromptContext { Screen = WidgetScreen.Sent, CanEmail = true };
            var signedIn = new PromptContext { Screen = WidgetScreen.Sent, SignedIn = true };

            Assert.AreEqual("Confirm:Write, Alt:Copy link, Back:Back to game | Send:Email me a link*", Describe(BugReportWidgetModel.Prompts(anonymous, S)));
            Assert.AreEqual("Alt:Copy link | Back:Back to game*", Describe(BugReportWidgetModel.Prompts(signedIn, S)));
        }

        [Test]
        public void EveryActionHasAGlyphOnEveryDevice()
        {
            foreach (GlyphSet set in System.Enum.GetValues(typeof(GlyphSet)))
            {
                foreach (PromptAction action in System.Enum.GetValues(typeof(PromptAction)))
                {
                    CollectionAssert.IsNotEmpty(PromptGlyphs.For(set, action), set + " " + action);
                }
            }
            CollectionAssert.AreEqual(new[] { "Ctrl", "Enter" }, PromptGlyphs.Keyboard(PromptAction.Send));
        }

        [Test]
        public void Sent_AfterTheEmailWasSaved_LooksLikeSignedIn()
        {
            var context = new PromptContext { Screen = WidgetScreen.Sent, CanEmail = false };
            Assert.AreEqual("Alt:Copy link | Back:Back to game*", Describe(BugReportWidgetModel.Prompts(context, S)));
        }
    }

    [TestFixture]
    public class BugReportWidgetFocusTests
    {
        private static string At(WidgetFocus f) => (f.OnAttachment ? "a" : "f") + f.Index;

        [Test]
        public void StartsOnTheFirstField()
        {
            Assert.AreEqual("f0", At(new WidgetFocus(fieldCount: 2, attachmentCount: 3, columns: 1)));
        }

        [Test]
        public void DownWalksFieldsThenAttachments_UpWalksBack()
        {
            var f = new WidgetFocus(2, 3, 1);
            var path = new System.Collections.Generic.List<string>();
            for (int i = 0; i < 5; i++) { f.Move(FocusMove.Down); path.Add(At(f)); }
            CollectionAssert.AreEqual(new[] { "f1", "a0", "a1", "a2", "a2" }, path);

            path.Clear();
            for (int i = 0; i < 5; i++) { f.Move(FocusMove.Up); path.Add(At(f)); }
            CollectionAssert.AreEqual(new[] { "a1", "a0", "f1", "f0", "f0" }, path);
        }

        [Test]
        public void Move_ReportsWhetherFocusChanged()
        {
            var f = new WidgetFocus(2, 0, 1);
            Assert.IsFalse(f.Move(FocusMove.Up));
            Assert.IsTrue(f.Move(FocusMove.Down));
            Assert.IsFalse(f.Move(FocusMove.Down), "no attachments: the last field is the end");
            Assert.IsFalse(f.Move(FocusMove.Right));
        }

        [Test]
        public void RightAndLeft_JumpBetweenSides_RememberingWhereYouWere()
        {
            var f = new WidgetFocus(2, 3, 1);
            f.Move(FocusMove.Down);            // f1
            f.Move(FocusMove.Right);
            Assert.AreEqual("a0", At(f));
            f.Move(FocusMove.Down);            // a1
            f.Move(FocusMove.Left);
            Assert.AreEqual("f1", At(f), "back to the field you left");
            f.Move(FocusMove.Right);
            Assert.AreEqual("a1", At(f), "back to the row you left");
        }

        [Test]
        public void TwoColumns_GridMoves()
        {
            // a0 a1
            // a2 a3
            // a4 a5
            // a6
            var f = new WidgetFocus(2, 7, 2);
            f.FocusAttachment(0);

            f.Move(FocusMove.Right); Assert.AreEqual("a1", At(f));
            Assert.IsFalse(f.Move(FocusMove.Right), "right edge");
            f.Move(FocusMove.Down); Assert.AreEqual("a3", At(f));
            f.Move(FocusMove.Down); Assert.AreEqual("a5", At(f));
            f.Move(FocusMove.Down); Assert.AreEqual("a6", At(f), "short last row: the item below-left");
            f.Move(FocusMove.Up); Assert.AreEqual("a4", At(f));
            f.Move(FocusMove.Left); Assert.AreEqual("f0", At(f), "left column jumps to the fields");
        }

        [Test]
        public void TwoColumns_UpFromTheTopRowGoesToTheLastField()
        {
            var f = new WidgetFocus(3, 6, 2);
            f.FocusAttachment(1);
            f.Move(FocusMove.Up);
            Assert.AreEqual("f2", At(f));
        }

        [Test]
        public void Resize_ClampsFocus()
        {
            var f = new WidgetFocus(2, 9, 2);
            f.FocusAttachment(8);
            f.Resize(fieldCount: 2, attachmentCount: 3, columns: 1);
            Assert.AreEqual("a2", At(f));
            f.Resize(2, 0, 1);
            Assert.AreEqual("f1", At(f), "attachments gone: last field");
        }

        [Test]
        public void FocusField_OutOfRange_Throws()
        {
            var f = new WidgetFocus(2, 1, 1);
            Assert.Throws<System.ArgumentOutOfRangeException>(() => f.FocusField(2));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => f.FocusAttachment(1));
        }

        [Test]
        public void NeedsAtLeastOneField()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new WidgetFocus(0, 1, 1));
        }
    }
}
