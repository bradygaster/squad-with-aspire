import { useCallback, useEffect, useMemo, useState } from 'react'
import './App.css'
import { clearMessages as clearMessagesApi, getSquads, resetAllState, sendMessage } from './api'
import { ChatThread } from './components/ChatThread'
import { ComposeBar } from './components/ComposeBar'
import { RepoPickerModal } from './components/RepoPickerModal'
import { SquadPresenceBar } from './components/SquadPresenceBar'
import { useMessageStream } from './hooks/useMessageStream'
import type { Squad, SquadMessage } from './types'

// Generate a color from a squad name deterministically
const PALETTE = ['#f4a261', '#5ad1e6', '#7bd88f', '#ff7aa2', '#e6c75a', '#d97af5', '#7af5b8', '#f28b82', '#81c995', '#aecbfa']

function squadColor(_name: string, index: number): string {
  return PALETTE[index % PALETTE.length]
}

function buildSquadPattern(squads: string[]): RegExp {
  if (squads.length === 0) return /(?!)/ // never matches
  const escaped = squads.map((s) => s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')).join('|')
  return new RegExp(`^@(${escaped})\\b\\s*(.*)$`, 'i')
}

function mergeMessages(messages: SquadMessage[]): SquadMessage[] {
  const messagesById = new Map(messages.map((message) => [message.id, message]))
  return Array.from(messagesById.values()).sort(
    (left, right) => Date.parse(left.timestamp) - Date.parse(right.timestamp),
  )
}

function createLocalMessage(
  from: string,
  to: string,
  subject: string,
  body: string,
): SquadMessage {
  return {
    id: crypto.randomUUID(),
    from,
    to,
    subject,
    body,
    timestamp: new Date().toISOString(),
    isRead: true,
  }
}

function ChatApp() {
  const [knownSquads, setKnownSquads] = useState<string[]>([])
  const [targetRepo, setTargetRepo] = useState<string | null>(null)
  const [sentMessages, setSentMessages] = useState<SquadMessage[]>([])
  const [isSending, setIsSending] = useState(false)
  const [waitingForResponse, setWaitingForResponse] = useState(false)
  const [errorMessage, setErrorMessage] = useState<string | null>(null)
  const [now, setNow] = useState(() => Date.now())

  // Fetch squad list from API on mount
  useEffect(() => {
    getSquads()
      .then(setKnownSquads)
      .catch((err) => console.warn('Failed to fetch squads, using empty list:', err))
  }, [])

  const squadColors = useMemo(() => {
    const colors: Record<string, string> = { user: '#7aa2ff', coordinator: '#9b87f5' }
    for (let i = 0; i < knownSquads.length; i++) {
      colors[knownSquads[i]] = squadColor(knownSquads[i], i)
    }
    return colors
  }, [knownSquads])

  const directSquadPattern = useMemo(() => buildSquadPattern(knownSquads), [knownSquads])

  const handleStreamMessages = useCallback((incomingMessages: SquadMessage[]) => {
    if (incomingMessages.some((message) => knownSquads.includes(message.from))) {
      setWaitingForResponse(false)
    }
  }, [knownSquads])
  const {
    messages: streamedMessages,
    clearMessages: clearStreamedMessages,
  } = useMessageStream(handleStreamMessages)

  useEffect(() => {
    const timer = window.setInterval(() => {
      setNow(Date.now())
    }, 60_000)

    return () => {
      window.clearInterval(timer)
    }
  }, [])

  const handleRepoSelected = useCallback((repo: string) => {
    setTargetRepo(repo)
  }, [])

  const [isChangingRepo, setIsChangingRepo] = useState(false)

  const handleChangeRepo = useCallback(async () => {
    const confirmed = window.confirm(
      'Change target repo?\n\nThis will permanently delete all messages, every squad\u2019s accumulated knowledge, and reset all in-progress squad sessions. You\u2019ll be prompted to pick a new repo afterwards.',
    )
    if (!confirmed) return

    setIsChangingRepo(true)
    setErrorMessage(null)

    try {
      await resetAllState()
      clearStreamedMessages()
      setSentMessages([])
      setWaitingForResponse(false)
      setTargetRepo(null)
    } catch (err) {
      const detail = err instanceof Error ? err.message : 'Unknown error'
      setErrorMessage(`Failed to change repo. ${detail}`)
    } finally {
      setIsChangingRepo(false)
    }
  }, [clearStreamedMessages])

  const messages = useMemo(
    () =>
      mergeMessages([...streamedMessages, ...sentMessages]).filter((msg) => {
        // Show messages FROM the user
        if (msg.from === 'user') return true
        // Show coordinator's acknowledgment (TO user)
        if (msg.from === 'coordinator' && msg.to === 'user') return true
        // Show squad replies (TO user or TO coordinator, i.e. replies up the chain)
        if (knownSquads.includes(msg.from)) return true
        // Hide coordinator-to-squad routing (internal dispatch)
        return false
      }),
    [sentMessages, streamedMessages],
  )

  const squads = useMemo<Squad[]>(() => {
    return knownSquads.map((name) => {
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
        color: squadColors[name],
        isActive,
        unreadCount,
      }
    })
  }, [messages, now, knownSquads, squadColors])

  const handleSend = async (body: string) => {
    setIsSending(true)
    setErrorMessage(null)

    try {
      const trimmedBody = body.trim()

      if (trimmedBody.startsWith('/clear')) {
        await clearMessagesApi()
        clearStreamedMessages()
        setSentMessages([])
        setWaitingForResponse(false)
        return
      }

      if (trimmedBody.startsWith('/squads')) {
        const squadsMessage = createLocalMessage(
          'coordinator',
          'user',
          'squads',
          `Available squads:\n${knownSquads.map((squad) => `- ${squad}`).join('\n')}`,
        )
        setSentMessages((currentMessages) =>
          mergeMessages([...currentMessages, squadsMessage]),
        )
        setWaitingForResponse(false)
        return
      }

      let destination = 'coordinator'
      let outboundBody = trimmedBody

      if (trimmedBody.startsWith('/status')) {
        outboundBody = 'what is your current status?'
      } else {
        const directMessageMatch = trimmedBody.match(directSquadPattern)
        if (directMessageMatch) {
          destination = directMessageMatch[1].toLowerCase()
          outboundBody = directMessageMatch[2].trim()
        }
      }

      setWaitingForResponse(true)
      const createdMessage = await sendMessage('user', destination, 'chat', outboundBody)
      setSentMessages((currentMessages) =>
        mergeMessages([...currentMessages, createdMessage]),
      )
    } catch (error) {
      setWaitingForResponse(false)
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
        <div className="app-header-meta">
          <p className="app-subtitle">
            {targetRepo
              ? `Working on ${targetRepo}`
              : 'Coordinate a multi-agent team from one live thread with streaming updates.'}
          </p>
          {targetRepo && (
            <button
              type="button"
              className="change-repo-button"
              onClick={handleChangeRepo}
              disabled={isChangingRepo}
              title="Reset everything (messages, knowledge, sessions) and pick a new target repo"
            >
              {isChangingRepo ? 'Resetting…' : 'Change repo'}
            </button>
          )}
        </div>
      </header>

      <SquadPresenceBar squads={squads} />

      <main className="app-main">
        <ChatThread
          isWaiting={waitingForResponse}
          messages={messages}
          squadColors={squadColors}
        />
      </main>

      {errorMessage ? <div className="app-error">{errorMessage}</div> : null}

      <ComposeBar disabled={isSending} onSend={handleSend} squads={knownSquads} />
    </div>
  )
}

function App() {
  return <ChatApp />
}

export default App
