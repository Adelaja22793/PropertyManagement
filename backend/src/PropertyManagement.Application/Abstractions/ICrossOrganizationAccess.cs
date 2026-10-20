namespace PropertyManagement.Application.Abstractions;

/// <summary>
/// Lifts the database's Row-Level Security for a deliberate lookup across organizations (design 4.4), such as
/// listing the organizations a user belongs to. Every use must also say which rows it wants explicitly, because the
/// query filters are ignored too. Dispose to restore the limit.
/// </summary>
public interface ICrossOrganizationAccess
{
    IDisposable Allow();
}
