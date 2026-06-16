export interface AutocompleteOption {
  value: string
  description: string
}

interface CommandAutocompleteProps {
  activeIndex: number
  onHighlight: (index: number) => void
  onSelect: (option: AutocompleteOption) => void
  options: readonly AutocompleteOption[]
  trigger: '/' | '@'
}

export function CommandAutocomplete({
  activeIndex,
  onHighlight,
  onSelect,
  options,
  trigger,
}: CommandAutocompleteProps) {
  if (options.length === 0) {
    return null
  }

  return (
    <div
      className="command-autocomplete"
      role="listbox"
      aria-label={trigger === '/' ? 'Command suggestions' : 'Squad suggestions'}
    >
      {options.map((option, index) => {
        const isActive = index === activeIndex

        return (
          <button
            key={`${trigger}${option.value}`}
            className={`command-autocomplete__item ${isActive ? 'command-autocomplete__item--active' : ''}`}
            type="button"
            role="option"
            aria-selected={isActive}
            onMouseEnter={() => onHighlight(index)}
            onClick={() => onSelect(option)}
          >
            <span className="command-autocomplete__value">{trigger}{option.value}</span>
            <span className="command-autocomplete__description">{option.description}</span>
          </button>
        )
      })}
    </div>
  )
}
