# Viking Oarsmen

A [BepInEx](https://github.com/BepInEx/BepInEx) mod for **Valheim** that lets players row their ship by hand.
Press a key while aboard: an oar appears at your side, strokes through the water and moves the ship
as if it were sailing with the wind at its back, while whoever is at the helm keeps steering.

## Features

- **Manual rowing:** press **R** aboard any ship to start rowing, then press it again to stop. You can switch to hold-to-row in the config.
- **Tailwind speed:** rowing thrust matches a full sail in a strong tailwind, scaled per ship type (raft, karve, longship, drakkar).
- **Helm keeps control:** rowing only pushes along the bow and never turns the ship. The helmsman steers with the vanilla rudder.
- **Animated oar:** the oar appears only while rowing and moves through a full stroke. The blade dips on the pull, lifts and turns flat on the return, and always sits on the side of the hull you are standing on.
- **Uses the game's own art:** the oar model is the Karve's steering oar, so no extra asset files are needed. If that model can't be found, a simple oar built in code is used instead.
- **Stamina cost:** 1 stamina every 10 seconds of rowing. You stop rowing automatically when exhausted.
- **Multiplayer-aware:** everyone sees each other's oars, and thrust is applied by the ship's owner, so rowing works for passengers too.

## Requirements

- Valheim (PC)
- [BepInExPack Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/)

## Installation

1. Download `VikingOarsmen.dll` from the [Releases](../../releases) page, or build it yourself (see below).
2. Copy it into `<Valheim>/BepInEx/plugins/`.
3. Launch the game. `BepInEx/LogOutput.log` should contain `Viking Oarsmen v1.1.0 loaded`.

## Usage

1. Board a ship. Stand near the side, or sit on a bench.
2. Press **R** to start rowing. "Remando!" shows up and your oar appears.
3. Press **R** again to stop. Rowing also stops when you leave the ship, fall in the water, die or run out of stamina.

You can row with the sail up. The two forces add together.

### Quick test in single player

Open the console with **F5** and run `devcommands`, `god` and `spawn Karve` while facing the water.
Close the console, climb aboard and press **R**.

## Configuration

The settings file is created on first launch at `BepInEx/config/com.autor.vikingoarsmen.cfg`:

| Section | Key | Default | Description |
| --- | --- | --- | --- |
| Controls | `RowKey` | `R` | Key to start/stop rowing (R also hides weapons in vanilla) |
| Controls | `HoldToRow` | `false` | `true` = row only while the key is held |
| Physics | `RowingPower` | `1.0` | 1.0 = full sail with a strong tailwind |
| Physics | `RampUpTime` | `1.5` | Seconds to reach full thrust (and to fade out) |
| Gameplay | `StaminaDrainAmount` | `1` | Stamina per drain tick (0 disables) |
| Gameplay | `StaminaDrainInterval` | `10` | Seconds between drain ticks |
| Visual | `StrokePeriod` | `2.0` | Duration of one oar stroke (seconds) |
| UI | `ShowMessage` | `true` | Show "Remando!" when rowing starts |

## Multiplayer

Every player who wants to row, see oars or own a rowed ship needs the mod installed.
Dedicated servers don't need it.

How it works: each rower writes the ID of the ship they are rowing to their own player data,
which Valheim syncs to everyone. The client that owns the ship (usually the helmsman) applies the
thrust, and every client draws the oars.

## Building from source

Prerequisites: Visual Studio 2022+ with **.NET desktop development**, or the .NET SDK, plus Valheim with BepInExPack installed.

1. Clone the repository.
2. Copy `Local.props.example` to `Local.props` and set `ValheimDir` to your Valheim folder.
   This file is git-ignored.
3. Build:
   - **Visual Studio:** open `VikingOarsmen.sln`, select **Release**, then press **Ctrl+Shift+B**.
   - **CLI:**
     ```bash
     dotnet build VikingOarsmen.sln -c Release
     ```
4. The output is `VikingOarsmen/bin/Release/VikingOarsmen.dll`.
   Set `DeployToPlugins` to `true` in `Local.props` to copy it into `BepInEx/plugins` on every build.

### Project layout

| File | Responsibility |
| --- | --- |
| `Plugin.cs` | BepInEx entry point, config and Harmony setup |
| `ShipRowingPatch.cs` | Harmony patches that add the mod's components to ships and players |
| `RowingController.cs` | Local input, stamina drain and synced rowing state |
| `ShipRowing.cs` | Thrust applied by the ship owner |
| `OarVisual.cs` | Oar placement and stroke animation |
| `OarModel.cs` | Oar mesh (reused from the Karve, or built procedurally) |

## Roadmap ideas

- Water splash effects and sound when the blade enters the water
- Hands following the oar using inverse kinematics (IK)
- Extra speed for each additional rower
- A dedicated oar model loaded from an AssetBundle

## Contributing

Issues and pull requests are welcome. Please keep code comments in English and match the existing style.

## License

[MIT](LICENSE) © 2026 Paulo Fabene

This project is not affiliated with or endorsed by Iron Gate Studio or Coffee Stain Publishing.
Valheim is a trademark of its respective owners.
