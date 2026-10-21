import type { OrganizationSummary } from '../../lib/types'
import { useSession } from '../../app/session'
import { Permissions } from '../../lib/permissions'
import { formatDate } from '../../lib/format'
import { ExportButton } from '../exports/ExportButton'

/** A cancelled company is closed, but its admins can download everything until the export window ends (design 4.8). */
export function CancelledNotice({ organization }: { organization: OrganizationSummary }) {
  const { can } = useSession()
  const until = organization.subscription?.exportUntilUtc
  return (
    <div className="mx-auto max-w-lg rounded-lg bg-white p-6 shadow-sm ring-1 ring-slate-200">
      <h1 className="text-lg font-semibold text-slate-900">This company's subscription has ended</h1>
      {can(Permissions.dataExport) ? (
        <>
          <p className="mt-2 text-sm text-slate-600">
            You can still download all of {organization.name}'s records and files{until ? ` until ${formatDate(until)}` : ''}. After that
            the data is deleted, apart from records the law requires us to keep.
          </p>
          <div className="mt-4 flex flex-wrap gap-3">
            <ExportButton path="/api/v1/exports/organization" fallbackName="organization-data.zip" label="Download records" />
            <ExportButton
              path="/api/v1/exports/organization?includeFiles=true"
              fallbackName="organization-data.zip"
              label="Download records and files"
            />
          </div>
        </>
      ) : (
        <p className="mt-2 text-sm text-slate-600">Ask an admin of {organization.name} if you need a copy of its records.</p>
      )}
    </div>
  )
}
