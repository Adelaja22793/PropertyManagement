using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PropertyManagement.Infrastructure.Persistence;

namespace PropertyManagement.Api.Controllers;

public sealed record PlanDto(string Code, string Name, int MaxUnits, int MaxUsers, decimal MonthlyPriceNgn,
    decimal AnnualPriceNgn, int TrialDays);

[ApiController]
[Route("api/v1/plans")]
public sealed class PlansController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    public async Task<IReadOnlyList<PlanDto>> List(CancellationToken ct) =>
        await db.SubscriptionPlans.Where(p => p.IsActive).OrderBy(p => p.MaxUnits)
            .Select(p => new PlanDto(p.Code, p.Name, p.MaxUnits, p.MaxUsers, p.MonthlyPriceNgn, p.AnnualPriceNgn, p.TrialDays))
            .ToListAsync(ct);
}
