using PropertyManagement.Domain.Properties;

namespace PropertyManagement.Application.Properties;

public sealed record PropertySummaryDto(
    Guid Id, string Code, string Name, PropertyType Type, string AddressLine, string? City, string State,
    Guid? BranchId, string? BranchName, Guid? ManagerUserId, PropertyStatus Status,
    int UnitCount, int OccupiedUnitCount);

public sealed record PropertyDetailDto(
    Guid Id, string Code, string Name, PropertyType Type, string? Description, string AddressLine, string? City,
    string State, string? LocalGovernmentArea, Guid? BranchId, string? BranchName, Guid? ManagerUserId,
    string? ManagerName, string? Amenities, PropertyStatus Status, DateTime CreatedAtUtc, DateTime? ArchivedAtUtc,
    int UnitCount, int OccupiedUnitCount, IReadOnlyList<OwnershipShareDto> CurrentOwners);

public sealed record SavePropertyRequest(
    string Code, string Name, PropertyType Type, string? Description, string AddressLine, string? City,
    string State, string? LocalGovernmentArea, Guid? BranchId, Guid? ManagerUserId, string? Amenities,
    PropertyStatus Status = PropertyStatus.Active)
{
    internal PropertyDetails ToDetails() => new(Code, Name, Type, Description, AddressLine, City, State,
        LocalGovernmentArea, BranchId, ManagerUserId, Amenities);
}

public sealed record PropertySearch(string? Search, Guid? BranchId, PropertyStatus? Status, bool IncludeArchived = false);

public sealed record UnitDto(
    Guid Id, Guid PropertyId, string PropertyCode, string Code, UnitType Type, string? Floor, int? Bedrooms,
    int? Bathrooms, decimal? SizeSquareMetres, decimal DefaultRent, string Currency, BillingFrequency BillingFrequency,
    decimal? ServiceChargeSharePercent, OccupancyStatus OccupancyStatus, string? Amenities, string? Notes,
    bool IsArchived, int? SharedOccupancyLimit, int ActiveLeaseCount);

public sealed record SaveUnitRequest(
    string Code, UnitType Type, string? Floor, int? Bedrooms, int? Bathrooms, decimal? SizeSquareMetres,
    decimal DefaultRent, string? Currency, BillingFrequency BillingFrequency, decimal? ServiceChargeSharePercent,
    string? Amenities, string? Notes, OccupancyStatus OccupancyStatus = OccupancyStatus.Vacant, int? SharedOccupancyLimit = null)
{
    internal UnitDetails ToDetails() => new(Code, Type, Floor, Bedrooms, Bathrooms, SizeSquareMetres, DefaultRent,
        Currency, BillingFrequency, ServiceChargeSharePercent, Amenities, Notes, SharedOccupancyLimit);
}

public sealed record OwnershipShareDto(Guid OwnerId, string OwnerName, decimal SharePercent, DateOnly EffectiveFrom, DateOnly? EffectiveTo);

public sealed record OwnershipDto(IReadOnlyList<OwnershipShareDto> Current, IReadOnlyList<OwnershipShareDto> History);

public sealed record OwnerShareInput(Guid OwnerId, decimal SharePercent);

/// <summary>Replaces the property's owners from <see cref="EffectiveFrom"/>. Shares must total 100%.</summary>
public sealed record SetOwnershipRequest(DateOnly EffectiveFrom, IReadOnlyList<OwnerShareInput> Shares);

public sealed record StaffOptionDto(Guid UserId, string FullName);
