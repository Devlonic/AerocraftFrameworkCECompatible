using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Mono.Cecil;
using Xunit;
using Xunit.Abstractions;

namespace AerocraftFramework.Tests
{
    /// <summary>
    /// Addons (RimThunder - Gruppa Krovi and others) only talk to the framework through XML: class names in
    /// thingClass/Class attributes and the field names of the CompProperties they fill in. Every such reference in
    /// our own defs and in every installed addon must resolve against the built assemblies.
    /// </summary>
    public class AddonXmlCompatibilityTests
    {
        private const string Namespace = "MYDE_AerocraftFramework.";
        private static readonly HashSet<string> ClassValueElements = new HashSet<string>
        {
            "thingClass", "driverClass", "giverClass", "worldObjectClass", "verbClass", "compClass", "workerClass", "li", "tabWindowClass"
        };

        private readonly ITestOutputHelper output;

        public AddonXmlCompatibilityTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private static AssemblyModel LoadOurs()
        {
            return new AssemblyModel(new[] { TestPaths.CoreDll, TestPaths.CeDll }, new[] { TestPaths.ManagedDir, TestPaths.HarmonyDir, Path.GetDirectoryName(TestPaths.CombatExtendedDll) });
        }

        private static List<string> CheckFile(AssemblyModel model, string file)
        {
            List<string> problems = new List<string>();
            XDocument doc = XDocument.Load(file);
            foreach (XElement element in doc.Descendants())
            {
                string cls = (string)element.Attribute("Class");
                if (cls != null && cls.StartsWith(Namespace))
                {
                    TypeDefinition type = model.Find(cls);
                    if (type == null)
                    {
                        problems.Add($"{file}: missing class {cls}");
                    }
                    else if (model.DerivesFrom(type, "Verse.CompProperties") && TestPaths.GameInstalled)
                    {
                        foreach (XElement child in element.Elements())
                        {
                            if (model.FindField(type, child.Name.LocalName) == null)
                            {
                                problems.Add($"{file}: {cls} has no field <{child.Name.LocalName}>");
                            }
                        }
                    }
                }
                if (!element.HasElements && ClassValueElements.Contains(element.Name.LocalName))
                {
                    string value = element.Value.Trim();
                    if (value.StartsWith(Namespace) && model.Find(value) == null)
                    {
                        problems.Add($"{file}: missing class {value} in <{element.Name.LocalName}>");
                    }
                }
            }
            return problems;
        }

        [Fact]
        public void OurDefsAndPatchesReferenceExistingClassesAndFields()
        {
            AssemblyModel model = LoadOurs();
            List<string> problems = TestPaths.XmlFiles(TestPaths.RepoRoot, "Source", "Languages", "About", ".git").SelectMany(f => CheckFile(model, f)).ToList();
            Assert.True(problems.Count == 0, string.Join("\n", problems));
        }

        [Fact]
        public void InstalledAddonsReferenceExistingClassesAndFields()
        {
            List<string> addons = TestPaths.AddonDirs.ToList();
            if (addons.Count == 0)
            {
                output.WriteLine("no addon installed: skipped");
                return;
            }
            AssemblyModel model = LoadOurs();
            List<string> problems = new List<string>();
            int files = 0;
            foreach (string addon in addons)
            {
                output.WriteLine("addon: " + addon);
                foreach (string file in TestPaths.XmlFiles(addon, "Languages", "About"))
                {
                    files++;
                    problems.AddRange(CheckFile(model, file));
                }
            }
            output.WriteLine($"{files} addon files checked");
            Assert.True(problems.Count == 0, string.Join("\n", problems));
        }

        private static Dictionary<string, string> DefNames(string root)
        {
            Dictionary<string, string> names = new Dictionary<string, string>();
            foreach (string file in TestPaths.XmlFiles(root))
            {
                XDocument doc = XDocument.Load(file);
                if (doc.Root?.Name.LocalName != "Defs")
                {
                    continue;
                }
                foreach (XElement def in doc.Root.Elements())
                {
                    string defName = def.Element("defName")?.Value.Trim();
                    if (defName != null)
                    {
                        names[def.Name.LocalName + "/" + defName] = file;
                    }
                    string name = (string)def.Attribute("Name");
                    if (name != null)
                    {
                        names["Name/" + name] = file;
                    }
                }
            }
            return names;
        }

