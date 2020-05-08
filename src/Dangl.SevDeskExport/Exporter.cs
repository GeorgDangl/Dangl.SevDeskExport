using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace Dangl.SevDeskExport
{
    public class Exporter
    {
        private readonly ApiExportOptions _apiExportOptions;
        private readonly Dictionary<string, List<JObject>> _sevDeskDataByModelName = new Dictionary<string, List<JObject>>();
        private string _basePath;
        private string _documentsBasePath;
        private HttpClient _httpClient;
        private DateTimeOffset _startDate;
        private SevDeskExporter _sevDeskExporter;

        public Exporter(ApiExportOptions apiExportOptions)
        {
            _apiExportOptions = apiExportOptions;
            SetExportPaths();
        }

        public async Task ExportSevDeskDataAndWriteToDiskAsync()
        {
            var options = await SevDeskApiOptionsGenerator.GetSevDeskApiEndpointOptionsAsync().ConfigureAwait(false);
            _httpClient = GetHttpClient();
            _sevDeskExporter = new SevDeskExporter(_apiExportOptions.SevDeskApiToken, options, _httpClient);

            var apiOptionsPath = Path.Combine(_basePath, "ApiExportOptions.json");
            using (var fs = File.CreateText(apiOptionsPath))
            {
                var apiOptionsJson = JsonConvert.SerializeObject(options, Formatting.Indented);
                await fs.WriteAsync(apiOptionsJson).ConfigureAwait(false);
            }

            await foreach (var apiResult in _sevDeskExporter.EnumerateDataAsync())
            {
                _sevDeskDataByModelName.Add(apiResult.modelName, apiResult.values);
                var jsonFilePath = Path.Combine(_basePath, apiResult.modelName + ".json");
                using (var fs = File.CreateText(jsonFilePath))
                {
                    var jsonResultContainer = new JObject();
                    jsonResultContainer["objects"] = new JArray(apiResult.values);

                    var jsonResult = jsonResultContainer.ToString(Formatting.Indented);
                    await fs.WriteAsync(jsonResult).ConfigureAwait(false);
                }
            }

            await ExportVouchersAsync().ConfigureAwait(false);
        }

        private void SetExportPaths()
        {
            _basePath = string.IsNullOrWhiteSpace(_apiExportOptions.ExportBaseFolder)
                ? string.Empty
                : _apiExportOptions.ExportBaseFolder;
            _basePath = Path.Combine(_basePath, $"{DateTime.Now:yyyy-MM-dd HH-mm}");
            if (!Directory.Exists(_basePath))
            {
                Directory.CreateDirectory(_basePath);
            }

            _documentsBasePath = Path.Combine(_basePath, "Dokumente");
            if (!Directory.Exists(_documentsBasePath))
            {
                Directory.CreateDirectory(_documentsBasePath);
            }
        }

        private async Task ExportVouchersAsync()
        {
            // Sevdesk is internally using a timezone offset, so documents for e.g. May 1st are actually stored as
            // having a +02:00 offset. It's not clear what timezone they use exactly, but previous data suggests
            // it's likely CET with daylight savings time.
            var cet = TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time");
            var utcOffset = cet.GetUtcOffset(new DateTimeOffset(_apiExportOptions.DocumentExportYear,
                _apiExportOptions.DocumentExportMonth, 1, 0, 0, 0, TimeSpan.Zero));
            _startDate = new DateTimeOffset(_apiExportOptions.DocumentExportYear,
                _apiExportOptions.DocumentExportMonth, 1, 0, 0, 0, utcOffset);
            var endDate = _startDate.AddMonths(1);

            Console.WriteLine("Downloading invoices...");
            await DownloadInvoicesAsync().ConfigureAwait(false);
            Console.Write("Downloading vouchers...");
            await DownloadVouchersAsync().ConfigureAwait(false);
        }

        private async Task DownloadInvoicesAsync()
        {
            foreach (var invoice in _sevDeskDataByModelName["Invoice"])
            {
                var invoiceDateString = invoice["invoiceDate"].ToString();
                var invoiceDate = DateTimeOffset.Parse(invoiceDateString, null);
                if (invoiceDate < _startDate || invoiceDate > _startDate.AddMonths(1))
                {
                    continue;
                }
                if (invoice["sendDate"] == null
                    || invoice["sendDate"].Type == JTokenType.Null)
                {
                    // Not sent invoices don't have documents attached
                    continue;
                }

                var document = _sevDeskDataByModelName["Document"]
                    // It should never be null for an invoice
                    .First(d => d["baseObject"] != null && d["baseObject"]["id"].ToString() == invoice["id"].ToString());
                var documentId = document["id"].ToString();
                var contactName = GetContactName(invoice, "contact");
                var fileName = $"Rechnung {invoiceDate:yyyyMMdd} {invoice["invoiceNumber"]} - {contactName}";
                await DownloadDocumentAndSaveFileAsync(documentId, fileName).ConfigureAwait(false);
            }
        }

        private async Task DownloadVouchersAsync()
        {
            foreach (var voucher in _sevDeskDataByModelName["Voucher"].Where(v => v["voucherDate"] != null))
            {
                var voucherDate = DateTimeOffset.Parse(voucher["voucherDate"].ToString(), null);
                if (voucherDate < _startDate || voucherDate > _startDate.AddMonths(1))
                {
                    continue;
                }

                var documentId = voucher["document"]?["id"]?.ToString();
                if (documentId == null)
                {
                    continue;
                }

                var supplierName = GetContactName(voucher, "supplier");

                var fileName = $"Beleg {voucherDate:yyyyMMdd} {voucher["invoiceNumber"]}" +
                    (string.IsNullOrWhiteSpace(supplierName) ? string.Empty : $" - {supplierName}");
                await DownloadDocumentAndSaveFileAsync(documentId, fileName).ConfigureAwait(false);
            }
        }

        private string GetContactName(JObject baseObject, string typeIdentifier)
        {
            var contactName = string.Empty;
            if (baseObject[typeIdentifier] != null)
            {
                var contact = _sevDeskDataByModelName["Contact"]
                    .First(c => c["id"].ToString() == baseObject[typeIdentifier]["id"].ToString());
                if (contact != null)
                {
                    contactName = contact["name"].ToString();
                }
                if (string.IsNullOrWhiteSpace(contactName))
                {
                    contactName = (contact["surename"]?.ToString() + " " + contact["familyname"]?.ToString()).Trim();
                }
            }

            return contactName;
        }

        private async Task DownloadDocumentAndSaveFileAsync(string documentId, string fileName)
        {
            var documentDownload = await _sevDeskExporter.DownloadDocumentAsync(documentId).ConfigureAwait(false);
            var exportPath = Path.Combine(_documentsBasePath, $"{fileName} - {documentDownload.fileName}".Replace('/', '_').Replace('\\', '_'));
            using (var fs = File.Create(exportPath))
            {
                await documentDownload.file.CopyToAsync(fs).ConfigureAwait(false);
            }
            documentDownload.file.Dispose();
        }

        private static HttpClient GetHttpClient()
        {
            var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("User-Agent", "Dangl IT GmbH sevDesk Export www.dangl-it.com");
            return httpClient;
        }
    }
}
