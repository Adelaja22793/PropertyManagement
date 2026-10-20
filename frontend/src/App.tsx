import { lazy, Suspense } from 'react'
import { Navigate, Route, Routes } from 'react-router-dom'
import { AppLayout } from './app/AppLayout'
import { GuestOnly, RequirePermission, RequireSession, RequireUser } from './app/guards'
import { Permissions } from './lib/permissions'
import { LoginPage } from './features/auth/LoginPage'
import { RegisterPage } from './features/auth/RegisterPage'
import { ForgotPasswordPage, ResetPasswordPage } from './features/auth/PasswordPages'
import { DashboardPage } from './features/dashboard/DashboardPage'
import { BranchesPage } from './features/branches/BranchesPage'
import { TeamPage } from './features/team/TeamPage'
import { AuditPage } from './features/audit/AuditPage'
import { SettingsPage } from './features/organizations/SettingsPage'
import { NewOrganizationPage } from './features/organizations/NewOrganizationPage'
import { JoinPage } from './features/organizations/JoinPage'

// Phase 3 screens load on first visit to keep the first download small.
const PropertiesPage = lazy(() => import('./features/properties/PropertiesPage').then((m) => ({ default: m.PropertiesPage })))
const PropertyDetailPage = lazy(() => import('./features/properties/PropertyDetailPage').then((m) => ({ default: m.PropertyDetailPage })))
const OwnersPage = lazy(() => import('./features/owners/OwnersPage').then((m) => ({ default: m.OwnersPage })))
const OwnerDetailPage = lazy(() => import('./features/owners/OwnerDetailPage').then((m) => ({ default: m.OwnerDetailPage })))
const TenantsPage = lazy(() => import('./features/tenants/TenantsPage').then((m) => ({ default: m.TenantsPage })))
const TenantDetailPage = lazy(() => import('./features/tenants/TenantDetailPage').then((m) => ({ default: m.TenantDetailPage })))
const LeasesPage = lazy(() => import('./features/leases/LeasesPage').then((m) => ({ default: m.LeasesPage })))
const LeaseDetailPage = lazy(() => import('./features/leases/LeaseDetailPage').then((m) => ({ default: m.LeaseDetailPage })))
const LeaseEditorPage = lazy(() => import('./features/leases/LeaseEditorPage').then((m) => ({ default: m.LeaseEditorPage })))
const MaintenancePage = lazy(() => import('./features/maintenance/MaintenancePage').then((m) => ({ default: m.MaintenancePage })))
const MaintenanceDetailPage = lazy(() => import('./features/maintenance/MaintenanceDetailPage').then((m) => ({ default: m.MaintenanceDetailPage })))
const PortalHome = lazy(() => import('./features/portal/PortalHome').then((m) => ({ default: m.PortalHome })))
const ListingsPage = lazy(() => import('./features/listings/ListingsPage').then((m) => ({ default: m.ListingsPage })))
const ListingDetailPage = lazy(() => import('./features/listings/ListingDetailPage').then((m) => ({ default: m.ListingDetailPage })))
const ListingEditorPage = lazy(() => import('./features/listings/ListingEditorPage').then((m) => ({ default: m.ListingEditorPage })))
const LeadDetailPage = lazy(() => import('./features/listings/LeadDetailPage').then((m) => ({ default: m.LeadDetailPage })))
const PublicCompanyPage = lazy(() => import('./features/public/PublicPages').then((m) => ({ default: m.PublicCompanyPage })))
const PublicListingPage = lazy(() => import('./features/public/PublicPages').then((m) => ({ default: m.PublicListingPage })))
const ReportsPage = lazy(() => import('./features/reports/ReportsPage').then((m) => ({ default: m.ReportsPage })))
const AccountSecurityPage = lazy(() => import('./features/account/AccountSecurityPage').then((m) => ({ default: m.AccountSecurityPage })))
const SupportDeskPage = lazy(() => import('./features/support/SupportDeskPage').then((m) => ({ default: m.SupportDeskPage })))
const VendorsPage = lazy(() => import('./features/maintenance/VendorsPage').then((m) => ({ default: m.VendorsPage })))

const maintenanceAccess = [Permissions.maintenanceView, Permissions.maintenanceUpdateAssigned]
const vendorAccess = [Permissions.maintenanceView, Permissions.maintenanceManage]
const listingAccess = [Permissions.listingsManage, Permissions.leadsManage]
const reportAccess = [Permissions.propertiesView, Permissions.leasesView, Permissions.maintenanceView, Permissions.leadsManage, Permissions.auditView]

