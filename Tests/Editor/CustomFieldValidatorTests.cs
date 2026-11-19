using System.Collections.Generic;
using NUnit.Framework;

namespace BetaHub.Tests
{
    public class CustomFieldValidatorTests
    {
        private const string FieldsUrl = "https://app.betahub.io/projects/pr-1/custom_issue_fields";
        private static readonly CustomFieldRequirement Country = new CustomFieldRequirement("country", "text", "country code");

        // BetaHub creates a missing field with the first report that carries it.
        [Test]
        public void MissingField_IsFine()
        {
            var problems = CustomFieldValidator.FieldProblems(new CustomIssueField[0], new[] { Country }, FieldsUrl);
            CollectionAssert.IsEmpty(problems);
        }

        // Values arrive as issue[custom], which fills a field whether or not testers can set it.
        [Test]
        public void FieldTestersCannotSet_IsFine()
        {
            var existing = new[] { new CustomIssueField { name = "country", field_type = "text", tester_settable = false } };
            CollectionAssert.IsEmpty(CustomFieldValidator.FieldProblems(existing, new[] { Country }, FieldsUrl));
        }

        [Test]
        public void FieldOfAnotherType_IsNamedWithBothTypes()
        {
            var existing = new[] { new CustomIssueField { name = "country", field_type = "select", tester_settable = true } };
            var problems = CustomFieldValidator.FieldProblems(existing, new[] { Country }, FieldsUrl);

            Assert.AreEqual(1, problems.Count);
            StringAssert.Contains("'country'", problems[0]);
            StringAssert.Contains("select", problems[0]);
            StringAssert.Contains("text", problems[0]);
            StringAssert.Contains(FieldsUrl, problems[0]);
        }
    }
}
