import { useSearchParams } from 'react-router-dom'

/** The open request, kept in the address so the back button closes it. */
export function useOpenRequest(): [string | null, (id: string | null) => void] {
  const [params, setParams] = useSearchParams()
  return [params.get('request'), (id) => setParams(id ? { request: id } : {})]
}
