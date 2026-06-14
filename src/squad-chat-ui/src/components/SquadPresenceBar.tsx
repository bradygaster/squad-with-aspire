import type { Squad } from '../types'

interface SquadPresenceBarProps {
  squads: Squad[];
}

export function SquadPresenceBar({ squads }: SquadPresenceBarProps) {
  return (
    <section className="presence-bar" aria-label="Squad presence">
      {squads.map((squad) => (
        <div
          key={squad.name}
          className={`presence-pill ${squad.isActive ? 'presence-pill--active' : ''}`}
          style={{
            borderColor: `${squad.color}55`,
            backgroundColor: `${squad.color}14`,
            color: squad.color,
          }}
        >
          <span className="presence-pill__dot" style={{ backgroundColor: squad.color }} />
          <span className="presence-pill__name">{squad.name}</span>
          {squad.unreadCount > 0 ? (
            <span className="presence-pill__count">{squad.unreadCount}</span>
          ) : null}
        </div>
      ))}
    </section>
  )
}
