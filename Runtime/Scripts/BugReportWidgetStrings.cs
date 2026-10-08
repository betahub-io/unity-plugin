using System;

namespace BetaHub
{
    /// <summary>Player-facing copy. Edit in the inspector or swap the whole object for another language.</summary>
    [Serializable]
    public class BugReportWidgetStrings
    {
        public string Kicker = "BetaHub";
        public string Title = "Report a bug";
        public string Subtitle = "Goes straight to the developers";
        public string SubtitleSignedIn = "Reporting as {0}";
        public string ErrorTitle = "Couldn't send";
        public string ErrorSubtitle = "{0} Your text is kept.";
        public string UnavailableTitle = "Bug reporting unavailable";
        public string UnavailableForPlayers = "Bug reporting isn't available right now.";

        public string DescriptionLabel = "What happened";
        public string DescriptionPrompt = "Describe what happened";
        public string StepsLabel = "Steps to reproduce";
        public string StepsPrompt = "Add steps if you can. It helps a lot.";
        public string Required = "required";
        public string Optional = "optional";
        public string TermsLabel = "Terms of bug reporting";
        public string TermsAccepted = "Accepted";
        public string TermsNotAccepted = "Not accepted";

        public string AttachmentsGroup = "Sending along";
        public string AttachmentsCount = "{0} of {1}";
        public string Included = "Included";
        public string LeftOut = "Left out";
        public string Queued = "Queued";
        public string Sent = "Sent";
        public string Failed = "Failed";
        public string Percent = "{0}%";

        public string ScreenshotCaption = "Your screenshot · this frame";
        public string NoScreenshot = "No screenshot this time";
        public string LatencyNote = "Measured while this form is open";
        public string LocationNote = "Looked up when you send, from one request to BetaHub. No GPS.";
        public string FileAddedBy = "Added by the game";
        public string ClipNoFrames = "Frames appear after a few seconds of play";
        public string PreviewCount = "{0} of {1}";
        public string PreviewShot = "{0} × {1} · the frame under this form";
        public string PreviewShotNoSize = "The frame under this form";
        public string PreviewClip = "One frame every 10 s · ends when you opened this";
        public string PreviewVideo = "Video file · sent as is";
        public string PreviewLog = "Last lines · the full file is sent";

        public string Write = "Write";
        public string Close = "Close";
        public string Done = "Done";
        public string NextField = "Next field";
        public string Include = "Include";
        public string LeaveOut = "Leave out";
        public string Accept = "Accept";
        public string Withdraw = "Withdraw";
        public string ReadTerms = "Read terms";
        public string FullScreen = "Full screen";
        public string Item = "Item";
        public string BackToForm = "Back to form";
        public string SendReport = "Send report";
        public string Sending = "Sending…";
        public string TryAgain = "Try again";
        public string BackToGame = "Back to game";
        public string BackToGameUploading = "Back to game · the upload continues";
        public string CopyLink = "Copy link";
        public string LinkCopied = "Link copied";
        public string EmailMeALink = "Email me a link";

        public string UploadingTitle = "Almost there";
        public string UploadingSubtitle = "Your report is saved. Uploading attachments.";
        public string SentTitle = "Report sent";
        public string SentAnonymous = "#{0} · saved anonymously";
        public string SentSignedIn = "#{0} · under your BetaHub reports";
        public string EmailLabel = "Email me when it's fixed";
        public string EmailPrompt = "you@example.com";
        public string EmailNote = "One email when it's fixed. Nothing else, no account.";
        public string EmailSaved = "We'll email {0} when it's fixed.";
        public string EmailInvalid = "That doesn't look like an email address.";
        public string EmailFailed = "Couldn't save your email. Try again or just go back.";
        public string SignedInNote = "We'll notify you on BetaHub when it's fixed.";
        public string ReportLink = "Report link";
        public string Copy = "Copy";
        public string SentWith = "Sent with this report";
        public string TextOnly = "Text only";

        public string ToastUploading = "Uploading your report";
        public string ToastUploadingSub = "{0}% · the game continues";
        public string ToastDone = "Report sent";
        public string ToastDoneSub = "Thank you";
        public string ToastFailed = "Report not sent";
    }
}
