# Viking Oarsmen

A [BepInEx](https://github.com/BepInEx/BepInEx) mod for **Valheim** that lets players row their ship by hand.
Sit on a bench and press a key: your character grabs an oar over the side and paddles the ship forward.
The more of the crew rows, the faster you go, and whoever is at the helm keeps steering.

## Features

- **Manual rowing:** sit on a bench, then press **R** to start rowing and press it again to stop. You can switch to hold-to-row in the config.
- **Crew-powered speed:** each rower adds 25% of the maximum thrust, so four rowers match a full sail in a strong tailwind. The maximum is scaled per ship type (raft, karve, longship, drakkar).
- **Helm keeps control:** rowing only pushes along the bow and never turns the ship. The helmsman steers with the vanilla rudder and can't row; only the crew on the benches can.
- **Animated oar:** the oar is the ship's own steering oar, held over the gunwale beside you like a paddle. The blade digs in ahead of you, pulls back through the water, then lifts out, turns flat and swings forward again. It appears only while rowing and disappears as soon as you get up.
- **Rowing pose:** your character holds the oar with both hands, the arms follow it through the stroke, and the torso reaches forward at the catch, pulls back through the drive and leans towards the oar.
- **Fits every ship:** the oar rests on the actual gunwale on the side you sit on, and its height follows the real water level so the blade always bites the water.
- **Uses the game's own art:** ships without a steering oar borrow the Karve's, so no extra asset files are needed. If none can be found, a simple oar built in code is used instead.
- **Stamina cost:** 1 stamina every 10 seconds of rowing. You stop rowing automatically when exhausted.
- **Multiplayer-aware:** everyone sees each other's oars, and thrust is applied by the ship's owner, so rowing works for passengers too.

## Requirements

- Valheim (PC)
- [BepInExPack Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/)

## Installation

**With a mod manager (r2modman / Thunderstore Mod Manager):** download the
`VikingOarsmen-<version>.zip` from the [Releases](https://github.com/fabenejr/row-your-boat/releases) page
and choose *Import local mod* in the manager.

**Manually:**

1. Download `VikingOarsmen.dll` from the [Releases](https://github.com/fabenejr/row-your-boat/releases) page.
2. Copy it into `<Valheim>/BepInEx/plugins/`.
3. Launch the game. `BepInEx/LogOutput.log` should contain `Viking Oarsmen vX.Y.Z loaded`.

## Usage

1. Board a ship and **sit on a bench** (press **E** on it).
2. Press **R** to start rowing. "Remando!" shows up and your oar appears.
3. Press **R** again to stop. Rowing also stops, and the oar disappears, when you get up from the bench, die or run out of stamina.

You can row with the sail up. The two forces add together.
Only the crew on the benches row: whoever is at the helm steers. Ships without benches, like the raft, can't be rowed.

### Quick test in single player

Open the console with **F5** and run `devcommands`, `god` and `spawn VikingShip` (or `spawn Karve`) while facing the water.
Close the console, climb aboard, sit on a bench and press **R**.

## Configuration

The settings file is created on first launch at `BepInEx/config/com.fabenejr.vikingoarsmen.cfg`:

| Section | Key | Default | Description |
| --- | --- | --- | --- |
| Controls | `RowKey` | `R` | Key to start/stop rowing (R also hides weapons in vanilla) |
| Controls | `HoldToRow` | `false` | `true` = row only while the key is held |
| Physics | `PowerPerRower` | `0.25` | Share of the maximum thrust each rower adds |
| Physics | `MaxRowingPower` | `1.0` | Maximum total thrust (1.0 = full sail with a strong tailwind) |
| Physics | `RampUpTime` | `1.5` | Seconds to reach full thrust (and to fade out) |
| Gameplay | `StaminaDrainAmount` | `1` | Stamina per drain tick (0 disables) |
| Gameplay | `StaminaDrainInterval` | `10` | Seconds between drain ticks |
| Visual | `StrokeSpeed` | `1.0` | Speed of the oar stroke (1.0 = one stroke every 1.5 seconds) |
| Visual | `OarScale` | `0.8` | Size of the oar compared to the ship's steering oar (1.0 = same size) |
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
   - **Visual Studio:** open `VikingOarsmen.sln`, select **Release**, then run **Build Solution**.
   - **CLI:**
     ```bash
     dotnet build VikingOarsmen.sln -c Release
     ```
4. The output is `VikingOarsmen/bin/Release/VikingOarsmen.dll`.
   Set `DeployToPlugins` to `true` in `Local.props` to copy it into `BepInEx/plugins` on every build.

See [CONTRIBUTING.md](CONTRIBUTING.md) for the project layout, coding guidelines and the in-game test checklist.

## Roadmap ideas

- Water splash effects and sound when the blade enters the water
- Hands following the oar using inverse kinematics (IK)
- Rowers in sync: all oars following the same stroke rhythm
- A dedicated oar model loaded from an AssetBundle

## Contributing

Issues and pull requests are welcome! Start with [CONTRIBUTING.md](CONTRIBUTING.md).
Changes are listed in [CHANGELOG.md](CHANGELOG.md), and maintainers publish versions following [RELEASING.md](RELEASING.md).

## License

[MIT](LICENSE) © 2026 Paulo Fabene

This project is not affiliated with or endorsed by Iron Gate Studio or Coffee Stain Publishing.
Valheim is a trademark of its respective owners.
