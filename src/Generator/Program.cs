using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Generator
{
    internal static class GeneratorSettings
    {
        public static bool ShowPrivateMembers { get; set; } = false;
        public static bool ShowInternalMembers { get; set; } = false;
        public static string OutputLocation { get; set; } = "./";
    }

    class Program
    {

        static async Task Main(string[] args)
        {
            Console.WriteLine("*********************** Object Model Generator ***********************");

            var arg = ArgumentParser.Parse(args);
            List<ICodeGenerator> generators = new List<ICodeGenerator>();
            if (arg.ContainsKey("format"))
            {
                var formats = arg["format"].Split(';', StringSplitOptions.RemoveEmptyEntries);
                foreach (var format in formats)
                    if (format == "md")
                        generators.Add(new Generators.MarkdownGenerator());
                    else if (format == "html")
                        generators.Add(new Generators.HtmlOmdGenerator());
                    else
                    {
                        Console.WriteLine("Invalid format parameter.");
                        WriteUsage();
                        return;
                    }
            }
            if(!generators.Any())
            {
                generators.Add(new Generators.HtmlOmdGenerator());
            }
            if (!arg.ContainsKey("source") && !arg.ContainsKey("assemblies"))
            {
                WriteUsage();
                return;
            }

            GeneratorSettings.ShowPrivateMembers = arg.ContainsKey("showPrivate");
            GeneratorSettings.ShowInternalMembers = arg.ContainsKey("showInternal");
            if(arg.ContainsKey("output"))
                GeneratorSettings.OutputLocation = arg["output"];
            List<Regex> filters = arg.ContainsKey("exclude") ? arg["exclude"].Split(';', StringSplitOptions.RemoveEmptyEntries).Select(f=>CreateFilter(f)).ToList() : new List<Regex>();
            if(arg.ContainsKey("regexfilter"))
                filters.Add(new Regex(arg["regexfilter"]));
            string[] source = arg.ContainsKey("source") ? arg["source"].Split(';', StringSplitOptions.RemoveEmptyEntries) : new string[] { };
            string[] oldSource = arg.ContainsKey("compareSource") ? arg["compareSource"].Split(';', StringSplitOptions.RemoveEmptyEntries) : null;
            string[] preprocessors = arg.ContainsKey("preprocessors") ? arg["preprocessors"].Split(';', StringSplitOptions.RemoveEmptyEntries) : null;
            string[] assemblies = arg.ContainsKey("assemblies") ? arg["assemblies"].Split(';', StringSplitOptions.RemoveEmptyEntries) : new string[] { };
            string[] compareAssemblies = arg.ContainsKey("compareAssemblies") ? arg["compareAssemblies"].Split(';', StringSplitOptions.RemoveEmptyEntries) : null;
            string[] referenceAssemblies = arg.ContainsKey("referenceAssemblies") ? arg["referenceAssemblies"].Split(';', StringSplitOptions.RemoveEmptyEntries) : null;
            
            var g = new Generator(generators);

            //Set up output filename
            if (string.IsNullOrEmpty(GeneratorSettings.OutputLocation))
                GeneratorSettings.OutputLocation = "./";
            var fi = new System.IO.FileInfo(GeneratorSettings.OutputLocation);
            if (!fi.Directory.Exists)
                throw new System.IO.DirectoryNotFoundException(fi.Directory.FullName);
            if (fi.Attributes == System.IO.FileAttributes.Directory)
                GeneratorSettings.OutputLocation = System.IO.Path.Combine(GeneratorSettings.OutputLocation, "OMD");

            if (oldSource != null || compareAssemblies != null)
                await g.ProcessDiffs(oldSource, source, compareAssemblies, assemblies, preprocessors, filters.ToArray(), referenceAssemblies);
            else
                await g.Process(source, assemblies, preprocessors, filters.ToArray(), referenceAssemblies);

            if(System.Diagnostics.Debugger.IsAttached)
                Console.ReadKey();
        }

        private static System.Text.RegularExpressions.Regex CreateFilter(string pattern, bool caseSensitive = false)
        {
            return new Regex("^" + Regex.Escape(pattern).
             Replace("\\*", ".*").
             Replace("\\?", ".") + "$", caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase);
        }

        private static void WriteUsage()
        {
            Console.WriteLine("\nUsage:");
            Console.WriteLine(" --source=[source folder] --compareSource=[oldSourceFolder] --preprocessors=[defines] --output=[out location] --format=[html,md] --filter=[regex] --showPrivate --showInternal");
            Console.WriteLine("\nRequired parameters (one or more):");
            Console.WriteLine("  source               Specifies the folder of source files to include for the object model.\n                       Separate with ; for multiple folders");
            Console.WriteLine("  assemblies           Specifies a set of assemblies to include for the object model.\n                       Separate with ; for multiple assemblies");
            Console.WriteLine("\nOptional parameters:");
            Console.WriteLine("  compareSource        Specifies a folder to compare source and generate a diff model\n                       This can be useful for finding API changes or compare branches");
            Console.WriteLine("  compareAssemblies    Specifies a set of assemblies to include to generate a adiff model.\n                       Separate with ; for multiple assemblies");
            Console.WriteLine("  output        Output location");
            Console.WriteLine("  preprocessors        Define a set of preprocessors values. Use ; to separate multiple");
            Console.WriteLine("  exclude              Defines one or more strings that can't be part of the path Ie '/Samples/;/UnitTests/'\n                       (use forward slash for folder separators)");
            Console.WriteLine("  regexfilter          Defines a regular expression for filtering on full file names in the source");
            Console.WriteLine("  referenceAssemblies  Specifies a set of assemblies to include for references for better type resolution.");
            Console.WriteLine("  showPrivate          Show private members (default is false)");
            Console.WriteLine("  showInternal         Show internal members (default is false)");
        }
    }
}
