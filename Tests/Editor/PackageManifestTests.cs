using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace BetaHub.Tests
{
    // package.json is what the Package Manager shows: the samples it lists must exist and say what they are.
    [TestFixture]
    public class PackageManifestTests
    {
        private static readonly Regex Sample = new Regex(
            "\\{\\s*\"displayName\":\\s*\"(?<name>[^\"]+)\",\\s*\"description\":\\s*\"(?<description>[^\"]+)\",\\s*\"path\":\\s*\"(?<path>[^\"]+)\"\\s*\\}");

        [Test]
        public void EverySample_HasADescription_AndShips()
        {
            string manifest = ManifestPath();
            string json = File.ReadAllText(manifest);
            var samples = Sample.Matches(json).Cast<Match>().ToList();
            int listed = Regex.Matches(json, "\"path\":\\s*\"Samples~/").Count;

            Assert.IsNotEmpty(samples, "No sample in package.json.");
            Assert.AreEqual(listed, samples.Count, "Every sample needs a displayName, a description and a path, in that order.");
            foreach (var sample in samples)
            {
                string folder = Path.Combine(Path.GetDirectoryName(manifest), sample.Groups["path"].Value);
                Assert.IsTrue(Directory.Exists(folder), sample.Groups["name"].Value + ": no folder at " + folder);
                Assert.IsNotEmpty(Directory.GetFiles(folder, "*.cs"), sample.Groups["name"].Value + " has no script.");
            }
        }

        // The Unity workspace links only the package's folders in, not package.json: this runs in Tools~/EditTests.
        private static string ManifestPath([CallerFilePath] string thisFile = "")
        {
            string path = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile), "..", "..", "package.json"));
            if (!File.Exists(path)) Assert.Ignore("package.json is not next to the tests here; run from the repository.");
            return path;
        }
    }
}
