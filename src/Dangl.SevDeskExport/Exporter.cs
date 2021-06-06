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

            Console.WriteLine("Downloading invoices...");
            await DownloadInvoicesAsync().ConfigureAwait(false);
            Console.WriteLine("Downloading vouchers...");
            await DownloadVouchersAsync().ConfigureAwait(false);
            Console.WriteLine("Downloading credit notes...");
            await DownloadCreditNotesAsync().ConfigureAwait(false);
        }

        private async Task DownloadInvoicesAsync()
        {
            foreach (var invoice in _sevDeskDataByModelName["Invoice"])
            {
                if (!CheckIfElementIsInDateRange(invoice))
                {
                    continue;
                }

                if ((invoice["sendDate"] == null
                    || invoice["sendDate"].Type == JTokenType.Null)
                    && (invoice["accountIntervall"] == null || invoice["accountIntervall"].Type == JTokenType.Null))
                {
                    // Not sent invoices don't have documents attached
                    // but interval invoices should still be included, since sevDesk
                    // doesn't seem to set the `sendDate` property for automatically sent invoices
                    continue;
                }

                var invoiceDateString = invoice["invoiceDate"].ToString();
                var invoiceDate = DateTimeOffset.Parse(invoiceDateString, null);

                var document = _sevDeskDataByModelName["Document"]
                    // It should never be null for an invoice
                    .FirstOrDefault(d => d["baseObject"] != null && d["baseObject"]["id"].ToString() == invoice["id"].ToString());
                if (document != null)
                {
                    var documentId = document["id"].ToString();
                    var contactName = GetContactName(invoice, "contact");
                    var fileName = $"Rechnung {invoiceDate:yyyyMMdd} {invoice["invoiceNumber"]} - {contactName}";
                    await DownloadDocumentAndSaveFileAsync(documentId, fileName).ConfigureAwait(false);
                }
            }
        }

        private async Task DownloadVouchersAsync()
        {
            foreach (var voucher in _sevDeskDataByModelName["Voucher"].Where(v => v["voucherDate"] != null))
            {
                if (!CheckIfElementIsInDateRange(voucher))
                {
                    continue;
                }

                var documentId = voucher["document"]?["id"]?.ToString();
                if (documentId == null)
                {
                    continue;
                }

                var recurringDateString = voucher["recurringStartDate"]?.ToString();
                if (!string.IsNullOrWhiteSpace(recurringDateString) && !CheckIfDateStringIsInRange(recurringDateString))
                {
                    // Recurring vouchers are only exported once, for the month in which they were created
                    continue;
                }

                var voucherDateString = voucher["voucherDate"].ToString();
                var voucherDate = DateTimeOffset.Parse(voucherDateString, null);

                var supplierName = GetContactName(voucher, "supplier");

                var fileName = $"Beleg {voucherDate:yyyyMMdd} {voucher["invoiceNumber"]}" +
                    (string.IsNullOrWhiteSpace(supplierName) ? string.Empty : $" - {supplierName}");
                await DownloadDocumentAndSaveFileAsync(documentId, fileName).ConfigureAwait(false);
            }
        }

        // Credit note = Stornorechnung in German
        private async Task DownloadCreditNotesAsync()
        {
            foreach (var invoice in _sevDeskDataByModelName["Invoice"])
            {
                if (!CheckIfElementIsInDateRange(invoice))
                {
                    continue;
                }

                var invoiceDateString = invoice["invoiceDate"].ToString();
                var invoiceDate = DateTimeOffset.Parse(invoiceDateString, null);

                if (invoice["invoiceType"] == null
                    || invoice["invoiceType"].ToString() != "SR")
                {
                    continue;
                }

                var contactName = GetContactName(invoice, "contact");
                var fileName = $"Stornorechnung {invoiceDate:yyyyMMdd} {invoice["invoiceNumber"]} - {contactName}";
                await DownloadInvoiceAsPdfByIdAndSaveFileAsync(invoice["id"].ToString(), fileName).ConfigureAwait(false);
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

        private async Task DownloadInvoiceAsPdfByIdAndSaveFileAsync(string invoiceId, string fileName)
        {
            var invoiceDownload = await _sevDeskExporter.DownloadInvoiceAsPdfAsync(invoiceId).ConfigureAwait(false);
            var exportPath = Path.Combine(_documentsBasePath, $"{fileName} - {invoiceDownload.fileName}".Replace('/', '_').Replace('\\', '_'));
            using (var fs = File.Create(exportPath))
            {
                await invoiceDownload.file.CopyToAsync(fs).ConfigureAwait(false);
            }
            invoiceDownload.file.Dispose();
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

        private bool CheckIfElementIsInDateRange(JObject element)
        {
            // There are other properties storing the date in sevDesk:
            // 'payDate', when a voucher was payed
            // 'update' when an element was modified
            // 'invoiceDate'
            // 'voucherDate'

            if (CheckIfDateStringIsInRange(element["create"].ToString()))
            {
                return true;
            }

            return false;
        }

        private bool CheckIfDateStringIsInRange(string date)
        {
            var parsedDate = DateTimeOffset.Parse(date, null);
            if (parsedDate >= _startDate && parsedDate <= _startDate.AddMonths(1))
            {
                return true;
            }

            return false;
        }
    }
}
