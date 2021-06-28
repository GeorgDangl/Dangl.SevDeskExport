# Dangl.SevDeskExport

[![Actions Status](https://github.com/GeorgDangl/Dangl.SevDeskExport/workflows/continuous/badge.svg)](https://github.com/GeorgDangl/Dangl.SevDeskExport/actions)  

This is a small CLI tool that exports all data from your sevDesk account. Documents for invoices and vouchers are also exported, but only for a single month. This is useful when you're doing monthly exports for handover to your accountant.
It will not export recurring inbound vouchers. Exports will also download credit notes (_Stornorechnungen_ in German).

## CLI Usage

Simply execute the converter, e.g. on Windows:

    Dangl.SevDeskExport.exe -t <ApiToken> -d <Date> -f <OutputFolder>

The data will be exported to a new folder generated in the `OutputFolder`. If no folder is specified, it's placed relative to the executable. The `Date` must be in format `MM/yyyy`, e.g. `05/2020` and will be used to export documents only for this month.

## Downloads & Documentation

[You can download the binaries directly from Dangl**Docu**](https://docs.dangl-it.com/Projects/Dangl.SevDeskExport).

## Configuration

The list of resources to export from your sevDesk account are defined in a file called `SevDeskApiExportOptions.json`, which must be placed next to the executable. If this file
is not found, then default values will be used that should export all the data from your account. Please see the [GitHub repository](https://github.com/GeorgDangl/Dangl.SevDeskExport) for an example of this file.

