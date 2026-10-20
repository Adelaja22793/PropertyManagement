import { useMutation } from '@tanstack/react-query'
import { ApiError, downloadFile } from '../../lib/api'
import { Button } from '../../components/ui'

/** Downloads a ZIP export (design 4.8 and 11.4). The server builds the file name. */
export function ExportButton({ path, fallbackName, label }: { path: string; fallbackName: string; label: string }) {
  const download = useMutation({ mutationFn: () => downloadFile(path, fallbackName) })
  return (
    <span className="inline-flex flex-col items-end gap-1">
      <Button variant="secondary" onClick={() => download.mutate()} disabled={download.isPending}>
        {download.isPending ? 'Preparing…' : label}
      </Button>
      {download.error && (
        <span role="alert" className="text-xs text-red-600">
          {download.error instanceof ApiError ? download.error.message : 'The download failed. Please try again.'}
        </span>
      )}
    </span>
  )
}
