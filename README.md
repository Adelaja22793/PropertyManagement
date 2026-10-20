# Property Management System

A multi-company property management platform for Nigerian property managers and landlords.
It follows the design in *Property Management System: Requirements & Technical Design v1.1*.

**Status: Phase 1 (foundation and sign-in) plus the core of Phase 2 (organization isolation and audit).**

## What works today

- Sign up as a management company or a self-managing landlord. The founder becomes Organization Admin and the organization starts a 30-day trial.
- Sign in and out, with lockout after 5 wrong passwords, rate-limited sign-in endpoints and password reset.
- One login can belong to several organizations and switch between them.
- Branches, and staff limited to the branches they work in.
- Inviting staff by email with a role (admin, branch manager, property manager, accountant, finance approver, agent, maintenance). Invitations are single-use, expire after 7 days and only work for the invited email.
- Changing roles and removing members. Removal takes effect on the member's next request. An organization always keeps at least one admin.
- Organization settings (contact details, TIN, VAT number, time zone). Currency is NGN.
- An append-only audit log of sign-ins, access denials and every sensitive change.

## How organization isolation works

Every organization-owned table has an `OrganizationId`. Four layers keep companies apart:

1. **Server-chosen organization.** The `X-Organization-Id` header (or `{organizationId}` in the URL) is only a request. `OrganizationContextMiddleware` accepts it only if the signed-in user has an *active* membership, checked against the database on every request.
2. **Query filters.** `AppDbContext` adds a filter to every organization-owned entity. With no organization selected, queries return nothing.
3. **Write guard.** `AppDbContext.SaveChanges` refuses any insert, update or delete of another organization's row, even one loaded with `IgnoreQueryFilters()`.
4. **Composite foreign keys.** Child rows reference `(OrganizationId, Id)`, so the database itself rejects links across organizations.

`tests/PropertyManagement.IntegrationTests/IsolationTests.cs` attacks each layer with two companies, ABC and XYZ.
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
cd backend && dotnet test        # 19 unit + 24 integration tests (needs SQL Server; set PMS_TEST_SQL to use another)
cd frontend && npm test          # 13 component and client tests
cd frontend && npm run e2e       # browser journey on desktop and phone sizes (needs the API running)
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
