# Property Management System

A multi-company property management platform for Nigerian property managers and landlords.
It follows the design in *Property Management System: Requirements & Technical Design v1.1*.

**Status: Phases 1 and 2 (foundation, sign-in, organization isolation and audit) Phase 3 (properties, units, owners and tenants) and the first part of Phase 4 (leases). No money features yet: invoices, payments and payouts wait for the accountant and tax adviser to sign off the [finance design](https://claude.ai/code/artifact/59369177-0d67-4085-a6e6-054ed378947e).**

## What works today

- Sign up as a management company or a self-managing landlord. The founder becomes Organization Admin and the organization starts a 30-day trial.
- Sign in and out, with lockout after 5 wrong passwords, rate-limited sign-in endpoints and password reset.
- One login can belong to several organizations and switch between them.
- Branches, and staff limited to the branches they work in.
- Inviting staff by email with a role (admin, branch manager, property manager, accountant, finance approver, agent, maintenance). Invitations are single-use, expire after 7 days and only work for the invited email.
- Changing roles and removing members. Removal takes effect on the member's next request. An organization always keeps at least one admin.
- Organization settings (contact details, TIN, VAT number, time zone). Currency is NGN.
- An append-only audit log of sign-ins, access denials and every sensitive change.

### Properties, owners and tenants (Phase 3)

- **Properties** with code, type, address, state and LGA, branch, responsible manager and amenities; archive when no unit is occupied.
- **Units** with a code unique within the property, type, rooms, size, default rent (NGN by default), billing frequency, service charge share and occupancy.
- **Who sees which properties.** Admins, accountants and finance approvers see all. Members limited to branches see those branches' properties and can only create properties there. A member whose only property role is Property Manager sees just the properties assigned to them.
- **Owners** (individuals or companies) with contact details, TIN and preferred channel.
- **Ownership shares** per property with effective dates. Shares are always replaced as a set that totals exactly 100%, from a chosen date, and history is kept.
- **Payout bank accounts** (10-digit NUBAN). A new or changed account is pending until a *different* person with `Owners.ApproveBankAccounts` approves it (maker-checker); approval replaces the previous account and emails the owner.
- **Management agreements** per owner, for one property or all of their properties: fee basis (percent of rent collected by default, percent invoiced, or fixed monthly), letting fee, payout frequency, advance rent release (on receipt by default, per decision 18.6), reserve and dates. Overlapping agreements are refused. Finance (Phase 4) will use these terms.
- **Tenants** (individuals or companies) with contacts, employer, emergency contact, RC number and TIN.
- **Identity details (KYC).** NIN or other ID and BVN are encrypted with ASP.NET Core Data Protection before they are stored. Everyone with `Tenants.View` sees only the ID type and last four digits; the full numbers need `Tenants.ViewKyc`, and every reveal is audited. Numbers never go in the audit log.
- **Guarantors** and **screening checks** (employer, previous landlord, references) recorded against the tenant, with who checked and the outcome.
- **Portal invitations** for owners and tenants. Accepting links the login to that owner or tenant record. Portal users have no staff permissions and are not listed as team members; the portal screens themselves come later.

### Leases (Phase 4, no money yet)

- **Drafts.** A lease names a unit, one or more tenants (the first is the primary tenant), guarantors who belong to those tenants, the term, rent per period, billing frequency, when rent falls due, grace and notice periods, holdover rent, renewal terms and other charges (caution deposit, agency and legal fees, stamp duty, service charge, others). Drafts can be changed freely and don't affect the unit.
- **Activating** fixes the terms and takes the unit. A unit can't have two leases on the same day. A lease past its end date with the tenant still in (holding over) keeps the unit until the move-out is recorded.
- **Stages** come from the dates: upcoming, active, expiring (within 90 days), holding over, then renewed, ended, terminated or cancelled.
- **Renewing** drafts the next lease for the same unit, tenants and guarantors, starting the day after the current one ends, with the new rent (rent review). Recurring charges carry over; move-in charges don't. Activating the renewal closes the current lease as renewed.
- **Ending** records the move-out date. Before the end date it is an early termination and needs a reason. The unit is free from the move-out day. Nothing is deleted.
- **Occupancy is driven by leases.** Activating, renewing or ending a lease updates the unit (occupied, reserved for a future start, or vacant), and an hourly job catches up date changes. Staff can't set occupancy by hand, or archive the unit, while it has an active lease.
- Leases follow property visibility, so a property manager sees only the leases on their properties.
- **Notices.** Record a notice served on the tenants (notice to quit, 7-day notice of owner's intention to recover possession, rent review, other): date, method of service, who served it and when it expires. The expiry defaults to the lease's notice period, or the Lagos Tenancy Law 2011 period for the tenancy (6 months yearly, 3 months half-yearly or quarterly, 1 month monthly; 7 days for owner's intention). The owner's intention notice is refused until a notice to quit has expired. Notices can be withdrawn but not deleted. A lawyer should confirm the periods for each organization.
- **Inspections.** One move-in and one move-out condition report per lease, area by area. A move-out report starts from the move-in areas and can propose deductions from the caution deposit (a proposal only until the finance work lands).
- **Reminders.** An hourly job emails the property's manager (or the organization admins when there is none) 90, 60 and 30 days before a lease ends, when a lease starts holding over, and when a served notice expires. Leases with a renewal drafted are skipped. Each reminder is sent once.

## How organization isolation works

Every organization-owned table has an `OrganizationId`. Four layers keep companies apart:

1. **Server-chosen organization.** The `X-Organization-Id` header (or `{organizationId}` in the URL) is only a request. `OrganizationContextMiddleware` accepts it only if the signed-in user has an *active* membership, checked against the database on every request.
2. **Query filters.** `AppDbContext` adds a filter to every organization-owned entity. With no organization selected, queries return nothing.
3. **Write guard.** `AppDbContext.SaveChanges` refuses any insert, update or delete of another organization's row, even one loaded with `IgnoreQueryFilters()`.
4. **Composite foreign keys.** Child rows reference `(OrganizationId, Id)`, so the database itself rejects links across organizations.

`tests/PropertyManagement.IntegrationTests/IsolationTests.cs` and `PropertyIsolationTests.cs` attack each layer with two companies, ABC and XYZ, including properties, units, owners, tenants and identity details.
SQL Server Row-Level Security (design 4.4, optional) is not added yet.

## Project layout

```text
backend/    ASP.NET Core Web API (.NET 10), EF Core, SQL Server
  src/PropertyManagement.Domain          entities and business rules
  src/PropertyManagement.Application     use cases, permissions, roles
  src/PropertyManagement.Infrastructure  EF Core, Identity, migrations
  src/PropertyManagement.Api             controllers, auth, middleware
  tests/                                 unit and integration tests
frontend/   React 19 + TypeScript + Vite, TanStack Query, React Hook Form, Zod, Tailwind
```

## Running it locally

You need the .NET 10 SDK, Node 22 and Docker.

```bash
docker compose up -d                       # SQL Server on localhost:1433

cd backend
dotnet run --project src/PropertyManagement.Api --urls http://localhost:5092
# Development settings create and migrate the database on startup.

cd ../frontend
npm install
npm run dev                                # http://localhost:5173 (forwards /api to :5092)
```

Emails (invitations, password resets) are only logged in development. The person who sends an invitation gets a link to share.

## Tests

```bash
cd backend && dotnet test        # 60 unit + 77 integration tests (needs SQL Server; set PMS_TEST_SQL to use another)
cd frontend && npm test          # 31 component and client tests
cd frontend && npm run e2e       # browser journeys on desktop and phone sizes (needs the API running)
```

Each integration test class creates its own database and drops it afterwards.

## API

| Method | Path | Notes |
| --- | --- | --- |
| POST | /api/v1/auth/register | New organization, or join with `invitationToken` |
| POST | /api/v1/auth/login, /logout | Cookie session |
| GET | /api/v1/auth/me | User and organizations with roles and permissions |
| POST | /api/v1/auth/forgot-password, /reset-password | |
| GET, POST | /api/v1/organizations | Mine; create another |
| GET, PUT | /api/v1/organizations/{id} | Settings |
| GET | /api/v1/organizations/{id}/members | |
| PATCH, DELETE | /api/v1/organizations/{id}/members/{membershipId} | Change roles or branch scope; remove |
| GET, POST | /api/v1/organizations/{id}/invitations | |
| DELETE | /api/v1/organizations/{id}/invitations/{invitationId} | Cancel |
| POST | /api/v1/invitations/accept | |
| GET, POST | /api/v1/branches | Uses `X-Organization-Id` |
| GET, PUT | /api/v1/branches/{id} | |
| GET | /api/v1/audit-logs | Paged |
| GET, POST | /api/v1/properties | Search (`search`, `branchId`, `status`, `includeArchived`, paging); create |
| GET | /api/v1/properties/manager-options | Staff who can be assigned as manager |
| GET, PUT | /api/v1/properties/{id} | |
| POST | /api/v1/properties/{id}/archive | |
| GET, POST | /api/v1/properties/{id}/units | |
| GET, PUT | /api/v1/properties/{id}/owners | Current and past shares; replace from a date (must total 100%) |
| GET, PUT | /api/v1/units/{id} | |
| POST | /api/v1/units/{id}/archive | |
| GET, POST | /api/v1/owners | Search; create |
| GET, PUT | /api/v1/owners/{id} | |
| GET, POST | /api/v1/owners/{id}/bank-accounts | New accounts wait for approval |
| POST | /api/v1/owners/{id}/bank-accounts/{accountId}/decision | Approve or reject; not by the person who added it |
| GET, POST | /api/v1/owners/{id}/agreements | |
| PUT | /api/v1/owners/{id}/agreements/{agreementId} | |
| POST | /api/v1/owners/{id}/portal-invite | |
| GET, POST | /api/v1/tenants | Search; create |
| GET, PUT | /api/v1/tenants/{id} | |
| GET, PUT | /api/v1/tenants/{id}/kyc | GET returns full numbers (needs `Tenants.ViewKyc`, audited); PUT replaces them |
| PUT | /api/v1/tenants/{id}/kyc/verification | |
| GET, POST | /api/v1/tenants/{id}/guarantors | |
| PUT, DELETE | /api/v1/tenants/{id}/guarantors/{guarantorId} | |
| GET, POST | /api/v1/tenants/{id}/screening | |
| POST | /api/v1/tenants/{id}/portal-invite | |
| GET, POST | /api/v1/leases | Search (`search`, `stage`, `propertyId`, `unitId`, `tenantId`, `endingWithinDays`, paging); create a draft |
| GET, PUT | /api/v1/leases/{id} | PUT changes a draft only |
| POST | /api/v1/leases/{id}/activate | Draft to active; refuses overlapping dates |
| POST | /api/v1/leases/{id}/cancel | Discard a draft |
| POST | /api/v1/leases/{id}/renew | Draft the next lease (`Leases.Renew`) |
| POST | /api/v1/leases/{id}/end | Record the move-out (`Leases.Terminate`); before the end date needs a reason |
| GET, POST | /api/v1/leases/{id}/notices | List; record a served notice (`Leases.IssueNotice`) |
| GET | /api/v1/leases/{id}/notices/default | Suggested expiry for `type` and `servedOn` |
| POST | /api/v1/leases/{id}/notices/{noticeId}/withdraw | |
| GET | /api/v1/leases/{id}/inspections | Move-in and move-out reports, with the deposit on the lease |
| PUT | /api/v1/leases/{id}/inspections/{MoveIn\|MoveOut} | Record or replace a report |
| GET | /api/v1/plans | Public |
| GET | /health | |

State-changing requests must send `X-Requested-With: XMLHttpRequest` (CSRF protection). Errors are RFC 9457 problem details with a `code` field.

## Known gaps before production

- A real email provider (invitations and resets are only logged).
- Two-factor sign-in for admins and finance roles (design 11.1).
- SQL Server Row-Level Security as an extra isolation layer.
- Platform admin area, support-access grants and subscription billing.
- Organization-defined custom roles (roles are fixed in code for now).
- The audit table is append-only in code; also deny UPDATE and DELETE to the app's database login.
- Data Protection keys (which encrypt identity numbers and sign-in cookies) are stored in the `DataProtectionKeys` table. In production also encrypt the keys themselves, for example with `ProtectKeysWithAzureKeyVault` or a certificate, so a database copy alone can't decrypt NIN or BVN.
- Invoices, payments, receipts, deposits held and owner payouts (rest of Phase 4) wait for the finance design sign-off.
- Generating notice and tenancy agreement documents from lawyer-approved templates, and inspection photos (both need file storage).
- Reminders to tenants themselves (by SMS or the tenant portal), and notice periods set per organization or state rather than the Lagos defaults.
- Shared occupancy of one unit by several separate leases.
- Two people activating overlapping leases at the very same moment could both succeed; add a database lock or check if that becomes a real risk.
- Owner and tenant portal screens (the invitations and record links are in place).
- Documents and photos for properties, owners and tenants (needs file storage with virus scanning, design 3.5).
