namespace PropertyManagement.Application.Common;

/// <summary>The resource does not exist in the active organization. Also used to hide other organizations' records.</summary>
public sealed class NotFoundException(string resource) : Exception($"{resource} was not found.");

/// <summary>The caller is authenticated but not allowed to do this.</summary>
public sealed class ForbiddenException(string message) : Exception(message);

/// <summary>An attempt to write a record that belongs to a different organization. Always a bug or an attack.</summary>
public sealed class CrossOrganizationWriteException(string entity) :
    Exception($"Blocked a write to {entity} belonging to another organization.");

/// <summary>The endpoint needs an active organization, but the request did not select one.</summary>
public sealed class OrganizationContextRequiredException() :
    Exception("Select an organization for this request (X-Organization-Id header).");
