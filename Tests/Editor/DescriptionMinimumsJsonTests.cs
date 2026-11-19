using NUnit.Framework;

namespace BetaHub.Tests
{
    // JsonUtility is native: these run in Unity only, not in Tools~/EditTests.
    [TestFixture]
    public class DescriptionMinimumsJsonTests
    {
        [Test]
        public void FromProjectJson_ReadsBothMinimums()
        {
            var minimums = DescriptionMinimums.FromProjectJson(
                "{\"id\":\"pr-5287510306\",\"name\":\"Demo\",\"access\":\"public\",\"min_description_length\":50,\"min_suggestion_length\":80,\"latest_release\":{}}");
            Assert.IsNotNull(minimums);
            Assert.AreEqual((50, 80), (minimums.Bug, minimums.Suggestion));
        }

        [Test]
        public void FromProjectJson_WithoutTheMinimums_IsNull()
        {
            Assert.IsNull(DescriptionMinimums.FromProjectJson("{\"id\":\"pr-1\",\"min_description_length\":50}"), "an older BetaHub");
            Assert.IsNull(DescriptionMinimums.FromProjectJson("{\"id\":\"pr-1\"}"));
        }

        [Test]
        public void FromProjectJson_NotJson_IsNull()
        {
            Assert.IsNull(DescriptionMinimums.FromProjectJson("<html>Sign in</html>"));
            Assert.IsNull(DescriptionMinimums.FromProjectJson(""));
            Assert.IsNull(DescriptionMinimums.FromProjectJson(null));
        }
    }
}
