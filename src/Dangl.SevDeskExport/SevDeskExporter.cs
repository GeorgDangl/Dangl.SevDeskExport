using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace Dangl.SevDeskExport
{
    public class SevDeskExporter
    {
        private readonly List<SevDeskDataApiEndpoint> _apiOptions;
        private readonly HttpClient _httpClient;
        private const string SEVDESK_API_BASE_URL = "https://my.sevdesk.de/api/v1";

        public SevDeskExporter(List<SevDeskDataApiEndpoint> apiOptions,
            HttpClient httpClient)
        {
            _apiOptions = apiOptions;
            _httpClient = httpClient;
        }

        public async IAsyncEnumerable<(string modelName, List<JObject> values)> EnumerateDataAsync()
        {
            var previousValues = new Dictionary<string, List<JObject>>();
            foreach (var options in _apiOptions)
            {
                var startTime = DateTime.UtcNow;
                Console.WriteLine($"Downloading {options.ModelName}...");
                var resourceUrl = $"{SEVDESK_API_BASE_URL}/{options.RelativeUrl}";
                if (options.AdditionalParameters != null)
                {
                    foreach (var param in options.AdditionalParameters)
                    {
                        resourceUrl += $"&{Uri.EscapeDataString(param.Key)}={Uri.EscapeDataString(param.Value)}";
                    }
                }

                var apiResponseValues = new List<JObject>();

                var urls = options.PathReplacement == null
                    ? new List<string> { resourceUrl }
                    : previousValues[options.PathReplacement.BaseModel]
                        .Select(bm => resourceUrl.Replace("{" + options.PathReplacement.UrlParameterName + "}", bm[options.PathReplacement.ObjectProperty].ToString(), StringComparison.OrdinalIgnoreCase))
                        .ToList();

                if (urls.Count > 0)
                {
                    Console.WriteLine($"Downloading items per resource for {urls.Count} total endpoints.");
                }

                foreach (var url in urls)
                {
                    var hasMore = true;
                    var lastOffset = 0;
                    const int limit = 100;
                    while (hasMore)
                    {
                        var urlWithOffset = $"{url}&offset={lastOffset}&limit={limit}";
                        lastOffset += limit;
                        try
                        {
                            var response = await _httpClient.GetStringAsync(GetUrlWithCorrectQueryStringFormat(urlWithOffset)).ConfigureAwait(false);
                            var data = (JObject.Parse(response)["objects"] as JArray).Select(j => j as JObject).ToList();

                            if (!AddRangeIfNewIds(apiResponseValues, data))
                            {
                                hasMore = false;
                            }
                        }
                        catch
                        {
                            Console.WriteLine($"Error while accessing the following url:{urlWithOffset}");
                            throw;
                        }
                    }
                }

                var duration = (DateTime.UtcNow - startTime).TotalSeconds;
                Console.WriteLine($"Downloaded {apiResponseValues.Count} items in {duration:#} seconds.");

                previousValues.Add(options.ModelName, apiResponseValues);
                yield return (options.ModelName, apiResponseValues);
            }
        }

        private static bool AddRangeIfNewIds(List<JObject> existingValues, List<JObject> possiblyNewValues)
        {
            var hasAddedNew = false;
            foreach (var entry in possiblyNewValues)
            {
                if (existingValues.Any(existing => existing["id"].ToString() == entry["id"].ToString()))
                {
                    continue;
                }

                hasAddedNew = true;
                existingValues.Add(entry);
            }

            if (!hasAddedNew)
            {
                return false;
            }

            return true;
        }

        public async Task<(Stream file, string fileName)> DownloadDocumentAsync(string documentId)
        {
            var downloadUrl = $"{SEVDESK_API_BASE_URL}/Document/{documentId}/download";
            var httpResponse = await _httpClient.GetAsync(downloadUrl).ConfigureAwait(false);
            if (!httpResponse.IsSuccessStatusCode)
            {
                Console.WriteLine("Error downloading document with id: " + documentId);
                throw new Exception("Error downloading document with id: " + documentId);
            }

            var responseString = await httpResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
            var responseJson = JObject.Parse(responseString);
            var base64Content = responseJson["objects"]["content"].ToString();
            var originalFileName = responseJson["objects"]["filename"].ToString();
            var binary = Convert.FromBase64String(base64Content);
            return (new MemoryStream(binary), $"{documentId}_{originalFileName}");
        }

        public async Task<(Stream file, string fileName)> DownloadInvoiceAsync(string invoiceId)
        {
            var downloadUrl = $"{SEVDESK_API_BASE_URL}/Invoice/{invoiceId}/getPdf?preventSendBy=true&download=true";
            var httpResponse = await _httpClient.GetAsync(downloadUrl).ConfigureAwait(false);
            if (!httpResponse.IsSuccessStatusCode)
            {
                Console.WriteLine("Error downloading invoice with id: " + invoiceId);
                throw new Exception("Error downloading invoice with id: " + invoiceId);
            }

            var responseContent = await httpResponse.Content.ReadAsStreamAsync().ConfigureAwait(false);
            var originalFileName = httpResponse.Content.Headers.ContentDisposition.FileName.Trim('"');
            
            return (responseContent, $"{invoiceId}_{originalFileName}");
        }

        public async Task<(Stream file, string fileName)> DownloadInvoiceAsPdfAsync(string invoiceId)
        {
            var downloadUrl = $"{SEVDESK_API_BASE_URL}/Invoice/{invoiceId}/getPdf";
            var httpResponse = await _httpClient.GetAsync(downloadUrl).ConfigureAwait(false);
            if (!httpResponse.IsSuccessStatusCode)
            {
                Console.WriteLine("Error downloading invoice with id: " + invoiceId);
                throw new Exception("Error downloading invoice with id: " + invoiceId);
            }

            var responseString = await httpResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
            var responseJson = JObject.Parse(responseString);
            var base64Content = responseJson["objects"]["content"].ToString();
            var originalFileName = responseJson["objects"]["filename"].ToString();
            var binary = Convert.FromBase64String(base64Content);
            return (new MemoryStream(binary), $"{invoiceId}_{originalFileName}");
        }

        private static string GetUrlWithCorrectQueryStringFormat(string url)
        {
            if (url.Contains("&") && !url.Contains("?"))
            {
                return url.Substring(0, url.IndexOf('&')) + "?" + url.Substring(url.IndexOf('&') + 1);
            }

            return url;
        }
    }
}