        [Fact]
        public void EveryDefOfTheOriginalStillExists()
        {
            string original = Path.Combine(TestPaths.OriginalModDir, "1.5");
            if (!Directory.Exists(original))
            {
                output.WriteLine("original mod not found: skipped");
                return;
            }
            Dictionary<string, string> theirs = DefNames(original);
            Dictionary<string, string> ours = DefNames(Path.Combine(TestPaths.RepoRoot, "1.5"));
            List<string> missing = theirs.Keys.Where(k => !ours.ContainsKey(k)).ToList();
            Assert.True(missing.Count == 0, "Missing defs: " + string.Join(", ", missing));
        }

        [Fact]
        public void AddonParentDefsExist()
        {
            Dictionary<string, string> ours = DefNames(Path.Combine(TestPaths.RepoRoot, "1.5"));
            List<string> missing = new List<string>();
            foreach (string addon in TestPaths.AddonDirs)
            {
                foreach (string file in TestPaths.XmlFiles(addon, "Languages", "About"))
                {
                    foreach (XElement def in XDocument.Load(file).Descendants().Where(e => ((string)e.Attribute("ParentName"))?.StartsWith("MYDE_") == true))
                    {
                        string parent = (string)def.Attribute("ParentName");
                        if (!ours.ContainsKey("Name/" + parent))
                        {
                            missing.Add($"{file}: parent {parent}");
                        }
                    }
                }
            }
            Assert.True(missing.Count == 0, string.Join("\n", missing));
        }

        [Fact]
        public void AboutAndLoadFoldersAreConsistent()
        {
            XDocument about = XDocument.Load(Path.Combine(TestPaths.RepoRoot, "About", "About.xml"));
            Assert.Equal("MYDE.AerocraftFramework", about.Root.Element("packageId")?.Value);
            Assert.Contains("1.5", about.Root.Element("supportedVersions").Elements("li").Select(e => e.Value));
            XDocument loadFolders = XDocument.Load(Path.Combine(TestPaths.RepoRoot, "LoadFolders.xml"));
            foreach (XElement li in loadFolders.Descendants("li"))
            {
                string folder = Path.Combine(TestPaths.RepoRoot, li.Value.Trim().TrimStart('/'));
                Assert.True(Directory.Exists(folder), "missing load folder " + li.Value);
            }
            Assert.True(File.Exists(TestPaths.CoreDll), "core assembly not built");
            Assert.True(File.Exists(TestPaths.CeDll), "CE assembly not built");
            Assert.False(File.Exists(Path.Combine(TestPaths.RepoRoot, "1.5", "CombatExtended", "Assemblies", "AerocraftFramework.dll")), "a copy of the core assembly in the CE folder would replace it");
            Assert.False(File.Exists(Path.Combine(TestPaths.RepoRoot, "1.5", "CombatExtended", "Assemblies", "CombatExtended.dll")), "Combat Extended must not be shipped");
        }

        [Fact]
        public void AllXmlFilesAreWellFormed()
        {
            List<string> problems = new List<string>();
            foreach (string file in Directory.EnumerateFiles(TestPaths.RepoRoot, "*.xml", SearchOption.AllDirectories).Where(f => !f.Contains(Path.DirectorySeparatorChar + "Source" + Path.DirectorySeparatorChar) && !f.Contains(Path.DirectorySeparatorChar + ".git" + Path.DirectorySeparatorChar)))
            {
                try
                {
                    XDocument.Load(file);
                }
                catch (Exception e)
                {
                    problems.Add(file + ": " + e.Message);
                }
            }
            Assert.True(problems.Count == 0, string.Join("\n", problems));
        }
    }
}
