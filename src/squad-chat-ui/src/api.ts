import type { SquadMessage } from './types'

const API_BASE_URL = import.meta.env.VITE_API_URL ?? 'http://localhost:5000'

const buildUrl = (path: string) => new URL(path, API_BASE_URL).toString()

async function parseJsonResponse<T>(response: Response): Promise<T> {
  if (!response.ok) {
    throw new Error(`Request failed with status ${response.status}`)
  }

  return (await response.json()) as T
}

export async function sendMessage(
  from: string,
  to: string,
  subject: string,
  body: string,
): Promise<SquadMessage> {
  const response = await fetch(buildUrl('/api/messages'), {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
    },
    body: JSON.stringify({ from, to, subject, body }),
  })

  return parseJsonResponse<SquadMessage>(response)
}

export async function getInbox(
  squadName: string,
  unreadOnly = false,
): Promise<SquadMessage[]> {
  const inboxUrl = new URL(
    buildUrl(`/api/messages/${encodeURIComponent(squadName)}/inbox`),
  )

  if (unreadOnly) {
    inboxUrl.searchParams.set('unreadOnly', 'true')
  }

  const response = await fetch(inboxUrl.toString())
  return parseJsonResponse<SquadMessage[]>(response)
}

export async function markRead(messageId: string): Promise<void> {
  const response = await fetch(buildUrl(`/api/messages/${encodeURIComponent(messageId)}/read`), {
    method: 'POST',
  })

  if (!response.ok) {
    throw new Error(`Request failed with status ${response.status}`)
  }
}

export { API_BASE_URL }
