# Squad Decisions

## Active Decisions

### Decision 001: EXPANDED Game Lineup (2026-06-14, updated)

**Status:** APPROVED — building ALL games

All four squads converged on the same 5 core games. User has greenlit expanding beyond 5 to include every classic that fits our constraints. Here's the **full arcade roster** for brutalgames.online:

| # | Game | Core Mechanic | Complexity |
|---|------|--------------|------------|
| 1 | **Snake** | Directional grid movement, eat & grow | ⭐ |
| 2 | **Breakout** | Paddle + ball, brick destruction | ⭐ |
| 3 | **Space Invaders** | Fixed shooter vs. descending alien grid | ⭐⭐ |
| 4 | **Asteroids** | Thrust/rotate ship, split rocks | ⭐⭐ |
| 5 | **Missile Command** | Point-and-click missile defense | ⭐⭐ |
| 6 | **Pong** | Two-paddle competitive/AI rally | ⭐ |
| 7 | **Frogger** | Lane-crossing obstacle avoidance | ⭐⭐ |
| 8 | **Tetris** | Falling block rotation & line clearing | ⭐⭐ |
| 9 | **Centipede** | Shoot segmented enemy snaking through mushroom field | ⭐⭐ |
| 10 | **Pac-Man** | Maze navigation, dot eating, ghost AI | ⭐⭐⭐ |

**Build Order (simplest → most complex):**
1. Snake (grid movement warmup)
2. Pong (basic physics, AI opponent)
3. Breakout (collision + brick grid)
4. Space Invaders (wave AI + projectiles)
5. Frogger (lane timing + sprite animation)
6. Tetris (rotation math + line detection)
7. Centipede (segment AI + mushroom grid)
8. Asteroids (thrust physics, screen wrap, splitting)
9. Missile Command (mouse targeting, explosion radius)
10. Pac-Man (pathfinding AI, maze generation)

**Design Notes for Site-Design Squad:**
- All games should use a shared neo-brutalist color palette (high contrast, bold borders, monospace fonts)
- Canvas size: 800×600 default, responsive scaling
- Each game gets a brutalist "cabinet" frame in the landing page

**Technical Notes for Game-Dev Squad:**
- Pure HTML/CSS/JS, zero dependencies, single `index.html` per game
- Canvas 2D API for rendering
- RequestAnimationFrame game loop
- Keyboard input (+ mouse for Missile Command)
- Local high score via localStorage

## Governance

- All meaningful changes require team consensus
- Document architectural decisions here
- Keep history focused on work, decisions focused on direction
