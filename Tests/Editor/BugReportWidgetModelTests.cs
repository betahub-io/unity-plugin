using System.Linq;
using NUnit.Framework;

namespace BetaHub.Tests
{
    [TestFixture]
    public class BugReportWidgetModelTests
    {
        private static readonly BugReportWidgetStrings S = new BugReportWidgetStrings();

        // --- which screen for which submission state ---

        // Nobody can type an email into a closed form, so a report waiting for one goes out without it.
        [Test]
        public void ShouldPublishWithoutEmail_OnlyWhenTheFormIsClosedAndTheReportWaits()
        {
            foreach (ReportSubmissionState state in System.Enum.GetValues(typeof(ReportSubmissionState)))
            {
                bool waits = state == ReportSubmissionState.UploadingMedia || state == ReportSubmissionState.AwaitingPublish;
                Assert.AreEqual(waits, BugReportWidgetModel.ShouldPublishWithoutEmail(state, formVisible: false), state + " closed");
                Assert.IsFalse(BugReportWidgetModel.ShouldPublishWithoutEmail(state, formVisible: true), state + " open");
            }
        }

        // --- field tags: the note, or a counter near the limit ---

        // Fields stop at their limit, so the count shows before the player runs into it.
        [Test]
        public void FieldTag_ShowsTheNoteUntilEightyPercentOfTheLimit()
        {
            Assert.AreEqual("required", BugReportWidgetModel.FieldTag("required", 0, 2000, S));
            Assert.AreEqual("required", BugReportWidgetModel.FieldTag("required", 1599, 2000, S));
            Assert.AreEqual("1,600 / 2,000", BugReportWidgetModel.FieldTag("required", 1600, 2000, S));
            Assert.AreEqual("2,000 / 2,000", BugReportWidgetModel.FieldTag("required", 2000, 2000, S));
        }