export function App() {
  return (
    <Routes>
      <Route path="/login" element={<GuestOnly><LoginPage /></GuestOnly>} />
      <Route path="/register" element={<GuestOnly><RegisterPage /></GuestOnly>} />
      <Route path="/forgot-password" element={<GuestOnly><ForgotPasswordPage /></GuestOnly>} />
      <Route path="/reset-password" element={<ResetPasswordPage />} />
      <Route path="/join" element={<JoinPage />} />
      {/* The public site: no sign-in. */}
      <Route path="/to-let/:slug" element={<Suspense fallback={null}><PublicCompanyPage /></Suspense>} />
      <Route path="/listing/:code" element={<Suspense fallback={null}><PublicListingPage /></Suspense>} />
      <Route path="/organizations/new" element={<RequireUser><NewOrganizationPage /></RequireUser>} />
      <Route path="/app" element={<RequireSession><AppLayout /></RequireSession>}>
        <Route index element={<DashboardPage />} />
        <Route path="properties" element={<RequirePermission permission={Permissions.propertiesView}><PropertiesPage /></RequirePermission>} />
        <Route path="properties/:id" element={<RequirePermission permission={Permissions.propertiesView}><PropertyDetailPage /></RequirePermission>} />
        <Route path="owners" element={<RequirePermission permission={Permissions.ownersView}><OwnersPage /></RequirePermission>} />
        <Route path="owners/:id" element={<RequirePermission permission={Permissions.ownersView}><OwnerDetailPage /></RequirePermission>} />
        <Route path="tenants" element={<RequirePermission permission={Permissions.tenantsView}><TenantsPage /></RequirePermission>} />
        <Route path="tenants/:id" element={<RequirePermission permission={Permissions.tenantsView}><TenantDetailPage /></RequirePermission>} />
        <Route path="leases" element={<RequirePermission permission={Permissions.leasesView}><LeasesPage /></RequirePermission>} />
        <Route path="leases/new" element={<RequirePermission permission={Permissions.leasesCreate}><LeaseEditorPage /></RequirePermission>} />
        <Route path="leases/:id" element={<RequirePermission permission={Permissions.leasesView}><LeaseDetailPage /></RequirePermission>} />
        <Route path="leases/:id/edit" element={<RequirePermission permission={Permissions.leasesCreate}><LeaseEditorPage /></RequirePermission>} />
        <Route path="maintenance" element={<RequirePermission permission={maintenanceAccess}><MaintenancePage /></RequirePermission>} />
        <Route path="maintenance/:id" element={<RequirePermission permission={maintenanceAccess}><MaintenanceDetailPage /></RequirePermission>} />
        <Route path="vendors" element={<RequirePermission permission={vendorAccess}><VendorsPage /></RequirePermission>} />
        <Route path="listings" element={<RequirePermission permission={listingAccess}><ListingsPage /></RequirePermission>} />
        <Route path="listings/new" element={<RequirePermission permission={Permissions.listingsManage}><ListingEditorPage /></RequirePermission>} />
        <Route path="listings/:id" element={<RequirePermission permission={listingAccess}><ListingDetailPage /></RequirePermission>} />
        <Route path="listings/:id/edit" element={<RequirePermission permission={Permissions.listingsManage}><ListingEditorPage /></RequirePermission>} />
        <Route path="leads/:id" element={<RequirePermission permission={Permissions.leadsManage}><LeadDetailPage /></RequirePermission>} />
        <Route path="reports" element={<RequirePermission permission={reportAccess}><ReportsPage /></RequirePermission>} />
        <Route path="portal" element={<PortalHome />} />
        <Route path="security" element={<AccountSecurityPage />} />
        <Route path="support-desk" element={<SupportDeskPage />} />
        <Route path="branches" element={<RequirePermission permission={Permissions.branchesView}><BranchesPage /></RequirePermission>} />
        <Route path="team" element={<RequirePermission permission={Permissions.usersView}><TeamPage /></RequirePermission>} />
        <Route path="audit" element={<RequirePermission permission={Permissions.auditView}><AuditPage /></RequirePermission>} />
        <Route path="settings" element={<RequirePermission permission={Permissions.organizationView}><SettingsPage /></RequirePermission>} />
      </Route>
      <Route path="*" element={<Navigate to="/app" replace />} />
    </Routes>
  )
}
