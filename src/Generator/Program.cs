using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Generator
{
    internal static class GeneratorSettings
    {
        public static bool ShowPrivateMembers { get; set; } = false;
        public static bool ShowInternalMembers { get; set; } = false;
    }

    class Program
    {

        static void Main(string[] args)
        {
            Console.WriteLine("*********************** Object Model Generator ***********************");

            ICodeGenerator generator = null;
            // TODO: Try using Microsoft.CodeAnalysis.CommandLineParser
            var arg = ArgumentParser.Parse(args);
            if (arg.ContainsKey("format"))
            {
                if (arg["format"] == "image")
                    generator = new Generators.OMDGenerator();
                else if (arg["format"] == "html")
                    generator = new Generators.HtmlOmdGenerator();
                else
                {
                    Console.WriteLine("Invalid format parameter.");
                    WriteUsage();
                    return;
                }
            }
            else
            {
                generator = new Generators.HtmlOmdGenerator();
            }
            if (!arg.ContainsKey("source"))
            {
                WriteUsage();
                return;
            }
            GeneratorSettings.ShowPrivateMembers = arg.ContainsKey("ShowPrivate");
            GeneratorSettings.ShowInternalMembers = arg.ContainsKey("ShowInternal");
            string[] filters = arg.ContainsKey("filters") ? arg["filters"].Split(';', StringSplitOptions.RemoveEmptyEntries) : null;
            System.Text.RegularExpressions.Regex regexfilter = arg.ContainsKey("regexfilter") ? new System.Text.RegularExpressions.Regex(arg["filter"]) : null;
            string[] source = arg["source"].Split(';', StringSplitOptions.RemoveEmptyEntries);
            string[] oldSource = arg.ContainsKey("compareSource") ? arg["compareSource"].Split(';', StringSplitOptions.RemoveEmptyEntries) : null;
            string[] preprocessors = arg.ContainsKey("preprocessors") ? arg["preprocessors"].Split(';', StringSplitOptions.RemoveEmptyEntries) : null;
            var g = new Generator(generator);
            if (oldSource != null)
                g.ProcessDiffs(oldSource, source, preprocessors, regexfilter, filters).Wait();
            else
                g.Process(source, preprocessors, regexfilter, filters).Wait();

            if(System.Diagnostics.Debugger.IsAttached)
            Console.ReadKey();
        }

        private static void WriteUsage()
        {
            Console.WriteLine("\nUsage:");
            Console.WriteLine(" dotnet GENERATOR.dll /source=[source folder] /compareSource=[oldSourceFolder] /preprocessors=[defines] /format=[html|image] /filter=[regex] /ShowPrivate /ShowInternal");
            Console.WriteLine("\nRequired parameters:");
            Console.WriteLine("  source        Specifies the folder of source files to include for the object model.\n                Separate with ; for multiple folders");
            Console.WriteLine("\nOptional parameters:");
            Console.WriteLine("  compareSource Specifies a folder to compare source and generate a diff model\n                This can be useful for finding API changes or compare branches");
            Console.WriteLine("  format        Format to generate: 'image' generates an image for each object.\n                'html' a single html output (html is default)");
            Console.WriteLine("  preprocessors Define a set of preprocessors values. Use ; to separate multiple");
            Console.WriteLine("  filters       Defines one or more strings that can't be part of the path Ie '/Samples/;/UnitTests/'\n                (use forward slash for folder separators)");
            Console.WriteLine("  regexfilter   Defines a regular expression for filtering on full file names in the source");
            Console.WriteLine("  ShowPrivate   Show private members (default is false)");
            Console.WriteLine("  ShowInternal  Show internal members (default is false)");
        }
    }
}
