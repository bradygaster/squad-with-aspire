import { useMemo, useRef, useState } from 'react'
import {
  CommandAutocomplete,
  type AutocompleteOption,
} from './CommandAutocomplete'

interface ComposeBarProps {
  disabled?: boolean
  onAutocompleteSelect?: (selection: {
    trigger: '/' | '@'
    value: string
  }) => void
  onSend: (body: string) => Promise<void>
  squads: readonly string[]
}

interface AutocompleteMatch {
  trigger: '/' | '@'
  query: string
  rangeStart: number
  rangeEnd: number
}

const COMMAND_OPTIONS: readonly AutocompleteOption[] = [
  { value: 'clear', description: 'Clear message history' },
  { value: 'status', description: 'Ask all squads for status' },
  { value: 'squads', description: 'List available squads' },
]

function getAutocompleteMatch(value: string, cursor: number): AutocompleteMatch | null {
  const beforeCursor = value.slice(0, cursor)

  const commandMatch = beforeCursor.match(/^\/([^\s]*)$/)
  if (commandMatch) {
    return {
      trigger: '/',
      query: commandMatch[1],
      rangeStart: 0,
      rangeEnd: cursor,
    }
  }

  const tokenStart = Math.max(
    beforeCursor.lastIndexOf(' '),
    beforeCursor.lastIndexOf('\n'),
    beforeCursor.lastIndexOf('\t'),
  ) + 1
  const token = beforeCursor.slice(tokenStart)

  if (token.startsWith('@')) {
    return {
      trigger: '@',
      query: token.slice(1),
      rangeStart: tokenStart,
      rangeEnd: cursor,
    }
  }

  return null
}

export function ComposeBar({
  disabled = false,
  onAutocompleteSelect,
  onSend,
  squads,
}: ComposeBarProps) {
  const [draft, setDraft] = useState('')
  const [autocomplete, setAutocomplete] = useState<AutocompleteMatch | null>(null)
  const [activeIndex, setActiveIndex] = useState(0)
  const textareaRef = useRef<HTMLTextAreaElement | null>(null)

  const squadOptions = useMemo<AutocompleteOption[]>(
    () => squads.map((squad) => ({ value: squad, description: 'Send directly to this squad' })),
    [squads],
  )

  const filteredOptions = useMemo(() => {
    if (!autocomplete) {
      return []
    }

    const options = autocomplete.trigger === '/' ? COMMAND_OPTIONS : squadOptions
    const query = autocomplete.query.toLowerCase()

    return options.filter((option) => option.value.toLowerCase().startsWith(query))
  }, [autocomplete, squadOptions])

  const updateAutocomplete = (value: string, cursor: number) => {
    const nextAutocomplete = getAutocompleteMatch(value, cursor)

    if (
      autocomplete?.trigger !== nextAutocomplete?.trigger ||
      autocomplete?.query !== nextAutocomplete?.query
    ) {
      setActiveIndex(0)
    }

    setAutocomplete(nextAutocomplete)
  }

  const submitMessage = async () => {
    const nextDraft = draft.trim()
    if (!nextDraft || disabled) {
      return
    }

    await onSend(nextDraft)
    setDraft('')
    setAutocomplete(null)
    setActiveIndex(0)
  }

  const applyAutocompleteOption = (option: AutocompleteOption) => {
    if (!autocomplete) {
      return
    }

    const insertedValue =
      autocomplete.trigger === '/' ? `/${option.value}` : `@${option.value} `
    const nextDraft =
      draft.slice(0, autocomplete.rangeStart) +
      insertedValue +
      draft.slice(autocomplete.rangeEnd)
    const nextCursor = autocomplete.rangeStart + insertedValue.length

    setDraft(nextDraft)
    setAutocomplete(null)
    setActiveIndex(0)
    onAutocompleteSelect?.({ trigger: autocomplete.trigger, value: option.value })

    requestAnimationFrame(() => {
      textareaRef.current?.focus()
      textareaRef.current?.setSelectionRange(nextCursor, nextCursor)
    })
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
        {autocomplete ? (
          <CommandAutocomplete
            activeIndex={activeIndex}
            onHighlight={setActiveIndex}
            onSelect={applyAutocompleteOption}
            options={filteredOptions}
            trigger={autocomplete.trigger}
          />
        ) : null}
        <textarea
          ref={textareaRef}
          id="chat-draft"
          className="compose-bar__input"
          placeholder="Message the coordinator squad..."
          rows={1}
          value={draft}
          onChange={(event) => {
            const nextValue = event.target.value
            setDraft(nextValue)
            updateAutocomplete(nextValue, event.target.selectionStart ?? nextValue.length)
          }}
          onClick={(event) => {
            updateAutocomplete(
              event.currentTarget.value,
              event.currentTarget.selectionStart ?? event.currentTarget.value.length,
            )
          }}
          onKeyDown={(event) => {
            if (autocomplete && filteredOptions.length > 0) {
              if (event.key === 'ArrowDown') {
                event.preventDefault()
                setActiveIndex((currentIndex) => (currentIndex + 1) % filteredOptions.length)
                return
              }

              if (event.key === 'ArrowUp') {
                event.preventDefault()
                setActiveIndex(
                  (currentIndex) =>
                    (currentIndex - 1 + filteredOptions.length) % filteredOptions.length,
                )
                return
              }

              if (event.key === 'Enter' && !event.shiftKey) {
                event.preventDefault()
                applyAutocompleteOption(filteredOptions[activeIndex] ?? filteredOptions[0])
                return
              }
            }

            if (autocomplete && event.key === 'Escape') {
              event.preventDefault()
              setAutocomplete(null)
              setActiveIndex(0)
              return
            }

            if (event.key === 'Enter' && !event.shiftKey) {
              event.preventDefault()
              void submitMessage()
            }
          }}
          onKeyUp={(event) => {
            updateAutocomplete(
              event.currentTarget.value,
              event.currentTarget.selectionStart ?? event.currentTarget.value.length,
            )
          }}
          onSelect={(event) => {
            updateAutocomplete(
              event.currentTarget.value,
              event.currentTarget.selectionStart ?? event.currentTarget.value.length,
            )
          }}
        />
      </label>
      <button className="compose-bar__button" type="submit" disabled={disabled || !draft.trim()}>
        {disabled ? 'Sending…' : 'Send'}
      </button>
    </form>
  )
}
