import { useEffect, useMemo, useRef, useCallback } from 'react'
import type { SquadMessage } from '../types'
import { MessageBubble } from './MessageBubble'

interface ChatThreadProps {
  messages: SquadMessage[];
  squadColors: Record<string, string>;
  isWaiting: boolean;
}

export function ChatThread({ messages, squadColors, isWaiting }: ChatThreadProps) {
  const containerRef = useRef<HTMLDivElement | null>(null)
  const bottomAnchorRef = useRef<HTMLDivElement | null>(null)
  const isNearBottomRef = useRef(true)

  const sortedMessages = useMemo(
    () =>
      [...messages].sort(
        (left, right) => Date.parse(left.timestamp) - Date.parse(right.timestamp),
      ),
    [messages],
  )

  const handleScroll = useCallback(() => {
    const el = containerRef.current
    if (!el) return
    // "Near bottom" = within 150px of the bottom
    isNearBottomRef.current = el.scrollHeight - el.scrollTop - el.clientHeight < 150
  }, [])

  useEffect(() => {
    // Only auto-scroll if the user is already near the bottom
    if (isNearBottomRef.current) {
      bottomAnchorRef.current?.scrollIntoView({ behavior: 'smooth', block: 'end' })
    }
  }, [sortedMessages, isWaiting])

  return (
    <section className="chat-thread" ref={containerRef} onScroll={handleScroll} aria-label="Conversation thread">
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
            targetColor={squadColors[message.to]}
          />
        ))
      )}
      {isWaiting ? (
        <div className="thinking-indicator" role="status" aria-live="polite">
          <div className="thinking-indicator__dots" aria-hidden="true">
            <span className="thinking-indicator__dot" />
            <span className="thinking-indicator__dot" />
            <span className="thinking-indicator__dot" />
          </div>
          <span>Squads are thinking...</span>
        </div>
      ) : null}
      <div ref={bottomAnchorRef} />
    </section>
  )
}
