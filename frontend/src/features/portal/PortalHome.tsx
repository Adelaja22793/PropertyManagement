import { useSession } from '../../app/session'
import { roleLabel } from '../../lib/permissions'
import { Card } from '../../components/ui'

/** What an owner or tenant sees until their portal is built: no staff pages. */
export function PortalHome() {
  const { me, organization } = useSession()
  const portal = organization?.roles.includes('Owner') ? 'owner' : 'tenant'
  return (
    <Card>
      <div className="py-6 text-center">
        <h1 className="text-lg font-semibold">Welcome, {me?.fullName.split(' ')[0]}</h1>
        <p className="mt-2 text-sm text-slate-600">
          You're connected to {organization?.name} as a {organization?.roles.map(roleLabel).join(', ').toLowerCase()}.
        </p>
        <p className="mt-4 text-sm font-medium text-slate-800">Your {portal} portal is coming soon.</p>
        <p className="mt-1 text-sm text-slate-600">
          {portal === 'owner'
            ? "You'll be able to see your properties, statements and payouts here."
            : "You'll be able to see your lease, invoices and receipts, and report repairs here."}
        </p>
      </div>
    </Card>
  )
}
