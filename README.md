# Strength of Field

A Zeepkist BepInEx plugin that calculates the lobby's Strength of Field (SOF)
from the COTD ELO rankings.

## Commands

- `/sof` — local message, detailed breakdown (rated / unrated / total).
- `!sof` — chat broadcast, short form.

The SOF score is the top-10 average ELO of players currently in the lobby,
normalized so that 100 represents a top-tier field.

## Requirements

- Zeepkist
- BepInEx 5
- ZeepSDK

## Installation

Install via Modkist. Dependencies are handled automatically.

## Data source

ELO data is pulled from the public COTD rankings repo:
https://github.com/Aizpunr/Zeepkist-COTD-Elo-Rankings

## Status

Alpha. Expect rough edges.

## License

MIT. See LICENSE.
