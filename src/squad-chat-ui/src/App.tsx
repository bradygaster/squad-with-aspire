import { useCallback, useMemo, useState } from 'react'
import './App.css'
import { sendMessage } from './api'
import { ChatThread } from './components/ChatThread'
import { ComposeBar } from './components/ComposeBar'
import { RepoPickerModal } from './components/RepoPickerModal'
import { SquadPresenceBar } from './components/SquadPresenceBar'
import { useMessageStream } from './hooks/useMessageStream'
import type { Squad, SquadMessage } from './types'

const SQUAD_COLORS: Record<string, string> = {
  user: '#7aa2ff',
  coordinator: '#9b87f5',
  'research-and-ideation-squad': '#f4a261',
  'site-design-squad': '#5ad1e6',
  'game-development-squad': '#7bd88f',
  'qa-squad': '#ff7aa2',
}

const KNOWN_SQUADS = [
  'research-and-ideation-squad',
  'site-design-squad',
  'game-development-squad',
  'qa-squad',
] as const

function mergeMessages(messages: SquadMessage[]): SquadMessage[] {
  const messagesById = new Map(messages.map((message) => [message.id, message]))
  return Array.from(messagesById.values()).sort(
    (left, right) => Date.parse(left.timestamp) - Date.parse(right.timestamp),
  )
}

function App() {
  const [targetRepo, setTargetRepo] = useState<string | null>(null)
  const { messages: streamedMessages } = useMessageStream()
  const [sentMessages, setSentMessages] = useState<SquadMessage[]>([])
  const [isSending, setIsSending] = useState(false)
  const [errorMessage, setErrorMessage] = useState<string | null>(null)

  const handleRepoSelected = useCallback((repo: string) => {
    setTargetRepo(repo)
  }, [])

  const messages = useMemo(
    () => mergeMessages([...streamedMessages, ...sentMessages]),
    [sentMessages, streamedMessages],
  )

  const squads = useMemo<Squad[]>(() => {
    const now = Date.now()

    return KNOWN_SQUADS.map((name) => {
      const unreadCount = messages.filter(
        (message) => message.from === name && !message.isRead,
      ).length
      const isActive = messages.some((message) => {
        if (message.from !== name && message.to !== name) {
          return false
        }

        const timestamp = Date.parse(message.timestamp)
        return !Number.isNaN(timestamp) && now - timestamp < 5 * 60 * 1000
      })

      return {
        name,
        color: SQUAD_COLORS[name],
        isActive,
        unreadCount,
      }
    })
  }, [messages])

  const handleSend = async (body: string) => {
    setIsSending(true)
    setErrorMessage(null)

    try {
      const createdMessage = await sendMessage('user', 'coordinator', 'chat', body)
      setSentMessages((currentMessages) =>
        mergeMessages([...currentMessages, createdMessage]),
      )
    } catch (error) {
      const detail = error instanceof Error ? error.message : 'Unknown error'
      setErrorMessage(`Unable to send message. ${detail}`)
    } finally {
      setIsSending(false)
    }
  }

  return (
    <div className="app-shell">
      {!targetRepo && <RepoPickerModal onRepoSelected={handleRepoSelected} />}

      <header className="app-header">
        <div>
          <p className="app-eyebrow">Inter-squad messaging</p>
          <h1>Squad chat</h1>
        </div>
        <p className="app-subtitle">
          {targetRepo
            ? `Working on ${targetRepo}`
            : 'Coordinate a multi-agent team from one live thread with streaming updates.'}
        </p>
      </header>

      <SquadPresenceBar squads={squads} />

      <main className="app-main">
        <ChatThread messages={messages} squadColors={SQUAD_COLORS} />
      </main>

      {errorMessage ? <div className="app-error">{errorMessage}</div> : null}

      <ComposeBar disabled={isSending} onSend={handleSend} />
    </div>
  )
}

export default App
