# Strength of Field

A Zeepkist BepInEx plugin that shows the lobby's Strength of Field (SOF),
computed from a cross-comp ELO ranking that aggregates five Zeepkist
competitions: COTD/COTW, ZSL, PCDJ, TyO, and Kerki.

## Commands

- `/sof` — local message, detailed breakdown (rated / unrated / total).
- `!sof` — chat broadcast, short form.

The SOF score is the top-10 average ELO of players currently in the lobby,
normalized so that 100 ≈ the strongest top-10 the scene has ever held at
one moment. Real lobbies cap around ~95%, since the global top-10 never
all show up to the same event.

## Requirements

- Zeepkist
- BepInEx 5
- ZeepSDK

## Installation

Install via Modkist. Dependencies are handled automatically.

## Live page

Browse the full ranking, per-event log, and a time-travel history slider at:

**https://aizpunr.github.io/Zeepkist-Strength-of-Field/**

## Data source

The mod fetches `elo_pool.json` (in this repo) at runtime. It's regenerated
from a chronological pipeline: Glicko-2 warmup over the first 10 events,
then SOF-weighted pairwise ELO over every subsequent event across the five
tracked comps. Players who have never appeared in any tracked comp fall
back to a regression on their GTR rank.

## Status

Beta. 1.0.0-beta.1 is pending Modkist review.

## License

MIT. See LICENSE.
