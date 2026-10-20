using PropertyManagement.Domain.Documents;

namespace PropertyManagement.Application.Documents;

public sealed record DocumentDto(
    Guid Id,
    DocumentSubject Subject,
    Guid SubjectId,
    DocumentCategory Category,
    string Title,
    string FileName,
    string ContentType,
    long SizeBytes,
    DateOnly? ExpiresOn,
    bool SharedWithOwner,
    bool SharedWithTenant,
    DocumentSource Source,
    string? UploadedBy,
    DateTime CreatedAtUtc);

/// <summary>A record's documents and what the caller may do with them.</summary>
public sealed record DocumentListDto(IReadOnlyList<DocumentDto> Items, bool CanUpload, bool CanEdit, bool CanDelete, bool CanViewIdentity);

/// <summary>A document due to expire, with the record it belongs to.</summary>
public sealed record ExpiringDocumentDto(
    Guid Id, string Title, DocumentCategory Category, DateOnly ExpiresOn, DocumentSubject Subject, Guid SubjectId, string SubjectLabel);

/// <summary>A document as an owner or tenant sees it in their portal.</summary>
public sealed record PortalDocumentDto(
    Guid Id,
    string Title,
    DocumentCategory Category,
    string FileName,
    string ContentType,
    long SizeBytes,
    DocumentSubject Subject,
    Guid SubjectId,
    string SubjectLabel,
    DateOnly? ExpiresOn,
    bool ByYou,
    DateTime CreatedAtUtc);

public sealed record UploadDocument(
    DocumentSubject Subject,
    Guid SubjectId,
    string FileName,
    ReadOnlyMemory<byte> Content,
    string? Title,
    DocumentCategory Category,
    DateOnly? ExpiresOn,
    bool SharedWithOwner,
    bool SharedWithTenant);

public sealed record UpdateDocumentRequest(string Title, DocumentCategory Category, DateOnly? ExpiresOn, bool SharedWithOwner, bool SharedWithTenant)
{
    public DocumentDetails ToDetails() => new(Title, Category, ExpiresOn, SharedWithOwner, SharedWithTenant);
}

/// <summary>The bytes of a document and how to send them.</summary>
public sealed record DocumentContent(Stream Content, string ContentType, string FileName, bool IsImage);
