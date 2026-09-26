using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;

namespace AerocraftFramework.Tests
{
    /// <summary>Reads the mod assemblies (and what they reference) with Mono.Cecil, without loading them.</summary>
    internal sealed class AssemblyModel
    {
        private readonly DefaultAssemblyResolver resolver = new DefaultAssemblyResolver();
        private readonly Dictionary<string, TypeDefinition> types = new Dictionary<string, TypeDefinition>();

        public AssemblyModel(IEnumerable<string> assemblyPaths, IEnumerable<string> searchDirs)
        {
            foreach (string dir in searchDirs.Where(Directory.Exists))
            {
                resolver.AddSearchDirectory(dir);
            }
            foreach (string path in assemblyPaths)
            {
                AssemblyDefinition assembly = Read(path);
                Assemblies.Add(assembly);
                foreach (TypeDefinition type in assembly.MainModule.GetTypes())
                {
                    types[type.FullName] = type;
                }
            }
        }

        public List<AssemblyDefinition> Assemblies { get; } = new List<AssemblyDefinition>();

        public AssemblyDefinition Read(string path)
        {
            return AssemblyDefinition.ReadAssembly(path, new ReaderParameters { AssemblyResolver = resolver });
        }

        public TypeDefinition Find(string fullName) => types.TryGetValue(fullName, out TypeDefinition type) ? type : null;

        /// <summary>Field in the type or any base type (fields of game types are resolved through the game assemblies).</summary>
        public FieldDefinition FindField(TypeDefinition type, string name)
        {
            while (type != null)
            {
                FieldDefinition field = type.Fields.FirstOrDefault(f => f.Name == name && !f.IsStatic);
                if (field != null)
                {
                    return field;
                }
                try
                {
                    type = type.BaseType?.Resolve();
                }
                catch (AssemblyResolutionException)
                {
                    return null;
                }
            }
            return null;
        }

        public bool DerivesFrom(TypeDefinition type, string baseFullName)
        {
            while (type != null)
            {
                if (type.FullName == baseFullName)
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

        /// <summary>Every type and member reference of the assembly that does not resolve against the given search dirs.</summary>
        public static List<string> UnresolvedReferences(string assemblyPath, IEnumerable<string> searchDirs)
        {
            DefaultAssemblyResolver resolver = new DefaultAssemblyResolver();
            foreach (string dir in searchDirs.Where(Directory.Exists))
            {
                resolver.AddSearchDirectory(dir);
            }
            AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(assemblyPath, new ReaderParameters { AssemblyResolver = resolver });
            List<string> broken = new List<string>();
            foreach (TypeReference type in assembly.MainModule.GetTypeReferences())
            {
                try
                {
                    if (type.Resolve() == null)
                    {
                        broken.Add("type " + type.FullName);
                    }
                }
                catch (Exception e)
                {
                    broken.Add("type " + type.FullName + ": " + e.Message);
                }
            }
            foreach (MemberReference member in assembly.MainModule.GetMemberReferences())
            {
                try
                {
                    IMemberDefinition resolved = member switch
                    {
                        MethodReference method => method.Resolve(),
                        FieldReference field => field.Resolve(),
                        _ => null
                    };
                    if (resolved == null)
                    {
                        broken.Add("member " + member.FullName);
                    }
                }
                catch (Exception e)
                {
                    broken.Add("member " + member.FullName + ": " + e.Message);
                }
            }
            return broken;
        }
    }
}
