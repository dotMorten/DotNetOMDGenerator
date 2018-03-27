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
        public static string OutputLocation { get; set; }
    }

    class Program
    {

        static void Main(string[] args)
        {
            Console.WriteLine("*********************** Object Model Generator ***********************");

            ICodeGenerator generator = null;
            var arg = ArgumentParser.Parse(args);
            //if (arg.ContainsKey("format"))
            //{
            //    if (arg["format"] == "image")
            //        generator = new Generators.OMDGenerator();
            //    else if (arg["format"] == "html")
            //        generator = new Generators.HtmlOmdGenerator();
            //    else
            //    {
            //        Console.WriteLine("Invalid format parameter.");
            //        WriteUsage();
            //        return;
            //    }
            //}
            //else
            //{
                generator = new Generators.HtmlOmdGenerator();
            //}
            if (!arg.ContainsKey("source"))
            {
                WriteUsage();
                return;
            }

            GeneratorSettings.ShowPrivateMembers = arg.ContainsKey("ShowPrivate");
            GeneratorSettings.ShowInternalMembers = arg.ContainsKey("ShowInternal");
            GeneratorSettings.OutputLocation = arg.ContainsKey("output") ? arg["output"] : "./";
            List<Regex> filters = arg.ContainsKey("exclude") ? arg["exclude"].Split(';', StringSplitOptions.RemoveEmptyEntries).Select(f=>CreateFilter(f)).ToList() : new List<Regex>();
            if(arg.ContainsKey("regexfilter"))
                filters.Add(new Regex(arg["regexfilter"]));
            string[] source = arg["source"].Split(';', StringSplitOptions.RemoveEmptyEntries);
            string[] oldSource = arg.ContainsKey("compareSource") ? arg["compareSource"].Split(';', StringSplitOptions.RemoveEmptyEntries) : null;
            string[] preprocessors = arg.ContainsKey("preprocessors") ? arg["preprocessors"].Split(';', StringSplitOptions.RemoveEmptyEntries) : null;
            string[] paths = { @"dotnet-api\src\Esri.ArcGISRuntime\Esri.ArcGISRuntime.Shared",
                @"dotnet-api\src\Esri.ArcGISRuntime\Esri.ArcGISRuntime.WindowsDesktop",
                @"dotnet-api\src\Esri.ArcGISRuntime.Hydrography\Esri.ArcGISRuntime.Hydrography.Shared",
                @"dotnet-api\src\Esri.ArcGISRuntime.Hydrography\Esri.ArcGISRuntime.Hydrography.WindowsDesktop",
                @"dotnet-api\src\Esri.ArcGISRuntime.Preview\Esri.ArcGISRuntime.Preview.Shared",
                @"dotnet-api\src\Esri.ArcGISRuntime.Preview\Esri.ArcGISRuntime.Preview.WindowsDesktop",
                @"dotnet-api\src\Esri.ArcGISRuntime\Esri.ArcGISRuntime.Xamarin.Forms.Shared",
                @"api_generated_interop\managed_wrappers" };
            //oldSource = paths.Select(p => @"c:\apps100.2\dotnet\" + p).ToArray();
            //source = paths.Select(p => @"c:\apps\dotnet\" + p).ToArray();
            //oldSource = null;
            //source = new[] { @"c:\GitHub\Microsoft\UWPCommunityToolkit"};
            // oldSource = new[] { @"c:\GitHub\Microsoft\UWPCommunityToolkit2" };
            // source = new[] { @"c:\temp\corefx\src" };
            // oldSource = new[] { @"c:\temp\corefx2.0\src" };
            // source = new[] { "https://github.com/dotMorten/NmeaParser/archive/master.zip" };
            // oldSource = new[] { "https://github.com/dotMorten/NmeaParser/archive/591c532920ef52eaa965ad0d1ee4565cac396914.zip" };
            //oldSource = new[] { "https://devtopia.esri.com/runtime/dotnet-api/archive/release/100.2.1.zip" };
            var g = new Generator(generator);
            if (oldSource != null)
                g.ProcessDiffs(oldSource, source, preprocessors, filters.ToArray()).Wait();
            else
                g.Process(source, preprocessors, filters.ToArray()).Wait();

            if(System.Diagnostics.Debugger.IsAttached)
                Console.ReadKey();
        }

        private static System.Text.RegularExpressions.Regex CreateFilter(string pattern, bool caseSensitive = false)
        {
            return new Regex("^" + Regex.Escape(pattern).
             Replace("\\*", ".*").
             Replace("\\?", ".") + "$", caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase);

            pattern = pattern.Replace(".", @"\.");
            pattern = pattern.Replace("?", ".");
            pattern = pattern.Replace("*", ".*?");
            pattern = pattern.Replace(@"\", @"\\");
            pattern = pattern.Replace(" ", @"\s");
            return new Regex(pattern, caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase);
        }


        private static void WriteUsage()
        {
            Console.WriteLine("\nUsage:");
            Console.WriteLine(" dotnet GENERATOR.dll /source=[source folder] /compareSource=[oldSourceFolder] /preprocessors=[defines] /output=[out location] /filter=[regex] /ShowPrivate /ShowInternal");
            Console.WriteLine("\nRequired parameters:");
            Console.WriteLine("  source        Specifies the folder of source files to include for the object model.\n                Separate with ; for multiple folders");
            Console.WriteLine("\nOptional parameters:");
            Console.WriteLine("  compareSource Specifies a folder to compare source and generate a diff model\n                This can be useful for finding API changes or compare branches");
            Console.WriteLine("  output        Output location");
            // Console.WriteLine("  format        Format to generate: 'image' generates an image for each object.\n                'html' a single html output (html is default)");
            Console.WriteLine("  preprocessors Define a set of preprocessors values. Use ; to separate multiple");
            Console.WriteLine("  exclude       Defines one or more strings that can't be part of the path Ie '/Samples/;/UnitTests/'\n                (use forward slash for folder separators)");
            Console.WriteLine("  regexfilter   Defines a regular expression for filtering on full file names in the source");
            Console.WriteLine("  ShowPrivate   Show private members (default is false)");
            Console.WriteLine("  ShowInternal  Show internal members (default is false)");
        }
    }
}
