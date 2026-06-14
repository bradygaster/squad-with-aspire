import { useEffect, useMemo, useRef } from 'react'
import type { SquadMessage } from '../types'
import { MessageBubble } from './MessageBubble'

interface ChatThreadProps {
  messages: SquadMessage[];
  squadColors: Record<string, string>;
}

export function ChatThread({ messages, squadColors }: ChatThreadProps) {
  const bottomAnchorRef = useRef<HTMLDivElement | null>(null)

  const sortedMessages = useMemo(
    () =>
      [...messages].sort(
        (left, right) => Date.parse(left.timestamp) - Date.parse(right.timestamp),
      ),
    [messages],
  )

  useEffect(() => {
    bottomAnchorRef.current?.scrollIntoView({ behavior: 'smooth', block: 'end' })
  }, [sortedMessages])

  return (
    <section className="chat-thread" aria-label="Conversation thread">
      {sortedMessages.length === 0 ? (
        <div className="chat-thread__empty">
          <h2>Squads are standing by</h2>
          <p>Start the conversation and the coordinator will route work to the right squad.</p>
        </div>
      ) : (
        sortedMessages.map((message) => (
          <MessageBubble
            key={message.id}
            message={message}
            accentColor={squadColors[message.from] ?? squadColors[message.to] ?? '#6ea8fe'}
          />
        ))
      )}
      <div ref={bottomAnchorRef} />
    </section>
  )
}
