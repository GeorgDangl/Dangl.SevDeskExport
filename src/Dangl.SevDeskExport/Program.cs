using CommandLine;
using CommandLine.Text;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;

namespace Dangl.SevDeskExport
{
    class Program
    {
        [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ApiExportOptions))]
        private static async Task Main(string[] args)
        {
            HeadingInfo.Default.WriteMessage("Visit https://www.dangl-it.com to find out more about this exporter");
            HeadingInfo.Default.WriteMessage("This generator is available on GitHub: https://github.com/GeorgDangl/Dangl.SevDeskExport");
            HeadingInfo.Default.WriteMessage($"Version {VersionInfo.Version}");

            await Parser.Default.ParseArguments<ApiExportOptions>(args)
                .WithNotParsed<ApiExportOptions>(errors =>
                {
                    Console.WriteLine("Could not parse CLI arguments");
                })
                .WithParsedAsync<ApiExportOptions>(async options =>
                {
                    try
                    {
                        var exporter = new Exporter(options);
                        await exporter.ExportSevDeskDataAndWriteToDiskAsync().ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e.ToString());
                    }
                });
        }
    }
}
