import { useMemo } from 'react'
import type { SquadMessage } from '../types'
import { MessageBubble } from './MessageBubble'

interface ChatThreadProps {
  messages: SquadMessage[];
  squadColors: Record<string, string>;
  isWaiting: boolean;
}

export function ChatThread({ messages, squadColors, isWaiting }: ChatThreadProps) {
  const sortedMessages = useMemo(
    () =>
      [...messages].sort(
        (left, right) => Date.parse(left.timestamp) - Date.parse(right.timestamp),
      ),
    [messages],
  )

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
    </section>
  )
}
