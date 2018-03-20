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
            var arg = ArgumentParser.Parse(args);
            if(arg.ContainsKey("format"))
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
            if(!arg.ContainsKey("source"))
            {
                WriteUsage();
                return;
            }
            GeneratorSettings.ShowPrivateMembers = arg.ContainsKey("ShowPrivate");
            GeneratorSettings.ShowInternalMembers = arg.ContainsKey("ShowInternal");
            var source = arg["source"];
            var g = new Generator(generator);
            g.Process(source.Split('|')).Wait();
            Console.ReadKey();
        }

        private static void WriteUsage()
        {
            Console.WriteLine("\nUage parameters:");
            Console.WriteLine("dotnet GENERATOR.dll /source=[source folder] /format=[html|image] /ShowPrivate /ShowInternal");
            Console.WriteLine("  source        Specifies the folder of source files to include for the object model.\n                Separate with | for multiple folders");
            Console.WriteLine("  format        Format to generate: 'image' generates an image for each object.\n                'html' a single html output (html is default)");
            Console.WriteLine("  ShowPrivate   Show private members (default is false)");
            Console.WriteLine("  ShowInternal  Show internal members (default is false)");
        }
    }
}
