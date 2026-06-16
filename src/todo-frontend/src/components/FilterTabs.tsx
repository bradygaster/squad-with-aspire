import type { FilterMode } from '../types';

interface FilterTabsProps {
  current: FilterMode;
  onChange: (mode: FilterMode) => void;
  activeCount: number;
}

const filters: { label: string; value: FilterMode }[] = [
  { label: 'All', value: 'all' },
  { label: 'Active', value: 'active' },
  { label: 'Completed', value: 'completed' },
];

export function FilterTabs({ current, onChange, activeCount }: FilterTabsProps) {
  return (
    <div className="filter-tabs">
      <span className="item-count">
        {activeCount} {activeCount === 1 ? 'item' : 'items'} left
      </span>
      <div className="tabs" role="tablist">
        {filters.map((f) => (
          <button
            key={f.value}
            role="tab"
            aria-selected={current === f.value}
            className={current === f.value ? 'active' : ''}
            onClick={() => onChange(f.value)}
          >
            {f.label}
          </button>
        ))}
      </div>
    </div>
  );
}
