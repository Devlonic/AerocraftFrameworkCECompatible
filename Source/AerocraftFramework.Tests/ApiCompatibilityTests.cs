using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Xunit;
using Xunit.Abstractions;

namespace AerocraftFramework.Tests
{
    /// <summary>
    /// The original mod broke because its CE build called CompAmmoUser.LoadAmmo(Thing), which the installed
    /// Combat Extended no longer has. These tests resolve every reference of the built assemblies against the
    /// installed game, Harmony and Combat Extended, and compare the public API with the original mod.
    /// </summary>
    public class ApiCompatibilityTests
    {
        private readonly ITestOutputHelper output;

        public ApiCompatibilityTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Fact]
        public void CoreAssemblyReferencesResolveAgainstTheGame()
        {
            if (!TestPaths.GameInstalled)
            {
                output.WriteLine("RimWorld not found: skipped");
                return;
            }
            List<string> broken = AssemblyModel.UnresolvedReferences(TestPaths.CoreDll, new[] { TestPaths.ManagedDir, TestPaths.HarmonyDir });
            Assert.True(broken.Count == 0, "Unresolved references:\n" + string.Join("\n", broken));
        }

        [Fact]
        public void CeAssemblyReferencesResolveAgainstInstalledCombatExtended()
        {
            if (!TestPaths.GameInstalled || !File.Exists(TestPaths.CombatExtendedDll))
            {
                output.WriteLine("RimWorld or Combat Extended not found: skipped");
                return;
            }
            string[] dirs = { TestPaths.ManagedDir, TestPaths.HarmonyDir, Path.GetDirectoryName(TestPaths.CombatExtendedDll), Path.GetDirectoryName(TestPaths.CoreDll) };
            List<string> broken = AssemblyModel.UnresolvedReferences(TestPaths.CeDll, dirs);
            Assert.True(broken.Count == 0, "Unresolved references:\n" + string.Join("\n", broken));
        }

        [Fact]
        public void TheOriginalCeBuildIsBrokenAgainstInstalledCombatExtended()
        {
            // Documents the root cause of "pawns cannot reload aircraft".
            string original = Path.Combine(TestPaths.OriginalModDir, "1.5", "CombatExtended", "Assemblies", "AerocraftFramework.dll");
            if (!TestPaths.GameInstalled || !File.Exists(TestPaths.CombatExtendedDll) || !File.Exists(original))
            {
                output.WriteLine("original mod, game or CE not found: skipped");
                return;
            }
            string[] dirs = { TestPaths.ManagedDir, TestPaths.HarmonyDir, Path.GetDirectoryName(TestPaths.CombatExtendedDll) };
            List<string> broken = AssemblyModel.UnresolvedReferences(original, dirs);
            foreach (string line in broken)
            {
                output.WriteLine(line);
            }
            Assert.Contains(broken, b => b.Contains("CompAmmoUser::LoadAmmo"));
        }

        /// <summary>Types of the original that were internal helpers and are intentionally gone.</summary>
        /// <summary>Members that were UI internals or need Combat Extended types, removed on purpose.</summary>
        private static readonly HashSet<string> RemovedMembers = new HashSet<string>
        {
            "ITab_Aerocraft_Weapon.DrawThing_ExtraWeapon",
            "Building_Aerocraft_Base.InventoryAmmo",
        };

        private static bool HasMethod(TypeDefinition type, string name)
        {
            while (type != null)
            {
                if (type.Methods.Any(m => m.Name == name))
                {
                    return true;
                }
                try
                {
                    type = type.BaseType?.Resolve();
                }
                catch (AssemblyResolutionException)
                {
                    return false;
                }
            }
            return false;
        }

        private static readonly HashSet<string> RemovedTypes = new HashSet<string>
        {
            "MYDE_AerocraftFramework.CELogger",
            "MYDE_AerocraftFramework.JobGiverUtils_Reload",
            "MYDE_AerocraftFramework.MYDE_AerocraftFramework_Patch",
        };

        [Fact]
        public void PublicApiOfTheOriginalIsKept()
        {
            string originalCore = Path.Combine(TestPaths.OriginalModDir, "1.5", "Assemblies", "AerocraftFramework.dll");
            string originalCe = Path.Combine(TestPaths.OriginalModDir, "1.5", "CombatExtended", "Assemblies", "AerocraftFramework.dll");
            if (!File.Exists(originalCore) || !File.Exists(originalCe))
            {
                output.WriteLine("original mod not found: skipped");
                return;
            }
            AssemblyModel ours = new AssemblyModel(new[] { TestPaths.CoreDll, TestPaths.CeDll }, new[] { TestPaths.ManagedDir, TestPaths.HarmonyDir });
            List<string> problems = new List<string>();
            foreach (string path in new[] { originalCore, originalCe })
            {
                AssemblyDefinition original = AssemblyDefinition.ReadAssembly(path);
                foreach (TypeDefinition type in original.MainModule.Types.Where(t => t.IsPublic && t.Namespace == "MYDE_AerocraftFramework"))
                {
                    if (RemovedTypes.Contains(type.FullName) || type.FullName.StartsWith("MYDE_AerocraftFramework.MYDE_Harmony_"))
                    {
                        continue;
                    }
                    TypeDefinition mine = ours.Find(type.FullName);
                    if (mine == null)
                    {
                        problems.Add("missing type " + type.FullName);
                        continue;
                    }
                    foreach (FieldDefinition field in type.Fields.Where(f => f.IsPublic))
                    {
                        FieldDefinition myField = mine.Fields.FirstOrDefault(f => f.Name == field.Name);
                        if (myField == null)
                        {
                            problems.Add($"missing field {type.Name}.{field.Name}");
                        }
                        else if (myField.FieldType.FullName != field.FieldType.FullName)
                        {
                            problems.Add($"field {type.Name}.{field.Name} changed type {field.FieldType.FullName} -> {myField.FieldType.FullName}");
                        }
                    }
                    foreach (MethodDefinition method in type.Methods.Where(m => m.IsPublic && !m.IsConstructor && !m.IsSpecialName))
                    {
                        if (!RemovedMembers.Contains(type.Name + "." + method.Name) && !HasMethod(mine, method.Name))
                        {
                            problems.Add($"missing method {type.Name}.{method.Name}");
                        }
                    }
                }
            }
            Assert.True(problems.Count == 0, string.Join("\n", problems.Distinct()));
        }
    }
}
