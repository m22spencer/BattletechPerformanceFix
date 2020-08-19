using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Mono.Cecil;
using Mono.Cecil.Rocks;
using Mono.Collections.Generic;

namespace RemoveAccessControl
{
    class Program
    {
        static void Main(string[] args)
        {
            var libsdir = args[0];
            var outlibs = args[1];
            if (!Directory.Exists(libsdir))
            {
                throw new Exception("create a libs folder and copy battletech Managed assemblies to it, then rebuild this project");
            }
            else
            {
                Console.WriteLine("Making all names public");


                var assemblyFiles = Directory.GetFiles(libsdir, "*.dll", SearchOption.AllDirectories);

                var assemblyResolver = new DefaultAssemblyResolver();
                assemblyResolver.AddSearchDirectory(libsdir);

                var readerParameters = new ReaderParameters { AssemblyResolver = assemblyResolver, InMemory = true, ReadWrite = true, ReadingMode = ReadingMode.Deferred };

                var assemblies = assemblyFiles.Select(file =>
                {
                    Console.WriteLine("For: " + file);
                    var def = AssemblyDefinition.ReadAssembly(file, readerParameters);
                    var types = def.Modules.SelectMany(m => m.Types).ToList();
                    var methods = types.SelectMany(t => t.Methods).ToList();
                    var fields = types.SelectMany(t => t.Fields).ToList();

                    types.ForEach(t => t.IsPublic = t.IsNestedPublic = true);
                    methods.ForEach(m => m.IsPublic = true);
                    fields.ForEach(m => m.IsPublic = true);
                    return (file, def);
                }).ToList();

                assemblyResolver.Dispose();

                Directory.CreateDirectory(outlibs);

                Console.WriteLine("Saving");
                assemblies.ForEach(def => def.Item2.Write(Path.Combine(outlibs, Path.GetFileName(def.file))));

                Console.WriteLine("Complete");
            }
        }
    }
}
