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

export function App() {
  return (
    <Routes>
      <Route path="/login" element={<GuestOnly><LoginPage /></GuestOnly>} />
      <Route path="/register" element={<GuestOnly><RegisterPage /></GuestOnly>} />
      <Route path="/forgot-password" element={<GuestOnly><ForgotPasswordPage /></GuestOnly>} />
      <Route path="/reset-password" element={<ResetPasswordPage />} />
      <Route path="/join" element={<JoinPage />} />
      <Route path="/organizations/new" element={<RequireUser><NewOrganizationPage /></RequireUser>} />
      <Route path="/app" element={<RequireSession><AppLayout /></RequireSession>}>
        <Route index element={<DashboardPage />} />
        <Route path="branches" element={<RequirePermission permission={Permissions.branchesView}><BranchesPage /></RequirePermission>} />
        <Route path="team" element={<RequirePermission permission={Permissions.usersView}><TeamPage /></RequirePermission>} />
        <Route path="audit" element={<RequirePermission permission={Permissions.auditView}><AuditPage /></RequirePermission>} />
        <Route path="settings" element={<RequirePermission permission={Permissions.organizationView}><SettingsPage /></RequirePermission>} />
      </Route>
      <Route path="*" element={<Navigate to="/app" replace />} />
    </Routes>
  )
}
