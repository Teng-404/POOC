using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace POOC.Helpers
{
    /// <summary>
    /// Helper กลางสำหรับ PDF ทุกประเภทใน POOC
    /// ควบคุม Font, Footer (เลขหน้า + วันที่พิมพ์) และ style พื้นฐาน
    /// </summary>
    public static class PdfDocumentBase
    {
        // ─── ค่าคงที่มาตรฐาน ───────────────────────────────────────────
        public const string DefaultFont    = "TH Sarabun New";
        public const float  DefaultFontSize = 11f;
        public const float  MarginCm       = 1.5f;
        public const float  MarginLandCm   = 1.2f;

        private static readonly System.Globalization.CultureInfo ThaiCulture =
            new("th-TH");

        // ─── Footer มาตรฐาน: "หน้า X / Y  |  พิมพ์เมื่อ dd/MM/yyyy HH:mm" ──
        public static void ApplyStandardFooter(PageDescriptor page)
        {
            page.Footer()
                .PaddingTop(4)
                .BorderTop(0.5f)
                .BorderColor(Colors.Grey.Lighten2)
                .Row(row =>
                {
                    row.RelativeItem()
                       .AlignLeft()
                       .Text(t =>
                       {
                           t.Span("หน้า ").FontSize(9).FontColor(Colors.Grey.Darken1);
                           t.CurrentPageNumber().FontSize(9).FontColor(Colors.Grey.Darken1);
                           t.Span(" / ").FontSize(9).FontColor(Colors.Grey.Darken1);
                           t.TotalPages().FontSize(9).FontColor(Colors.Grey.Darken1);
                       });

                    row.RelativeItem()
                       .AlignRight()
                       .Text($"พิมพ์เมื่อ {DateTime.Now.ToString("dd/MM/yyyy HH:mm", ThaiCulture)}")
                       .FontSize(9)
                       .FontColor(Colors.Grey.Darken1);
                });
        }

        // ─── DefaultTextStyle มาตรฐาน ───────────────────────────────────
        public static TextStyle DefaultTextStyle =>
            TextStyle.Default
                     .FontFamily(DefaultFont)
                     .FontSize(DefaultFontSize);
    }
}
