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
### Decision 001: Initial Game Lineup (2026-06-14)

**Status:** Proposed — circulated to all squads for development

We're recommending **5 retro arcade classics** for brutalgames.online. Each is well-known, simple to implement as a single zero-dependency `index.html` with Canvas 2D, and offers distinct gameplay mechanics:

| # | Game | Core Mechanic | Why It Works |
|---|------|--------------|--------------|
| 1 | **Space Invaders** | Fixed shooter — move left/right, shoot up at descending alien grid | Iconic, simple state machine, great intro game |
| 2 | **Missile Command** | Point-and-click defense — launch missiles to intercept incoming warheads | Trackball/mouse feel, satisfying explosions, escalating difficulty |
| 3 | **Breakout** | Paddle + ball — bounce ball to destroy bricks | Physics-lite, power-ups optional, very satisfying |
| 4 | **Asteroids** | Thrust/rotate ship, shoot drifting rocks that split | Wrap-around screen, vector-style graphics perfect for neo-brutalist theme |
| 5 | **Snake** | Directional movement — eat food, grow longer, don't hit yourself | Simplest to build, addictive, good mobile candidate |

**Build Order Recommendation:**
1. Snake (warmup — simplest mechanics, fastest to ship)
2. Breakout (adds collision/physics)
3. Space Invaders (grid AI + wave progression)
4. Asteroids (rotation, momentum, screen wrap)
5. Missile Command (mouse input, explosion radius, multiple targets)

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
