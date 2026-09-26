using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace AerocraftFramework.Tests
{
    /// <summary>
    /// Locations of the repository, the game and the installed mods. Override with the environment variables
    /// RIMWORLD_DIR, WORKSHOP_DIR and CE_DLL. Checks that need the game are skipped when it is not installed.
    /// </summary>
    internal static class TestPaths
    {
        public static string RepoRoot
        {
            get
            {
                DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory);
                while (dir != null && !(File.Exists(Path.Combine(dir.FullName, "LoadFolders.xml")) && Directory.Exists(Path.Combine(dir.FullName, "About"))))
                {
                    dir = dir.Parent;
                }
                return dir?.FullName ?? throw new InvalidOperationException("Repository root not found");
            }
        }

        public static string RimWorldDir => Environment.GetEnvironmentVariable("RIMWORLD_DIR") ?? @"D:\Games\Steam\steamapps\common\RimWorld";

        public static string ManagedDir => Path.Combine(RimWorldDir, "RimWorldWin64_Data", "Managed");

        public static bool GameInstalled => File.Exists(Path.Combine(ManagedDir, "Assembly-CSharp.dll"));

        public static string WorkshopDir => Environment.GetEnvironmentVariable("WORKSHOP_DIR") ?? Path.GetFullPath(Path.Combine(RimWorldDir, "..", "..", "workshop", "content", "294100"));

        public static string CombatExtendedDll => Environment.GetEnvironmentVariable("CE_DLL") ?? Path.Combine(RimWorldDir, "Mods", "CombatExtended", "Assemblies", "CombatExtended.dll");

        public static string HarmonyDir
        {
            get
            {
                string workshop = Path.Combine(WorkshopDir, "2009463077", "Current", "Assemblies");
                if (Directory.Exists(workshop))
                {
                    return workshop;
                }
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages", "lib.harmony", "2.3.3", "lib", "net472");
            }
        }

        public static string CoreDll => Path.Combine(RepoRoot, "1.5", "Assemblies", "AerocraftFramework.dll");

        public static string CeDll => Path.Combine(RepoRoot, "1.5", "CombatExtended", "Assemblies", "AerocraftFramework.CE.dll");

        /// <summary>The original Aerocraft Framework from the Steam workshop.</summary>
        public static string OriginalModDir => Path.Combine(WorkshopDir, "2959802157");

        /// <summary>Installed mods that depend on Aerocraft Framework (RimThunder - Gruppa Krovi...).</summary>
        public static IEnumerable<string> AddonDirs
        {
            get
            {
                if (!Directory.Exists(WorkshopDir))
                {
                    yield break;
                }
                foreach (string dir in Directory.GetDirectories(WorkshopDir))
                {
                    string about = Path.Combine(dir, "About", "About.xml");
                    if (!File.Exists(about))
                    {
                        continue;
                    }
                    XDocument doc;
                    try
                    {
                        doc = XDocument.Load(about);
                    }
                    catch
                    {
                        continue;
                    }
                    bool depends = doc.Descendants("modDependencies").Descendants("packageId")
                        .Any(p => string.Equals(p.Value.Trim(), "MYDE.AerocraftFramework", StringComparison.OrdinalIgnoreCase));
                    if (depends)
                    {
                        yield return dir;
                    }
                }
            }
        }

        public static IEnumerable<string> XmlFiles(string root, params string[] excludedTopFolders)
        {
            if (!Directory.Exists(root))
            {
                return Enumerable.Empty<string>();
            }
            return Directory.EnumerateFiles(root, "*.xml", SearchOption.AllDirectories)
                .Where(f =>
                {
                    string relative = Path.GetRelativePath(root, f);
                    string first = relative.Split(Path.DirectorySeparatorChar)[0];
                    return !excludedTopFolders.Contains(first, StringComparer.OrdinalIgnoreCase) && !relative.Contains(Path.DirectorySeparatorChar + "Languages" + Path.DirectorySeparatorChar);
                });
        }
    }
}
