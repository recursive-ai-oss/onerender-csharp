# Paged output compared with WeasyPrint (information)

WeasyPrint 70.0 was run as a command-line program on `index.html` of each template, with its own default fonts and page rules; onerender used the same HTML with the packaged fonts, through `HtmlPdf.ToPdf`. The two do not share fonts, so counts differ where text wraps differently. Nothing here gates.

| Document | onerender pages | WeasyPrint pages |
|---|---|---|
| invoice | 2 | 3 |
| report | 47 | 53 |
| statement | 6 | 9 |
| receipt | 1 | 1 |
| certificate | 1 | 1 |
| email | 1 | 1 |
| paged | 9 | 9 |

## Paged sample: first body line of each page

| Page | onerender | WeasyPrint |
|---|---|---|
| 1 | Running headers, a contents list with leaders and a table wh | Running headers, a contents list with leaders and a table wh |
| 2 | Schedule General Ledger | Contract freight term report charge carrier receipt referenc |
| 3 | License Invoice Supply | Forecast balance summary reference balance reference documen |
| 4 | Region Shipment Term | Ledger quarter payment margin warranty shipment rate term ba |
| 5 | 12 transfer client account 361.31 | 12 transfer client account 361.31 |
| 6 | 44 transfer revenue client 2,916.51 | 44 transfer revenue client 2,916.51 |
| 7 | 76 margin general shipment 4,647.78 | 76 margin general shipment 4,647.78 |
| 8 | Balance Schedule Warranty | Payment  transfer  total  charge  order  estimate  project   |
| 9 | Vendor Supply Policy | Document income service forecast schedule project period ord |
