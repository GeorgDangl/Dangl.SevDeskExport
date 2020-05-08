using CommandLine;
using System;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Dangl.SevDeskExport
{
    public class ApiExportOptions
    {
        private string _voucherExportDate;

        [Option('t', "token", Required = true, HelpText = "Your sevDesk API Token")]
        public string SevDeskApiToken { get; set; }

        [Option('f', "folder", Required = false, HelpText = "Optional base path under which to place the data export")]
        public string ExportBaseFolder { get; set; }

        [Option('d', "date", Required = true, HelpText = "Date for which month to download documents, must be in format MM/yyyy, e.g. 05/2020")]
        public string DocumentExportDate
        {
            get => _voucherExportDate;
            set
            {
                var dateRegex = @"^(0[1-9]|1[012])[\/]\d{4}$";
                if (!Regex.IsMatch(value, dateRegex))
                {
                    throw new TargetInvocationException("The format to specifiy the date for which documents are being exported must be MM/yyyy, e.g. 05/2020", null);
                }
                _voucherExportDate = value;
            }
        }

        internal int DocumentExportMonth => Convert.ToInt32(_voucherExportDate.Substring(0, 2), CultureInfo.InvariantCulture);
        internal int DocumentExportYear => Convert.ToInt32(_voucherExportDate.Substring(3), CultureInfo.InvariantCulture);
    }
}
