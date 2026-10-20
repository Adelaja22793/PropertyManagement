import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { get, post } from '../../lib/api'
import type { SupportGrant } from '../../lib/types'
import { Badge, Button, Card, ErrorBox } from '../../components/ui'
import { grantTiming, statusTone } from './supportGrant'

/** The admin's view of support access requests: approve, decline, or end access early (design 4.7). */
export function SupportAccessCard() {
  const queryClient = useQueryClient()
  const grants = useQuery({ queryKey: ['support-grants'], queryFn: () => get<SupportGrant[]>('/api/v1/support-grants') })
  const act = useMutation({
    mutationFn: ({ id, action }: { id: string; action: 'approve' | 'decline' | 'revoke' }) =>
      post<SupportGrant>(`/api/v1/support-grants/${id}/${action}`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['support-grants'] }),
  })
  const list = grants.data ?? []

  return (
    <Card title="Support access">
      <p className="text-sm text-slate-600">
        Our support team can't see your data unless you let them. When they ask, you choose: access is read-only, lasts at most a day, and
        every page they open is in your audit log.
      </p>
      <div className="mt-3">
        <ErrorBox error={grants.error ?? act.error} />
      </div>
      {list.length === 0 ? (
        <p className="mt-3 text-sm text-slate-500">No requests yet.</p>
      ) : (
        <ul className="mt-2 divide-y divide-slate-100">
          {list.slice(0, 10).map((g) => (
            <li key={g.id} className="flex flex-wrap items-start justify-between gap-3 py-3">
              <div className="min-w-0">
                <p className="text-sm font-medium">
                  {g.requestedByName} <span className="font-normal text-slate-500">({g.requestedByEmail})</span>
                </p>
                <p className="text-sm text-slate-700">{g.reason}</p>
                <p className="text-xs text-slate-500">{grantTiming(g)}</p>
              </div>
              <div className="flex items-center gap-2">
                <Badge tone={statusTone[g.status]}>{g.status}</Badge>
                {g.status === 'Pending' && (
                  <>
                    <Button onClick={() => act.mutate({ id: g.id, action: 'approve' })} disabled={act.isPending}>
                      Allow for {g.hours} {g.hours === 1 ? 'hour' : 'hours'}
                    </Button>
                    <Button variant="secondary" onClick={() => act.mutate({ id: g.id, action: 'decline' })} disabled={act.isPending}>
                      Decline
                    </Button>
                  </>
                )}
                {g.status === 'Active' && (
                  <Button variant="danger" onClick={() => act.mutate({ id: g.id, action: 'revoke' })} disabled={act.isPending}>
                    End access now
                  </Button>
                )}
              </div>
            </li>
          ))}
        </ul>
      )}
    </Card>
  )
}
