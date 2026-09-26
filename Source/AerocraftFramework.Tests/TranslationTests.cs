using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace AerocraftFramework.Tests
{
    public class TranslationTests
    {
        private static Dictionary<string, string> Keyed(string language)
        {
            Dictionary<string, string> keys = new Dictionary<string, string>();
            string dir = Path.Combine(TestPaths.RepoRoot, "Languages", language, "Keyed");
            foreach (string file in Directory.EnumerateFiles(dir, "*.xml"))
            {
                foreach (XElement element in XDocument.Load(file).Root.Elements())
                {
                    Assert.False(keys.ContainsKey(element.Name.LocalName), $"{language}: duplicate key {element.Name.LocalName}");
                    keys[element.Name.LocalName] = element.Value;
                }
            }
            return keys;
        }

        private static IEnumerable<string> KeysUsedInCode()
        {
            Regex literal = new Regex("\"(AerocraftFramework_[A-Za-z0-9_]+)\"");
            string source = Path.Combine(TestPaths.RepoRoot, "Source");
            return Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) && !f.Contains("AerocraftFramework.Tests"))
                .SelectMany(f => literal.Matches(File.ReadAllText(f)).Select(m => m.Groups[1].Value))
                .Distinct();
        }

        [Fact]
        public void EveryKeyUsedInCodeHasEnglishText()
        {
            Dictionary<string, string> english = Keyed("English");
            List<string> missing = KeysUsedInCode().Where(k => !english.ContainsKey(k)).ToList();
            Assert.True(missing.Count == 0, "Missing English keys: " + string.Join(", ", missing));
        }

        [Fact]
        public void UkrainianTranslatesEveryEnglishKeyWithTheSameArguments()
        {
            Dictionary<string, string> english = Keyed("English");
            Dictionary<string, string> ukrainian = Keyed("Ukrainian");
            List<string> problems = new List<string>();
            Regex argument = new Regex(@"\{\d+\}");
            foreach (KeyValuePair<string, string> pair in english)
            {
                if (!ukrainian.TryGetValue(pair.Key, out string translated))
                {
                    problems.Add("missing " + pair.Key);
                    continue;
                }
                string[] a = argument.Matches(pair.Value).Select(m => m.Value).OrderBy(x => x).ToArray();
                string[] b = argument.Matches(translated).Select(m => m.Value).OrderBy(x => x).ToArray();
                if (!a.SequenceEqual(b))
                {
                    problems.Add($"{pair.Key}: arguments {string.Join("", a)} vs {string.Join("", b)}");
                }
            }
            Assert.True(problems.Count == 0, string.Join("\n", problems));
        }

        [Fact]
        public void UkrainianDefInjectionsTargetExistingDefs()
        {
            HashSet<string> defNames = new HashSet<string>(TestPaths.XmlFiles(Path.Combine(TestPaths.RepoRoot, "1.5"))
                .SelectMany(f => XDocument.Load(f).Descendants("defName").Select(d => d.Value.Trim())));
            List<string> problems = new List<string>();
            foreach (string root in new[] { Path.Combine(TestPaths.RepoRoot, "Languages", "Ukrainian", "DefInjected"), Path.Combine(TestPaths.RepoRoot, "1.5", "CombatExtended", "Languages", "Ukrainian", "DefInjected") })
            {
                foreach (string file in Directory.EnumerateFiles(root, "*.xml", SearchOption.AllDirectories))
                {
                    foreach (XElement element in XDocument.Load(file).Root.Elements())
                    {
                        string defName = element.Name.LocalName.Split('.')[0];
                        if (!defNames.Contains(defName))
                        {
                            problems.Add($"{Path.GetFileName(file)}: {element.Name.LocalName}");
                        }
                    }
                }
            }
            Assert.True(problems.Count == 0, string.Join("\n", problems));
        }
    }
}
