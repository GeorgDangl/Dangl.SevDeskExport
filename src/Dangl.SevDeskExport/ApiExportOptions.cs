using CommandLine;

namespace Dangl.SevDeskExport
{
    public class ApiExportOptions
    {
        [Option('t', "token", Required = true, HelpText = "Your sevDesk API Token")]
        public string SevDeskApiToken { get; set; }

        [Option('f', "folder", Required = false, HelpText = "Optional base path under which to place the data export")]
        public string ExportBaseFolder { get; set; }
    }
}