        [Test]
        public void FieldTag_RejectsALimitBelowOne()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => BugReportWidgetModel.FieldTag("optional", 0, 0, S));
        }

        [Test]
        public void IsAtLimit_OnlyWhenTheFieldIsFull()
        {
            Assert.IsFalse(BugReportWidgetModel.IsAtLimit(999, 1000));
            Assert.IsTrue(BugReportWidgetModel.IsAtLimit(1000, 1000));
        }

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
            Assert.IsFalse(BugReportWidgetModel.CanSend("", termsRequired: false, termsAccepted: false, configured: true, minLength: 0));
            Assert.IsFalse(BugReportWidgetModel.CanSend("   \n", false, false, true, 0));
            Assert.IsTrue(BugReportWidgetModel.CanSend("It broke", false, false, true, 0));
        }

        [Test]
        public void CanSend_TermsGateOnlyWhenRequired()
        {
            Assert.IsFalse(BugReportWidgetModel.CanSend("It broke", termsRequired: true, termsAccepted: false, configured: true, minLength: 0));
            Assert.IsTrue(BugReportWidgetModel.CanSend("It broke", termsRequired: true, termsAccepted: true, configured: true, minLength: 0));
        }

        [Test]
        public void CanSend_NeverWhenNotConfigured()
        {
            Assert.IsFalse(BugReportWidgetModel.CanSend("It broke", false, false, configured: false, minLength: 0));
        }

        [Test]
        public void CanSend_NeverOverTheLimit()
        {
            Assert.IsFalse(BugReportWidgetModel.CanSend(new string('x', BugReportWidgetModel.DescriptionLimit + 1), false, false, true, 0));
        }

        // BetaHub refuses a description shorter than the project's minimum; spaces around it don't count.
        [Test]
        public void CanSend_NotBelowTheMinimum()
        {
            Assert.IsFalse(BugReportWidgetModel.CanSend("  " + new string('x', 79) + "  ", false, false, true, minLength: 80));
            Assert.IsTrue(BugReportWidgetModel.CanSend(new string('x', 80), false, false, true, minLength: 80));
        }

        [Test]
        public void DescriptionTag_CountsUpToTheMinimum_ThenIsTheUsualTag()
        {
            Assert.AreEqual("12 / 80 min", BugReportWidgetModel.DescriptionTag(" twelve chars ", 80, S));
            Assert.AreEqual(S.Required, BugReportWidgetModel.DescriptionTag(new string('x', 80), 80, S));
            Assert.AreEqual("5 / 50 min", BugReportWidgetModel.DescriptionTag("short", 50, S), "a bug's minimum counts the same way");
            Assert.AreEqual(S.Required, BugReportWidgetModel.DescriptionTag("", 0, S), "no minimum");
            Assert.AreEqual("1,600 / 2,000", BugReportWidgetModel.DescriptionTag(new string('x', 1600), 80, S));
        }

        // --- fields and types ---

        [Test]
        public void FieldsFor_ABugHasSteps_ASuggestionDoesNot_TermsLast()
        {
            CollectionAssert.AreEqual(new[] { WidgetField.Type, WidgetField.Description, WidgetField.Steps },
                BugReportWidgetModel.FieldsFor(ReportType.Bug, termsRequired: false));
            CollectionAssert.AreEqual(new[] { WidgetField.Type, WidgetField.Description, WidgetField.Terms },
                BugReportWidgetModel.FieldsFor(ReportType.Suggestion, termsRequired: true));
        }

        [Test]
        public void TextsFor_ABug_SpeakOfAReport()
        {
            var bug = BugReportWidgetModel.TextsFor(ReportType.Bug, S);
            Assert.AreEqual(("Report a bug", "What happened", "Describe what happened", "Send report", "Report link", "Sent with this report", "Report sent"),
                (bug.Title, bug.DescriptionLabel, bug.DescriptionPrompt, bug.Send, bug.Link, bug.SentWith, bug.SentTitle));
        }

        // A suggestion is not a report of something that happened.
        [Test]
        public void TextsFor_ASuggestion_SpeakOfTheIdea()
        {
            var idea = BugReportWidgetModel.TextsFor(ReportType.Suggestion, S);
            Assert.AreEqual(("Suggest an idea", "Your idea", "Describe your idea", "Send suggestion", "Suggestion link", "Sent with this suggestion", "Suggestion sent"),
                (idea.Title, idea.DescriptionLabel, idea.DescriptionPrompt, idea.Send, idea.Link, idea.SentWith, idea.SentTitle));
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
            var header = BugReportWidgetModel.ComposeHeader(configured: true, isDevelopmentBuild: false, configurationError: null, submissionError: null, signedInAs: null, ReportType.Bug, S);
            Assert.AreEqual((BugReportWidgetModel.HeaderKind.Normal, S.Title, S.Subtitle, true), (header.Kind, header.Title, header.Subtitle, header.Tabs));
        }

        [Test]
        public void ComposeHeader_SignedIn_NamesTheReporter()
        {
            var header = BugReportWidgetModel.ComposeHeader(true, false, null, null, signedInAs: "Kasia_P", ReportType.Bug, S);
            Assert.AreEqual("Reporting as Kasia_P", header.Subtitle);
        }

        [Test]
        // The tabs stand where the title was, so the subtitle says the send failed.
        public void ComposeHeader_SubmissionError_IsTheSubtitle_UnderTheTabs()
        {
            var header = BugReportWidgetModel.ComposeHeader(true, false, null, submissionError: "No connection.", signedInAs: null, ReportType.Bug, S);
            Assert.AreEqual((BugReportWidgetModel.HeaderKind.Error, true), (header.Kind, header.Tabs));
            Assert.AreEqual("Couldn't send. No connection. Your text is kept.", header.Subtitle);
        }

        // BetaHub's messages can end in a space; the sentences keep one between them.
        [Test]
        public void ComposeHeader_SubmissionError_TrimsTheServerMessage()
        {
            var header = BugReportWidgetModel.ComposeHeader(true, false, null, submissionError: " Description must be at least 50 characters long. ", signedInAs: null, ReportType.Bug, S);
            Assert.AreEqual("Couldn't send. Description must be at least 50 characters long. Your text is kept.", header.Subtitle);
        }

        [Test]
        public void ComposeHeader_Suggestion_IsTitledSo()
        {
            var header = BugReportWidgetModel.ComposeHeader(true, false, null, null, null, ReportType.Suggestion, S);
            Assert.AreEqual(S.SuggestTitle, header.Title);
        }

        [Test]
        public void ComposeHeader_Misconfiguration_DetailOnlyInDevelopmentBuilds()
        {
            var dev = BugReportWidgetModel.ComposeHeader(configured: false, isDevelopmentBuild: true, configurationError: "Project ID is not set.", submissionError: null, signedInAs: null, ReportType.Bug, S);
            var player = BugReportWidgetModel.ComposeHeader(configured: false, isDevelopmentBuild: false, configurationError: "Project ID is not set.", submissionError: null, signedInAs: null, ReportType.Bug, S);

            Assert.AreEqual((BugReportWidgetModel.HeaderKind.Warning, S.UnavailableTitle, false), (dev.Kind, dev.Title, dev.Tabs));
            StringAssert.Contains("Project ID", dev.Subtitle);
            StringAssert.Contains("console", dev.Subtitle);
            Assert.AreEqual(S.UnavailableForPlayers, player.Subtitle);
        }

        [Test]
        public void SentHeader_NamesTheReportAndWhereItLives()
        {
            Assert.AreEqual("#4821 · saved anonymously", BugReportWidgetModel.SentHeader("4821", signedIn: false, ReportType.Bug, S).Subtitle);
            Assert.AreEqual("#4821 · under your BetaHub reports", BugReportWidgetModel.SentHeader("4821", signedIn: true, ReportType.Bug, S).Subtitle);
            Assert.AreEqual(S.SentTitle, BugReportWidgetModel.SentHeader("4821", false, ReportType.Bug, S).Title);
            Assert.AreEqual(S.SuggestionSentTitle, BugReportWidgetModel.SentHeader("12", false, ReportType.Suggestion, S).Title);
        }

        // --- attachment row values ---

        [Test]
        // The row's checkbox shows it is included; only leaving something out is spelt out.
        public void ComposeRow_SpellsOutOnlyLeftOut()
        {
            Assert.AreEqual(("", RowTone.Accent), Pair(BugReportWidgetModel.ComposeRowStatus(true, S)));
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
            Screen = WidgetScreen.Compose, Focus = focus, CanSend = true, Configured = true, FocusedIncluded = true, HasAttachments = true,
        };

        private static string Describe(PromptBar bar)
        {
            string Side(System.Collections.Generic.IEnumerable<Prompt> prompts) => string.Join(", ", prompts.Select(p =>
                p.Action + ":" + p.Label + (p.Primary ? "*" : "") + (p.Disabled ? "(off)" : "") + (p.Busy ? "(busy)" : "")));
            return Side(bar.Left) + " | " + Side(bar.Right);
        }

        private static string Describe(PromptContext context) => Describe(BugReportWidgetModel.Prompts(context, S));

        // Back is the first prompt on every screen, so it never moves; Move explains the arrows.
        [Test]
        public void Compose_FieldFocused()
        {
            Assert.AreEqual("Back:Close, Move:Move, Confirm:Write, Alt:Attachments | Send:Send report*", Describe(Compose()));
        }

        [Test]
        public void Compose_FieldFocused_NoAttachments_NoAttachmentsPrompt()
        {
            var context = Compose();
            context.HasAttachments = false;
            Assert.AreEqual("Back:Close, Move:Move, Confirm:Write | Send:Send report*", Describe(context));
        }

        [Test]
        public void Compose_SendDisabledUntilAllowed()
        {
            var context = Compose();
            context.CanSend = false;
            Assert.AreEqual("Back:Close, Move:Move, Confirm:Write, Alt:Attachments | Send:Send report(off)", Describe(context));
        }

        [Test]
        public void Compose_Typing_OffersDoneNextAndPrevious_OnKeyboardOnly()
        {
            var keyboard = Compose();
            keyboard.Typing = true;
            var gamepad = keyboard;
            gamepad.Gamepad = true;

            Assert.AreEqual("Back:Done, Next:Next, Prev:Previous | Send:Send report*", Describe(keyboard));
            Assert.AreEqual("Back:Done | Send:Send report*", Describe(gamepad));
        }

        [Test]
        public void Compose_AttachmentFocused_TogglesAndOpensAttachments()
        {
            var included = Compose(WidgetFocusKind.Attachment);
            var leftOut = included;
            leftOut.FocusedIncluded = false;

            Assert.AreEqual("Back:Close, Move:Move, Confirm:Leave out, Alt:Attachments | Send:Send report*", Describe(included));
            Assert.AreEqual("Back:Close, Move:Move, Confirm:Include, Alt:Attachments | Send:Send report*", Describe(leftOut));
        }

        [Test]
        public void Compose_TermsFocused()
        {
            var context = Compose(WidgetFocusKind.Terms);
            Assert.AreEqual("Back:Close, Move:Move, Confirm:Accept, Alt:Read terms | Send:Send report*", Describe(context));
            context.TermsAccepted = true;
            Assert.AreEqual("Back:Close, Move:Move, Confirm:Withdraw, Alt:Read terms | Send:Send report*", Describe(context));
        }

        [Test]
        public void Compose_TypeFocused_ChoosesWithLeftAndRight()
        {
            Assert.AreEqual("Back:Close, Move:Move, ChangeType:Bug or suggestion | Send:Send report*", Describe(Compose(WidgetFocusKind.Type)));
        }

        [Test]
        public void Compose_Suggestion_SendsASuggestion()
        {
            var context = Compose();
            context.Type = ReportType.Suggestion;
            Assert.AreEqual("Back:Close, Move:Move, Confirm:Write, Alt:Attachments | Send:Send suggestion*", Describe(context));
            context.HasError = true;
            Assert.AreEqual("Back:Close, Move:Move, Confirm:Write, Alt:Attachments | Send:Try again*", Describe(context));
        }

        [Test]
        public void Error_SendBecomesTryAgain()
        {
            var context = Compose();
            context.HasError = true;
            Assert.AreEqual("Back:Close, Move:Move, Confirm:Write, Alt:Attachments | Send:Try again*", Describe(context));
        }

        [Test]
        public void Unavailable_OnlyCloses()
        {
            var context = Compose();
            context.Configured = false;
            context.CanSend = false;
            Assert.AreEqual("Back:Close | Send:Send report(off)", Describe(context));
        }

        [Test]
        public void Preview()
        {
            var context = new PromptContext { Screen = WidgetScreen.Preview, FocusedIncluded = true };
            Assert.AreEqual("Back:Back to report, Navigate:Item, Confirm:Leave out | ", Describe(context));
        }

        // A log opens at its newest line: nothing newer to show, so that prompt is greyed out.
        [Test]
        public void Preview_OfALog_OffersOlderAndNewerLines_EachOffAtItsEnd()
        {
            var atEnd = new PromptContext { Screen = WidgetScreen.Preview, FocusedIncluded = true, PreviewIsLog = true, LogAtEnd = true };
            var atTop = atEnd;
            atTop.LogAtEnd = false;
            atTop.LogAtTop = true;

            Assert.AreEqual("Back:Back to report, Navigate:Item, ScrollUp:Older lines, ScrollDown:Newer lines(off), Confirm:Leave out | ", Describe(atEnd));
            Assert.AreEqual("Back:Back to report, Navigate:Item, ScrollUp:Older lines(off), ScrollDown:Newer lines, Confirm:Leave out | ", Describe(atTop));
        }

        [Test]
        public void Sending_IsOneBusyPrompt()
        {
            var context = new PromptContext { Screen = WidgetScreen.Sending };
            Assert.AreEqual(" | Send:Sending…(busy)", Describe(context));
        }

        [Test]
        public void Uploading_CanLeave()
        {
            var context = new PromptContext { Screen = WidgetScreen.Uploading };
            Assert.AreEqual("Back:Back to game · the upload continues | ", Describe(context));
        }

        [Test]
        public void Sent_WithEmail_FollowsFocus_BetweenTheEmailAndTheLink()
        {
            var email = new PromptContext { Screen = WidgetScreen.Sent, CanEmail = true, SentFocus = SentFocus.Email };
            var link = email;
            link.SentFocus = SentFocus.Link;
            var typing = email;
            typing.Typing = true;

            Assert.AreEqual("Back:Back to game, Move:Move, Confirm:Write, Alt:Copy link | Send:Email me a link*", Describe(email));
            Assert.AreEqual("Back:Back to game, Move:Move, Confirm:Copy link | Send:Email me a link*", Describe(link));
            Assert.AreEqual("Back:Done | Send:Email me a link*", Describe(typing));
        }

        // Signed in, after the email was saved, or a suggestion: going back is the main thing left.
        [Test]
        public void Sent_NoEmail_BackToGameLeads()
        {
            var context = new PromptContext { Screen = WidgetScreen.Sent, CanEmail = false };
            Assert.AreEqual("Back:Back to game*, Alt:Copy link | ", Describe(context));
        }

        // Prev (Shift+Tab) is only ever offered on a keyboard.
        [Test]
        public void EveryActionHasAGlyphOnEveryDevice()
        {
            foreach (GlyphSet set in System.Enum.GetValues(typeof(GlyphSet)))
            {
                foreach (PromptAction action in System.Enum.GetValues(typeof(PromptAction)))
                {
                    if (set != GlyphSet.Keyboard && action == PromptAction.Prev) continue;
                    CollectionAssert.IsNotEmpty(PromptGlyphs.For(set, action), set + " " + action);
                }
            }
            CollectionAssert.AreEqual(new[] { "Ctrl", "Enter" }, PromptGlyphs.Keyboard(PromptAction.Send));
            CollectionAssert.AreEqual(new[] { "Shift", "Tab" }, PromptGlyphs.Keyboard(PromptAction.Prev));
        }

        [Test]
        public void Keyboard_ArrowsPagesAndTabKeys()
        {
            CollectionAssert.AreEqual(new[] { "↑", "↓", "←", "→" }, PromptGlyphs.Keyboard(PromptAction.Move));
            CollectionAssert.AreEqual(new[] { "←", "→" }, PromptGlyphs.Keyboard(PromptAction.ChangeType));
            CollectionAssert.AreEqual(new[] { "PgUp" }, PromptGlyphs.Keyboard(PromptAction.ScrollUp));
            CollectionAssert.AreEqual(new[] { "PgDn" }, PromptGlyphs.Keyboard(PromptAction.ScrollDown));
            CollectionAssert.AreEqual(new[] { "1" }, PromptGlyphs.Keyboard(PromptAction.TabReport));
            CollectionAssert.AreEqual(new[] { "2" }, PromptGlyphs.Keyboard(PromptAction.TabAttachments));
        }

        // A pad shows chips: the d-pad, the right stick's direction, and each pad's own shoulder names.
        [Test]
        public void Pad_ChipsForDirectionsAndShoulders()
        {
            var chips = new[]
            {
                (GlyphSet.Xbox, PromptAction.Move, "▲ ▼ ◀ ▶"),
                (GlyphSet.Xbox, PromptAction.ChangeType, "◀ ▶"),
                (GlyphSet.Xbox, PromptAction.ScrollUp, "R ▲"),
                (GlyphSet.Xbox, PromptAction.ScrollDown, "R ▼"),
                (GlyphSet.Xbox, PromptAction.TabReport, "LB"),
                (GlyphSet.Xbox, PromptAction.TabAttachments, "RB"),
                (GlyphSet.PlayStation, PromptAction.TabReport, "L1"),
                (GlyphSet.PlayStation, PromptAction.TabAttachments, "R1"),
                (GlyphSet.Switch, PromptAction.TabReport, "L"),
                (GlyphSet.Switch, PromptAction.TabAttachments, "R"),
            };
            foreach (var (set, action, text) in chips)
            {
                var glyph = PromptGlyphs.For(set, action);
                Assert.AreEqual(1, glyph.Length, set + " " + action);
                Assert.AreEqual((GlyphPartKind.Dpad, text), (glyph[0].Kind, glyph[0].Text), set + " " + action);
            }
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

        // Tab and Shift+Tab: every field, then every attachment, and round again.
        [Test]
        public void Step_WalksFieldsThenAttachments_AndWraps()
        {
            var f = new WidgetFocus(3, 2, 2);
            var path = new System.Collections.Generic.List<string>();
            for (int i = 0; i < 5; i++) { f.Step(1); path.Add(At(f)); }
            CollectionAssert.AreEqual(new[] { "f1", "f2", "a0", "a1", "f0" }, path);

            f.Step(-1);
            Assert.AreEqual("a1", At(f), "back from the first field to the last attachment");
        }

        [Test]
        public void Step_NoAttachments_WrapsInTheFields()
        {
            var f = new WidgetFocus(2, 0, 1);
            f.Step(1); f.Step(1);
            Assert.AreEqual("f0", At(f));
        }

        // The attachments tab opens from a field on the attachment last visited.
        [Test]
        public void LastAttachment_IsTheOneFocusLeft()
        {
            var f = new WidgetFocus(2, 3, 1);
            Assert.AreEqual(0, f.LastAttachment);
            f.FocusAttachment(2);
            f.FocusField(0);
            Assert.AreEqual(2, f.LastAttachment);
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
