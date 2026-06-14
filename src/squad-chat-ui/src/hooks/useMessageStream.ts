import { useEffect, useState } from 'react'
import { API_BASE_URL } from '../api'
import type { SquadMessage } from '../types'

const STREAM_PATH = '/api/messages/stream?squad=*'
const RECONNECT_DELAY_MS = 2000

function mergeMessages(
  currentMessages: SquadMessage[],
  incomingMessages: SquadMessage[],
): SquadMessage[] {
  const messagesById = new Map(currentMessages.map((message) => [message.id, message]))

  incomingMessages.forEach((message) => {
    messagesById.set(message.id, message)
  })

  return Array.from(messagesById.values()).sort(
    (left, right) => Date.parse(left.timestamp) - Date.parse(right.timestamp),
  )
}

export function useMessageStream() {
  const [messages, setMessages] = useState<SquadMessage[]>([])

  useEffect(() => {
    let eventSource: EventSource | null = null
    let reconnectTimer: number | null = null
    let isDisposed = false

    const connect = () => {
      if (isDisposed) {
        return
      }

      const streamUrl = new URL(STREAM_PATH, API_BASE_URL)
      eventSource = new EventSource(streamUrl.toString())

      eventSource.onmessage = (event) => {
        try {
          const payload = JSON.parse(event.data) as
            | SquadMessage
            | SquadMessage[]
            | { message: SquadMessage }

          const incomingMessages = Array.isArray(payload)
            ? payload
            : 'message' in payload
              ? [payload.message]
              : [payload]

          setMessages((currentMessages) =>
            mergeMessages(currentMessages, incomingMessages),
          )
        } catch (error) {
          console.error('Failed to parse message stream payload.', error)
        }
      }

      eventSource.onerror = () => {
        eventSource?.close()
        if (!isDisposed) {
          reconnectTimer = window.setTimeout(connect, RECONNECT_DELAY_MS)
        }
      }
    }

    connect()

    return () => {
      isDisposed = true
      eventSource?.close()
      if (reconnectTimer !== null) {
        window.clearTimeout(reconnectTimer)
      }
    }
  }, [])

  return { messages }
}
