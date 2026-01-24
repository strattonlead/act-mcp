using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UglyToad.PdfPig;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.Extensions.Logging;

namespace ACT.Services;

public interface IFileParsingService
{
    Task<string> ExtractTextAsync(Stream stream, string fileName);
}

public class FileParsingService : IFileParsingService
{
    private readonly ILogger<FileParsingService> _logger;

    public FileParsingService(ILogger<FileParsingService> logger)
    {
        _logger = logger;
    }

    public async Task<string> ExtractTextAsync(Stream stream, string fileName)
    {
        try
        {
            var extension = Path.GetExtension(fileName).ToLowerInvariant();

            // Reset stream position
            if (stream.Position > 0)
                stream.Position = 0;

            return extension switch
            {
                ".txt" => await ExtractFromTxtAsync(stream),
                ".pdf" => ExtractFromPdf(stream),
                ".docx" => ExtractFromDocx(stream),
                _ => throw new NotSupportedException($"File extension {extension} is not supported.")
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to extract text from {fileName}");
            throw;
        }
    }

    private async Task<string> ExtractFromTxtAsync(Stream stream)
    {
        using var reader = new StreamReader(stream, leaveOpen: true);
        return await reader.ReadToEndAsync();
    }

    private string ExtractFromPdf(Stream stream)
    {
        var sb = new StringBuilder();
        // PdfPig requires a seekable stream. If the input stream isn't, copy to MemoryStream.
        // But usually browser uploads or file streams are seekable.
        // Note: PdfPig.Open takes a stream.
        
        using (var document = PdfDocument.Open(stream, new ParsingOptions { ClipPaths = true }))
        {
            foreach (var page in document.GetPages())
            {
                sb.AppendLine(page.Text);
            }
        }
        return sb.ToString();
    }

    private string ExtractFromDocx(Stream stream)
    {
        using (var wordDoc = WordprocessingDocument.Open(stream, false))
        {
            var body = wordDoc.MainDocumentPart?.Document.Body;
            return body?.InnerText ?? string.Empty;
        }
    }
}
