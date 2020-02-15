using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace Dangl.SevDeskExport
{
    public class Exporter
    {
        private readonly ApiExportOptions _apiExportOptions;

        public Exporter(ApiExportOptions apiExportOptions)
        {
            _apiExportOptions = apiExportOptions;
        }

        public async Task ExportSevDeskDataAndWriteToDisk()
        {
            var options = await SevDeskApiOptionsGenerator.GetSevDeskApiEndpointOptionsAsync().ConfigureAwait(false);
            var httpClient = GetHttpClient();
            var exporter = new SevDeskExporter(_apiExportOptions.SevDeskApiToken, options, httpClient);

            //var result = await exporter.GetAllDataAsync().ConfigureAwait(false);

            var basePath = string.IsNullOrWhiteSpace(_apiExportOptions.ExportBaseFolder)
                ? string.Empty
                : _apiExportOptions.ExportBaseFolder;
            basePath = Path.Combine(basePath, $"{DateTime.Now:yyyy-MM-dd HH-mm}");
            if (!Directory.Exists(basePath))
            {
                Directory.CreateDirectory(basePath);
            }

            var apiOptionsPath = Path.Combine(basePath, "ApiExportOptions.json");
            using (var fs = File.CreateText(apiOptionsPath))
            {
                var apiOptionsJson = JsonConvert.SerializeObject(options, Formatting.Indented);
                await fs.WriteAsync(apiOptionsJson).ConfigureAwait(false);
            }

            await foreach (var apiResult in exporter.EnumerateDataAsync())
            {
                var jsonFilePath = Path.Combine(basePath, apiResult.modelName + ".json");
                using (var fs = File.CreateText(jsonFilePath))
                {
                    var jsonResultContainer = new JObject();
                    jsonResultContainer["objects"] = new JArray(apiResult.values);

                    var jsonResult = jsonResultContainer.ToString(Formatting.Indented);
                    await fs.WriteAsync(jsonResult).ConfigureAwait(false);
                }
            }
        }

        private static HttpClient GetHttpClient()
        {
            var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("User-Agent", "Dangl IT GmbH sevDesk Export www.dangl-it.com");
            return httpClient;
        }
    }
}
