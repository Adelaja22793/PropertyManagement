import { useMutation } from '@tanstack/react-query'
import { ApiError, downloadFile } from '../../lib/api'
import { Button } from '../../components/ui'

/** Downloads a ZIP export (design 4.8 and 11.4). The server builds the file name. */
export function ExportButton({
  path,
  fallbackName,
  label,
  ariaLabel,
  variant = 'secondary',
}: {
  path: string
  fallbackName: string
  label: string
  /** Names the person when several buttons share a label. */
  ariaLabel?: string
  variant?: 'secondary' | 'ghost'
}) {
  const download = useMutation({ mutationFn: () => downloadFile(path, fallbackName) })
  return (
    <span className="inline-flex flex-col items-end gap-1">
      <Button variant={variant} aria-label={ariaLabel} onClick={() => download.mutate()} disabled={download.isPending}>
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
