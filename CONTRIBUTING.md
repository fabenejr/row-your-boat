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
- **Valheim** with **BepInExPack Valheim** installed

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
| `ShipRowingPatch.cs` | Harmony patches that add the mod's components to ships and players |
| `RowingController.cs` | Local input, stamina drain and synced rowing state |
| `ShipRowing.cs` | Thrust applied by the ship owner |
| `OarVisual.cs` | Oar placement and stroke animation |
| `OarModel.cs` | Oar mesh (reused from the Karve, or built procedurally) |
| `package/` | Thunderstore manifest and icon |
| `scripts/release.ps1` | Release packaging (see [RELEASING.md](RELEASING.md)) |

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
- [ ] Standing on a ship, pressing **R** shows the "sit on a bench" hint and does nothing else.
- [ ] Sitting on a bench (`spawn VikingShip`) and at the helm (`spawn Karve`, `spawn Raft`), **R** starts and stops rowing.
- [ ] The oar sits on the gunwale, its blade enters the water on the drive and clears it on the return.
- [ ] Getting up from the bench or helm hides the oar immediately.
- [ ] The ship accelerates smoothly and the helm still steers.
- [ ] Stamina drops by 1 every 10 seconds. Rowing stops when exhausted.
- [ ] Opening chat, console, inventory or map doesn't toggle rowing.
- [ ] *(If possible)* With two players, each sees the other's oar and two rowers are faster than one.

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
