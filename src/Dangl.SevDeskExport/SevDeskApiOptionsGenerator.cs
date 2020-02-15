using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Dangl.SevDeskExport
{
    public static class SevDeskApiOptionsGenerator
    {
        public static async Task<List<SevDeskDataApiEndpoint>> GetSevDeskApiEndpointOptionsAsync()
        {
            var optionsFilePath = "SevDeskApiExportOptions.json";
            if (File.Exists(optionsFilePath))
            {
                // Use the dedicated file in case it's present
                using (var fs = File.OpenRead(optionsFilePath))
                {
                    return await ReadOptionsFromStreamAsync(fs).ConfigureAwait(false);
                }
            }

            using (var defaultOptionsStream = GetDefaultOptionsStream())
            {
                return await ReadOptionsFromStreamAsync(defaultOptionsStream).ConfigureAwait(false);
            }
        }

        private static async Task<List<SevDeskDataApiEndpoint>> ReadOptionsFromStreamAsync(Stream optionsStream)
        {
            using (var sr = new StreamReader(optionsStream))
            {
                var json = await sr.ReadToEndAsync().ConfigureAwait(false);
                var apiOptions = JsonConvert.DeserializeObject<List<SevDeskDataApiEndpoint>>(json);
                // They're loaded from Json, so we're checking there are no duplicate definitions
                var optionsHaveDuplicates = apiOptions.Count != apiOptions.Select(ao => ao.ModelName).Distinct().Count();
                if (optionsHaveDuplicates)
                {
                    throw new Exception("There are duplicate api options configured in Json with the same ModelName");
                }
                return apiOptions;
            }
        }

        private static Stream GetDefaultOptionsStream()
        {
            return typeof(SevDeskApiOptionsGenerator)
                .Assembly
                .GetManifestResourceStream("Dangl.SevDeskExport.SevDeskApiExportOptions.json");
        }
    }
}
