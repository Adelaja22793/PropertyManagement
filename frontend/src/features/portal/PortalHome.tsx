import { useSession } from '../../app/session'
import { OwnerPortal } from './OwnerPortal'
import { TenantPortal } from './TenantPortal'

/** The owner and tenant portals. Someone who is both sees both. */
export function PortalHome() {
  const { organization } = useSession()
  const roles = organization?.roles ?? []
  const owner = roles.includes('Owner')
  const tenant = roles.includes('Tenant')
  return (
    <div className="space-y-10">
      {owner && <OwnerPortal />}
      {tenant && <TenantPortal />}
    </div>
  )
}
