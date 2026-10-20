using System.Text;
using PropertyManagement.Application.Documents;
using PropertyManagement.Domain.Common;
using PropertyManagement.Domain.Documents;

namespace PropertyManagement.UnitTests;

public class DocumentDomainTests
{
    private static readonly DateTime Now = new(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc);

    private static Document New(string contentType, DocumentCategory category = DocumentCategory.Other) =>
        new(Guid.NewGuid(), DocumentSubject.Property, Guid.NewGuid(), null,
            new DocumentDetails("Deed", category, null, false, false), "deed.pdf", contentType, 10, new string('a', 64),
            DocumentSource.Staff, Guid.NewGuid(), Now);

    [Theory]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData(@"C:\Users\seun\Desktop\lease.pdf", "lease.pdf")]
    [InlineData("in\"voice<1>.pdf", "invoice1.pdf")]
    [InlineData("   ", "file")]
    public void File_names_lose_paths_and_unsafe_characters(string input, string expected) =>
        Assert.Equal(expected, Document.CleanFileName(input));

    [Fact]
    public void Storage_key_comes_from_ids_only()
    {
        var document = New("application/pdf");
        Assert.Equal($"{document.OrganizationId:N}/{document.Id:N}", document.StorageKey);
    }

    [Fact]
    public void Only_images_are_photos_and_deleted_documents_stay_deleted()
    {
        Assert.Throws<DomainException>(() => New("application/pdf", DocumentCategory.Photo));
        var photo = New("image/png", DocumentCategory.Photo);
        photo.Delete(Guid.NewGuid(), Now);
        Assert.True(photo.IsDeleted);
        Assert.Equal("already_decided", Assert.Throws<DomainException>(() => photo.Delete(Guid.NewGuid(), Now)).Code);
        Assert.Throws<DomainException>(() => photo.Update(new DocumentDetails("x", DocumentCategory.Photo, null, false, false)));
    }

    [Fact]
    public void File_types_come_from_the_bytes()
    {
        Assert.Equal(FileTypes.Pdf, FileTypes.Detect("%PDF-1.4"u8, "anything.png"));
        Assert.Equal(FileTypes.Jpeg, FileTypes.Detect(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, "photo"));
        Assert.Equal(FileTypes.Webp, FileTypes.Detect("RIFF\0\0\0\0WEBPVP8 "u8, "p.webp"));
        var docx = Encoding.ASCII.GetBytes("PK\u0003\u0004....word/document.xml");
        Assert.Equal(FileTypes.Docx, FileTypes.Detect(docx, "lease.docx"));
        Assert.Null(FileTypes.Detect(docx, "lease.docm"));
        Assert.Null(FileTypes.Detect("<svg onload=alert(1)>"u8, "logo.svg"));
        Assert.Null(FileTypes.Detect("MZ\u0090\0"u8, "photo.jpg"));
    }
}
