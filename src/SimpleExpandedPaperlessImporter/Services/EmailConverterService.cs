using MimeKit;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SimpleExpandedPaperlessImporter.Services;

/// <summary>
/// Converts .eml email files to PDF using QuestPDF (Community License, no CVEs).
/// The original .eml is preserved alongside the generated PDF.
/// </summary>
public class EmailConverterService(ILogger<EmailConverterService> logger)
{
    static EmailConverterService()
    {
        // Required by QuestPDF: declare community license
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public async Task<string> ConvertEmailToPdfAsync(string emlPath, CancellationToken ct = default)
    {
        logger.LogInformation("Converting email '{EmlPath}' to PDF", emlPath);

        var message = await Task.Run(() => MimeMessage.Load(emlPath), ct);
        var pdfPath = Path.ChangeExtension(emlPath, ".pdf");

        var bodyText = GetBodyText(message);

        var attachmentNames = message.Attachments
            .OfType<MimePart>()
            .Select(a => a.FileName)
            .Where(n => !string.IsNullOrEmpty(n))
            .ToList();

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Content().Column(col =>
                {
                    col.Spacing(8);

                    // Title
                    col.Item().Text("E-Mail Import")
                        .FontSize(18).Bold().FontColor(Colors.BlueGrey.Darken3);

                    // Separator
                    col.Item().PaddingVertical(4)
                        .LineHorizontal(1).LineColor(Colors.Grey.Lighten1);

                    // Metadata table
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.ConstantColumn(100);
                            c.RelativeColumn();
                        });

                        AddMetaRow(table, "Von", message.From.ToString());
                        AddMetaRow(table, "An",  message.To.ToString());
                        if (message.Cc.Count > 0)
                            AddMetaRow(table, "CC", message.Cc.ToString());
                        AddMetaRow(table, "Betreff", message.Subject ?? "(kein Betreff)");
                        AddMetaRow(table, "Datum",   message.Date.LocalDateTime.ToString("dd.MM.yyyy HH:mm:ss"));
                        if (!string.IsNullOrEmpty(message.MessageId))
                            AddMetaRow(table, "Message-ID", message.MessageId);
                        if (attachmentNames.Count > 0)
                            AddMetaRow(table, "Anhänge", string.Join(", ", attachmentNames));
                    });

                    // Body separator
                    col.Item().PaddingVertical(4)
                        .LineHorizontal(1).LineColor(Colors.Grey.Lighten1);

                    col.Item().Text("Inhalt:").Bold().FontSize(11);

                    // Body text
                    col.Item()
                        .Background(Colors.Grey.Lighten4)
                        .Padding(8)
                        .Text(bodyText.Length > 10000
                            ? bodyText[..10000] + "\n\n[… Text gekürzt …]"
                            : bodyText)
                        .FontFamily(Fonts.CourierNew)
                        .FontSize(9);

                    // Note about original EML
                    col.Item().PaddingTop(16)
                        .Text($"Originaldatei: {Path.GetFileName(emlPath)} (liegt neben dieser PDF)")
                        .FontSize(8).FontColor(Colors.Grey.Medium).Italic();
                });

                page.Footer().AlignCenter()
                    .Text(x =>
                    {
                        x.Span("Seite ").FontSize(8).FontColor(Colors.Grey.Medium);
                        x.CurrentPageNumber().FontSize(8).FontColor(Colors.Grey.Medium);
                        x.Span(" / ").FontSize(8).FontColor(Colors.Grey.Medium);
                        x.TotalPages().FontSize(8).FontColor(Colors.Grey.Medium);
                    });
            });
        }).GeneratePdf(pdfPath);

        logger.LogInformation("Email converted to PDF: '{PdfPath}'", pdfPath);
        return pdfPath;
    }

    private static void AddMetaRow(TableDescriptor table, string label, string value)
    {
        table.Cell().Padding(2).Text(label + ":").Bold();
        table.Cell().Padding(2).Text(value);
    }

    private static string GetBodyText(MimeMessage message)
    {
        if (!string.IsNullOrWhiteSpace(message.TextBody))
            return message.TextBody;
        if (!string.IsNullOrWhiteSpace(message.HtmlBody))
            return StripHtmlTags(message.HtmlBody);
        return "(Kein Inhalt)";
    }

    private static string StripHtmlTags(string html) =>
        System.Text.RegularExpressions.Regex.Replace(html, "<[^>]*>", " ")
            .Replace("&nbsp;", " ").Replace("&amp;", "&")
            .Replace("&lt;", "<").Replace("&gt;", ">")
            .Replace("&quot;", "\"").Trim();
}



