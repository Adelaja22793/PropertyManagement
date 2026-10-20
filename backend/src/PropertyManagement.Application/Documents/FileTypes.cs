using System.Text;

namespace PropertyManagement.Application.Documents;

/// <summary>
/// The file types the system accepts, recognised from the file's first bytes rather than the browser's claim, so a
/// script renamed to .pdf is refused. No HTML, SVG or executables: nothing that could run when opened from our domain.
/// </summary>
public static class FileTypes
{
    public sealed record FileType(string ContentType, string Extension, bool IsImage);

    public static readonly FileType Pdf = new("application/pdf", ".pdf", false);
    public static readonly FileType Jpeg = new("image/jpeg", ".jpg", true);
    public static readonly FileType Png = new("image/png", ".png", true);
    public static readonly FileType Webp = new("image/webp", ".webp", true);
    public static readonly FileType Docx = new("application/vnd.openxmlformats-officedocument.wordprocessingml.document", ".docx", false);
    public static readonly FileType Xlsx = new("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", ".xlsx", false);

    public const string Accepted = "PDF, JPEG, PNG, WebP, Word (.docx) or Excel (.xlsx)";

    /// <summary>The file's type, or null when it isn't one we accept.</summary>
    public static FileType? Detect(ReadOnlySpan<byte> content, string fileName)
    {
        if (content.StartsWith("%PDF-"u8)) return Pdf;
        if (content.Length >= 3 && content[0] == 0xFF && content[1] == 0xD8 && content[2] == 0xFF) return Jpeg;
        if (content.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return Png;
        if (content.Length >= 12 && content.StartsWith("RIFF"u8) && content[8..12].SequenceEqual("WEBP"u8)) return Webp;
        // Office files are zip archives; the extension says which kind. Macro-enabled variants (.docm, .xlsm) are refused.
        if (content.StartsWith(new byte[] { 0x50, 0x4B, 0x03, 0x04 }))
        {
            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            if (extension == ".docx" && Contains(content, "word/")) return Docx;
            if (extension == ".xlsx" && Contains(content, "xl/")) return Xlsx;
        }
        return null;
    }

    private static bool Contains(ReadOnlySpan<byte> content, string marker) =>
        content.IndexOf(Encoding.ASCII.GetBytes(marker)) >= 0;
}
