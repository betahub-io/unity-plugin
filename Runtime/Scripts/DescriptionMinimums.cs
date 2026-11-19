namespace BetaHub
{
    using System;
    using UnityEngine;

    /// <summary>
    /// The shortest description BetaHub takes for each report type, as the project sets it
    /// (projects/{id}.json). Read by <see cref="BetaHubReporter.LoadDescriptionMinimums"/>.
    /// </summary>
    public sealed class DescriptionMinimums
    {
        public int Bug { get; }
        public int Suggestion { get; }

        public DescriptionMinimums(int bug, int suggestion)
        {
            if (bug < 1) throw new ArgumentOutOfRangeException(nameof(bug), bug, "A minimum is at least 1.");
            if (suggestion < 1) throw new ArgumentOutOfRangeException(nameof(suggestion), suggestion, "A minimum is at least 1.");
            Bug = bug;
            Suggestion = suggestion;
        }

        public int For(ReportType type) => type == ReportType.Suggestion ? Suggestion : Bug;

        /// <summary>The minimums in the project's JSON. Null when the reply holds none, e.g. from an older BetaHub.</summary>
        public static DescriptionMinimums FromProjectJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            Reply reply;
            try
            {
                reply = JsonUtility.FromJson<Reply>(json);
            }
            catch (ArgumentException)
            {
                return null;
            }
            // JsonUtility leaves a missing number at 0
            if (reply == null || reply.min_description_length < 1 || reply.min_suggestion_length < 1) return null;
            return new DescriptionMinimums(reply.min_description_length, reply.min_suggestion_length);
        }

        /// <summary>
        /// Whether BetaHub refused the read, so asking again gets the same answer: a private project
        /// hides itself from a project token. No reply or a server error may pass.
        /// </summary>
        public static bool IsRefusal(long responseCode) => responseCode >= 400 && responseCode < 500;

        [Serializable]
        private class Reply
        {
            public int min_description_length;
            public int min_suggestion_length;
        }
    }
}
