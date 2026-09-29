# Changelog

All notable changes to this project are documented here.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project
follows [Semantic Versioning](https://semver.org/).

Add your changes under **Unreleased**; `scripts/release.ps1` turns that section into the next version.

## Unreleased

### Added

- Manual rowing from a ship bench or the helm: press **R** to start/stop (or hold, with `HoldToRow`).
- Visible, animated oar (drive, lift and feathered recovery), shown only while rowing and synced to all players.
  The model is reused from the Karve's steering oar, with a procedural fallback.
- The oar rests on the gunwale and its angle follows the real water level (waves included);
  its length adapts to each hull's height, so it fits raft, karve, longship and drakkar.
- Each rower adds a share of the thrust (`PowerPerRower`, default 25%), capped by `MaxRowingPower`
  (1.0 = full sail in a strong tailwind, per ship type), with a smooth ramp up/down.
- Stamina drain of 1 every 10 seconds of rowing (`StaminaDrainAmount`, `StaminaDrainInterval`).
- Rowing stops and the oar disappears when the rower gets up, dies or runs out of stamina.
