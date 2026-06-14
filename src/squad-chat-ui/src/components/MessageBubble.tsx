import type { CSSProperties } from 'react'
import type { SquadMessage } from '../types'

interface MessageBubbleProps {
  message: SquadMessage;
  accentColor: string;
  targetColor?: string;
}

export function MessageBubble({ message, accentColor, targetColor }: MessageBubbleProps) {
  const isUserMessage = message.from === 'user'
  const directTarget =
    isUserMessage && message.to !== 'coordinator' && message.to !== 'user'
      ? message.to
      : null
  const parsedDate = new Date(message.timestamp)
  const timestamp = Number.isNaN(parsedDate.getTime())
    ? ''
    : new Intl.DateTimeFormat(undefined, {
        hour: 'numeric',
        minute: '2-digit',
      }).format(parsedDate)

  const bubbleStyle = {
    '--accent-color': accentColor,
  } as CSSProperties

  const badgeStyle = {
    color: accentColor,
    borderColor: `${accentColor}55`,
    backgroundColor: `${accentColor}1a`,
  } as CSSProperties

  const directTagStyle = directTarget
    ? ({
        color: targetColor ?? accentColor,
        borderColor: `${targetColor ?? accentColor}55`,
        backgroundColor: `${targetColor ?? accentColor}1a`,
      } as CSSProperties)
    : undefined

  return (
    <div className={`message-row ${isUserMessage ? 'message-row--user' : 'message-row--squad'}`}>
      <article
        className={`message-bubble ${isUserMessage ? 'message-bubble--user' : 'message-bubble--squad'}`}
        style={bubbleStyle}
      >
        <div className="message-bubble__meta">
          <div className="message-bubble__meta-main">
            <span className="message-badge" style={badgeStyle}>
              {isUserMessage ? 'You' : message.from}
            </span>
            {directTarget ? (
              <span className="message-direct-tag" style={directTagStyle}>
                → {directTarget}
              </span>
            ) : null}
          </div>
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
