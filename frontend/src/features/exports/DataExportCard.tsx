import { useState } from 'react'
import { Card } from '../../components/ui'
import { ExportButton } from './ExportButton'

/** Settings card for downloading everything the organization holds (design 4.8). */
export function DataExportCard() {
  const [includeFiles, setIncludeFiles] = useState(false)
  return (
    <Card title="Download your data">
      <div className="space-y-3">
        <p className="text-sm text-slate-600">
          A ZIP of spreadsheets (CSV) with every record your organization holds: properties, owners, tenants, leases, repairs, listings,
          your team and the audit log. Identity numbers are left out except their last four digits. Downloads are recorded in the audit log.
        </p>
        <label className="flex items-center gap-2 text-sm text-slate-700">
          <input
            type="checkbox"
            checked={includeFiles}
            onChange={(e) => setIncludeFiles(e.target.checked)}
            className="rounded border-slate-300"
          />
          Include uploaded files and photos (larger download)
        </label>
        <ExportButton
          path={`/api/v1/exports/organization${includeFiles ? '?includeFiles=true' : ''}`}
          fallbackName="organization-data.zip"
          label="Download all data"
        />
        <p className="text-xs text-slate-500">
          For one person's data, for example when a tenant asks what you hold about them, use Download their data on their page.
        </p>
      </div>
    </Card>
  )
}
