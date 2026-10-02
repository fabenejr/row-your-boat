# Changelog

All notable changes to this project are documented here.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project
follows [Semantic Versioning](https://semver.org/).

Add your changes under **Unreleased**; `scripts/release.ps1` turns that section into the next version.

## Unreleased

### Added

- The mod now depends on [Jötunn](https://thunderstore.io/c/valheim/p/ValheimModding/Jotunn/). Install it
  alongside BepInExPack Valheim.
- A craftable oar (tier-1 workbench, 6 Fine Wood), based on the Club. Equipping it is now required to row,
  and it works as a real weapon when you're not on a ship.
- Gear shifting: with the oar equipped and seated, rowing starts automatically in neutral; W/S step through
  reverse, neutral and gears 1–3 exactly like the ship's own rudder (minus steering). Each gear has its own
  thrust (`Gear2Multiplier`, `Gear3Multiplier`, `ReverseMultiplier`) and stamina cadence
  (`StaminaDrainIntervalSlow/Half/Full`).
- The blade splashes, with sound, as it hits the water. The effect is the game's own arrow-in-water splash,
  and the new `Splash` setting turns it off.
- The crew rows in time: every oar follows the same beat, taken from the network clock, so all players see
  the same strokes.

### Changed

- Thrust rebalanced for the gears: `PowerPerRower` defaults to `0.08` (was `0.25`), `Gear2Multiplier` to `1.4`
  and `Gear3Multiplier` to `1.8`. Existing config files keep their own values.
- Rowing no longer toggles with a key: it starts and stops with the oar and the bench. The `RowKey` and
  `HoldToRow` settings are **removed**; `StaminaDrainInterval` is renamed `StaminaDrainIntervalSlow`.
- Stamina drain is now a per-stroke cost (like swinging a weapon) instead of a flat drain over time: running
  out withholds that gear's thrust until stamina refills, instead of stopping rowing outright.
- Rowers now leave the bench only with **E** or **Jump**, same as the helm. Previously W/S stood the rower
  up immediately (same vanilla rule that stands anyone up on movement input, which the helm is exempt from
  by being a "doodad controller" — rowers now get the same exemption for movement without becoming one).
- The selected gear is shown with the helm's own arrows (one, two or three up for gears 1–3, one down for
  reverse) around the steering-wheel icon, on the gunwale beside the rower's bench, instead of a message in the center of the
  screen. The `ShowMessage` setting is **removed**.

### Fixed

- The blade now reaches the water and stays in it through the whole pull. The lean of the oar is adjusted every
  frame to the water under the blade, following waves, the ship rolling and pitching, and the swing that used to
  lift the blade out at both ends of the stroke. It leaves and re-enters the water quickly at the ends of the recovery.
- On high hulls, an oar too short to reach the water slides down through the rower's hands instead of
  paddling in the air.

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
