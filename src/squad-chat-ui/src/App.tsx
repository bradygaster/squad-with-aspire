import { useCallback, useEffect, useMemo, useState } from 'react'
import './App.css'
import { clearMessages as clearMessagesApi, sendMessage } from './api'
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

const DIRECT_SQUAD_PATTERN = new RegExp(
  `^@(${KNOWN_SQUADS.map((squad) => squad.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')).join('|')})\\b\\s*(.*)$`,
  'i',
)

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

function App() {
  const [targetRepo, setTargetRepo] = useState<string | null>(null)
  const {
    messages: streamedMessages,
    clearMessages: clearStreamedMessages,
  } = useMessageStream()
  const [sentMessages, setSentMessages] = useState<SquadMessage[]>([])
  const [isSending, setIsSending] = useState(false)
  const [errorMessage, setErrorMessage] = useState<string | null>(null)
  const [now, setNow] = useState(() => Date.now())

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

  const messages = useMemo(
    () =>
      mergeMessages([...streamedMessages, ...sentMessages]).filter((msg) => {
        // Show messages FROM the user
        if (msg.from === 'user') return true
        // Show coordinator's acknowledgment (TO user)
        if (msg.from === 'coordinator' && msg.to === 'user') return true
        // Show squad replies (TO user or TO coordinator, i.e. replies up the chain)
        if (KNOWN_SQUADS.includes(msg.from as (typeof KNOWN_SQUADS)[number])) return true
        // Hide coordinator-to-squad routing (internal dispatch)
        return false
      }),
    [sentMessages, streamedMessages],
  )

  const squads = useMemo<Squad[]>(() => {
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
  }, [messages, now])

  const handleSend = async (body: string) => {
    setIsSending(true)
    setErrorMessage(null)

    try {
      const trimmedBody = body.trim()

      if (trimmedBody.startsWith('/clear')) {
        await clearMessagesApi()
        clearStreamedMessages()
        setSentMessages([])
        return
      }

      if (trimmedBody.startsWith('/squads')) {
        const squadsMessage = createLocalMessage(
          'coordinator',
          'user',
          'squads',
          `Available squads:\n${KNOWN_SQUADS.map((squad) => `- ${squad}`).join('\n')}`,
        )
        setSentMessages((currentMessages) =>
          mergeMessages([...currentMessages, squadsMessage]),
        )
        return
      }

      let destination = 'coordinator'
      let outboundBody = trimmedBody

      if (trimmedBody.startsWith('/status')) {
        outboundBody = 'what is your current status?'
      } else {
        const directMessageMatch = trimmedBody.match(DIRECT_SQUAD_PATTERN)
        if (directMessageMatch) {
          destination = directMessageMatch[1].toLowerCase()
          outboundBody = directMessageMatch[2].trim()
        }
      }

      const createdMessage = await sendMessage('user', destination, 'chat', outboundBody)
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

      <ComposeBar disabled={isSending} onSend={handleSend} squads={KNOWN_SQUADS} />
    </div>
  )
}

export default App
