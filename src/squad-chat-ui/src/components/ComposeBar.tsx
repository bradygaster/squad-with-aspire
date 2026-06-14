import { useState } from 'react'

interface ComposeBarProps {
  disabled?: boolean;
  onSend: (body: string) => Promise<void>;
}

export function ComposeBar({ disabled = false, onSend }: ComposeBarProps) {
  const [draft, setDraft] = useState('')

  const submitMessage = async () => {
    const nextDraft = draft.trim()
    if (!nextDraft || disabled) {
      return
    }

    await onSend(nextDraft)
    setDraft('')
  }

  return (
    <form
      className="compose-bar"
      onSubmit={(event) => {
        event.preventDefault()
        void submitMessage()
      }}
    >
      <label className="compose-bar__field" htmlFor="chat-draft">
        <span className="sr-only">Message the coordinator</span>
        <textarea
          id="chat-draft"
          className="compose-bar__input"
          placeholder="Message the coordinator squad..."
          rows={1}
          value={draft}
          onChange={(event) => {
            setDraft(event.target.value)
          }}
          onKeyDown={(event) => {
            if (event.key === 'Enter' && !event.shiftKey) {
              event.preventDefault()
              void submitMessage()
            }
          }}
        />
      </label>
      <button className="compose-bar__button" type="submit" disabled={disabled || !draft.trim()}>
        {disabled ? 'Sending…' : 'Send'}
      </button>
    </form>
  )
}
