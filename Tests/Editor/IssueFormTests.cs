using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace BetaHub.Tests
{
    public class IssueFormTests
    {
        // issue[custom] is where BetaHub creates a field the project doesn't have yet (text, for developers),
        // as for the Unreal plugin; issue[custom_fields] only fills fields that exist and testers can set.
        [Test]
        public void CustomFields_GoWhereBetaHubCreatesMissingOnes()
        {
            var fields = Issue.DraftFormFields("A description long enough", "", 0, null,
                new Dictionary<string, string> { { "level", "Forest 3" } });

            CollectionAssert.Contains(fields, new KeyValuePair<string, string>("issue[custom][level]", "Forest 3"));
            Assert.IsFalse(fields.Any(f => f.Key.StartsWith("issue[custom_fields]")), "no field goes the old way");
        }

        [Test]
        public void ReleaseId_WinsOverTheLabel()
        {
            var fields = Issue.DraftFormFields("d", "s", 42, "1.2.0", null);

            CollectionAssert.Contains(fields, new KeyValuePair<string, string>("issue[release_id]", "42"));
            Assert.IsFalse(fields.Any(f => f.Key == "issue[release_label]"));
            CollectionAssert.Contains(fields, new KeyValuePair<string, string>("draft", "true"));
        }
    }
}
