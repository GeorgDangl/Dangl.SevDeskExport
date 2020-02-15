using CommandLine;
using CommandLine.Text;
using System;
using System.Threading.Tasks;

namespace Dangl.SevDeskExport
{
    class Program
    {
        static async Task Main(string[] args)
        {
            HeadingInfo.Default.WriteMessage("Visit https://www.dangl-it.com to find out more about this exporter");
            HeadingInfo.Default.WriteMessage("This generator is available on GitHub: https://github.com/GeorgDangl/Dangl.SevDeskExport");
            HeadingInfo.Default.WriteMessage($"Version {VersionInfo.Version}");
            await Parser.Default.ParseArguments<ApiExportOptions>(args)
                .MapResult(async options =>
                {
                    try
                    {
                        var exporter = new Exporter(options);
                        await exporter.ExportSevDeskDataAndWriteToDisk().ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e.ToString());
                    }
                },
                errors =>
                {
                    Console.WriteLine("Could not parse CLI arguments");
                    return Task.CompletedTask;
                }).ConfigureAwait(false);
        }
    }
}
