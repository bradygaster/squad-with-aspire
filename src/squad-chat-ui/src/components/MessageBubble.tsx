import type { CSSProperties } from 'react'
import type { SquadMessage } from '../types'

interface MessageBubbleProps {
  message: SquadMessage;
  accentColor: string;
}

export function MessageBubble({ message, accentColor }: MessageBubbleProps) {
  const isUserMessage = message.from === 'user'
  const timestamp = new Intl.DateTimeFormat(undefined, {
    hour: 'numeric',
    minute: '2-digit',
  }).format(new Date(message.timestamp))

  const bubbleStyle = {
    '--accent-color': accentColor,
  } as CSSProperties

  const badgeStyle = {
    color: accentColor,
    borderColor: `${accentColor}55`,
    backgroundColor: `${accentColor}1a`,
  } as CSSProperties

  return (
    <div className={`message-row ${isUserMessage ? 'message-row--user' : 'message-row--squad'}`}>
      <article
        className={`message-bubble ${isUserMessage ? 'message-bubble--user' : 'message-bubble--squad'}`}
        style={bubbleStyle}
      >
        <div className="message-bubble__meta">
          <span className="message-badge" style={badgeStyle}>
            {isUserMessage ? 'You' : message.from}
          </span>
          <time className="message-time" dateTime={message.timestamp}>
            {timestamp}
          </time>
        </div>
        {message.subject !== 'chat' ? (
          <div className="message-subject">{message.subject}</div>
        ) : null}
        <p className="message-body">{message.body}</p>
      </article>
    </div>
  )
}
