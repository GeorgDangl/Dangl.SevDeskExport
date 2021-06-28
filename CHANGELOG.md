# Changelog

All notable changes to **Dangl.SevDeskExport** are documented here.

## v1.4.3:
- Fixed a bug that would not export all vouchers, when the sevDesk API returned the `recurringStartDate` for non-recurring vouchers

## v1.4.1:
- Fixed the invoice download feature to also export invoices that were sent directly via sevDesk. They don't have their `sendDate` specified

## v1.4.0:
- Changed to logic when deciding which vouchers and invoices to download. Now, only elements that were created in the specific month are being downloaded

## v1.3.0:
- The tool now also exports credit notes (Stornorechnungen), even if they were not sent

## v1.1.0:
- The tool now also exports vouchers and invoices for a given month

## v1.0.0:
- Initial release
