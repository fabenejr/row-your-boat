# Contributing to Viking Oarsmen

Thanks for helping! Bug reports, ideas, tuning feedback and code are all welcome.
By participating you agree to follow the [Code of Conduct](CODE_OF_CONDUCT.md).

## Reporting bugs and ideas

- **Bugs:** open an issue with the *Bug report* template. Please attach `BepInEx/LogOutput.log` and
  list your other mods. Many problems are mod conflicts.
- **Ideas:** open an issue with the *Feature request* template before starting large changes,
  so we can agree on the approach first.

## Development setup

Requirements:

- Windows with **Visual Studio 2022+** (".NET desktop development" workload) or the **.NET SDK**
- **Valheim** with **BepInExPack Valheim** and **Jötunn** installed (the project references `JotunnLib` via
  NuGet to compile against it, but running the game still needs `Jotunn.dll` in `BepInEx/plugins`)

Steps:

1. Fork and clone the repository.
2. Copy `Local.props.example` to `Local.props` and set `ValheimDir` to your Valheim folder.
   Set `DeployToPlugins` to `true` to copy the DLL into `BepInEx/plugins` on every build.
3. Open `VikingOarsmen.sln` and build in **Release**, or run:
   ```bash
   dotnet build VikingOarsmen.sln -c Release
   ```

> Never commit game or BepInEx assemblies (`assembly_valheim.dll`, `UnityEngine*.dll`, ...).
> They are proprietary and are only referenced from your local install.

### Finding game APIs

Put the cursor on a game type such as `Ship` or `Player` in Visual Studio and press **F12**. VS shows
the decompiled game code, which is the best way to see what a method does before patching it.

## Project layout

| File | Responsibility |
| --- | --- |
| `Plugin.cs` | BepInEx entry point, config and Harmony setup |
| `OarItem.cs` | Registers the craftable oar item/weapon and its recipe via Jötunn |
| `RowingGear.cs` | The `RowingGear` enum (mirrors `Ship.Speed`) |
| `ShipRowingPatch.cs` | Harmony patches that add the mod's components to ships and players |
| `RaftSeat.cs` | The extra rowing bench added to every raft |
| `RowingController.cs` | Local input (gear shifting, stamina checks) and synced rowing/gear state |
| `ShipRowing.cs` | Gear-weighted thrust applied by the ship owner |
| `OarVisual.cs` | Per-player rowing visuals: animation, rowing grip, blade splash, facing the stern, gunwale for the gear indicator |
| `ShipFit.cs` | Per-ship rower placement: slide towards the gunwale and lean out over it |
| `RowerAnimation.cs` | Plays the rowing clips over the game's Animator (Playables), in step with the crew |
| `ModAssets.cs` | Loads the embedded AssetBundle (oar model, rowing clips) |
| `OarSwing.cs` | Slower swing animation when attacking with the oar |
| `OarSplash.cs` | Splash and sound when the blade hits the water (reused from arrows) |
| `GearHud.cs` | The rower's gear indicator, cloned from the vanilla ship HUD |
| `VikingOarsmen/AssetBundles/` | The AssetBundle (oar model, rowing clips) built in Unity and embedded in the DLL |
| `art/oar/` | Blender sources of the oar model and animation rig, plus the script that builds them |
| `VikingOarsmen-Documents/` | Design notes (Obsidian vault): plan, steering decisions and the asset pipeline |
| `package/` | Thunderstore manifest and icon |
| `scripts/release.ps1` | Release packaging (see [RELEASING.md](RELEASING.md)) |
| `.github/workflows/release.yml` | Drafts the GitHub release when a `vX.Y.Z` tag is pushed |

## Coding guidelines

- **Code comments in English**, short and focused on *why*, one per logical step.
- Match the existing style (enforced by `.editorconfig`):
  - Braces on their own line.
  - Fields are `_camelCase` (private) or `s_camelCase` (private static). Constants are `PascalCase`.
- **Multiplayer first:** only the ZNetView owner may change a ship's physics, and each player only
  writes to their own ZDO. Anything visual must also work for remote players.
- Prefer adding components and small postfixes over prefix patches that skip vanilla code.
- New user-facing settings go through `Config.Bind` with a clear description, and get a row in the README table.
- Don't change the plugin GUID. It would reset everyone's config.

## Testing in game

There is no automated test suite (the game can't run in CI), so please check these by hand before opening a PR:

- [ ] Plugin loads: `BepInEx/LogOutput.log` shows `Viking Oarsmen vX.Y.Z loaded`, with no errors.
- [ ] The oar is craftable at a tier-1 workbench for 6 Fine Wood, with an icon and a model (temporary mesh is fine).
- [ ] Sitting on a bench without the oar equipped: pressing W/S shows the "equip the oar" hint and does nothing else.
- [ ] Sitting on a bench (`spawn VikingShip`, `spawn Karve`) with the oar equipped: rowing starts automatically in neutral.
- [ ] W steps neutral → 1 → 2 → 3 (no skipping); S steps the other way into reverse. Same one-degree-at-a-time behavior as the ship's own rudder.
- [ ] W/S never stand the rower up. Only **E** (interact) or **Jump** get off the bench, same two ways out as the helm.
- [ ] Reverse actually pushes the ship backward.
- [ ] At the helm, W/S still steer the ship as usual and never start rowing.
- [ ] The oar rests on the gunwale beside the rower on both sides of the ship, without cutting through the hull.
- [ ] The blade pulls back through the water, then lifts out, turns flat and swings forward.
- [ ] The blade stays in the water for the whole pull, also on the longship and in rough weather.
- [ ] The blade splashes, with sound, as it enters the water. `Splash = false` turns it off.
- [ ] With several rowers, all oars stroke in time.
- [ ] The rower holds the oar with both hands, the arms follow it and the torso moves with the stroke.
- [ ] Getting up from the bench, or switching away from the oar, hides it immediately.
- [ ] The ship accelerates smoothly and the helm still steers.
- [ ] Stamina drains per stroke, faster in gear 2/3 than gear 1/reverse. Running out withholds that gear's thrust (selected gear doesn't change) until stamina refills enough for the next stroke.
- [ ] Opening chat, console, inventory or map doesn't shift gear.
- [ ] The oar works as a normal weapon outside a ship (attack, stagger, knockback) without side effects.
- [ ] *(If possible)* With two players, each sees the other's oar, and rowing in different gears adds/subtracts thrust correctly.

Useful console commands (**F5**): `devcommands`, `god`, `spawn VikingShip`, `spawn Karve`, `spawn Raft`.

## Pull requests

1. Create a branch from `main`: `fix/short-description` or `feature/short-description`.
2. Keep PRs focused on one change.
3. **Add an entry under `## Unreleased` in `CHANGELOG.md`**, in the right group (Added, Changed, Fixed, Removed).
4. Fill in the PR template, including which ships and scenarios you tested.
5. Commit messages: short imperative summary (`Fix oar clipping on raft`), with details in the body if needed.

Maintainers handle versioning and releases ([RELEASING.md](RELEASING.md)).

## License

By contributing, you agree that your contributions are licensed under the [MIT License](LICENSE).
