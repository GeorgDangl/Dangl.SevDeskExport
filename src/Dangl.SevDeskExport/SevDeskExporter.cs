using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;

namespace Dangl.SevDeskExport
{
    public class SevDeskExporter
    {
        private readonly string _apiToken;
        private readonly List<SevDeskDataApiEndpoint> _apiOptions;
        private readonly HttpClient _httpClient;
        private const string SEVDESK_API_BASE_URL = "https://my.sevdesk.de/api/v1";

        public SevDeskExporter(string apiToken,
            List<SevDeskDataApiEndpoint> apiOptions,
            HttpClient httpClient)
        {
            _apiToken = apiToken;
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
                var resourceUrl = $"{SEVDESK_API_BASE_URL}/{options.RelativeUrl}?token={_apiToken}";
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
                            var response = await _httpClient.GetStringAsync(urlWithOffset).ConfigureAwait(false);
                            var data = (JObject.Parse(response)["objects"] as JArray).Select(j => j as JObject).ToList();

                            if (!AddRangeIfNewIds(apiResponseValues, data))
                            {
                                hasMore = false;
                            }
                        }
                        catch
                        {
                            Console.WriteLine($"Error while accessing the following url:{urlWithOffset.Replace(_apiToken, "**TOKEN**", StringComparison.InvariantCultureIgnoreCase)}");
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
    }
}
