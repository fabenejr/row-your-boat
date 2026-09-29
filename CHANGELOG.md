# Changelog

All notable changes to this project are documented here.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project
follows [Semantic Versioning](https://semver.org/).

Add your changes under **Unreleased**; `scripts/release.ps1` turns that section into the next version.

## Unreleased

## 2.0.0 - 2026-09-28

### Changed

- Only the crew on the benches can row; the helmsman steers and can't row anymore.
  Ships without benches, like the raft, can no longer be rowed.
- The oar is now the ship's own steering oar (the Karve's on ships without one), held over the gunwale beside
  the rower like a paddle: the blade digs in ahead, pulls back through the water, then lifts out, feathers and
  swings forward. It rests on the gunwale instead of cutting through the hull, its height follows the water level,
  and it is held fixed to the rower, so it no longer drifts away from them.
- `StrokePeriod` is replaced by `StrokeSpeed` (1.0 = one stroke every 1.5 seconds).

### Added

- Rowing pose: the rower holds the oar with both hands, the arms follow it through the stroke, and the torso
  reaches forward at the catch, pulls back through the drive and leans towards the oar. Seen by all players.
- `OarScale` setting (default 0.8) to size the oar relative to the steering oar it is copied from.

## 1.2.0 - 2026-09-28

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
