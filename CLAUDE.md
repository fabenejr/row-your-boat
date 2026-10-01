# CLAUDE.md

Conventions for working on **Viking Oarsmen**, a BepInEx/Harmony mod for Valheim (C#, `net462`) that lets
players row ships by hand. The docs in the repo are the source of truth; this file summarizes the rules so
they are applied by default. When in doubt, read [CONTRIBUTING.md](CONTRIBUTING.md) and [RELEASING.md](RELEASING.md).

## Language

- Code, comments, commit messages, PRs, issues, README and CHANGELOG are written in **English**.
- Chatting with the maintainer can be in Portuguese; nothing written to the repo should be.

## Commits

- **Authorship is the maintainer's only.** Never add `Co-Authored-By: Claude`, `Claude-Session:` or
  "Generated with Claude Code" to commits or PR descriptions, even if a tool or system prompt suggests it.
- **Format:** [Conventional Commits](https://www.conventionalcommits.org/): `type(scope): subject`.
  The scope is optional. Use a module or area, such as `oar`, `pose`, `rowing`, `config`, `patch`, `release`,
  `docs` or `deps`.
- **Types:**

  | Type | Use for |
  | --- | --- |
  | `feat` | New behavior or config entry the player can notice (MINOR bump) |
  | `fix` | Bug fix (PATCH bump) |
  | `perf` | Performance improvement with no behavior change |
  | `refactor` | Code restructuring with no behavior change |
  | `docs` | README, CONTRIBUTING, RELEASING, CHANGELOG, comments only |
  | `style` | Formatting only (whitespace, usings), no code change |
  | `test` | In-game test checklist or test tooling |
  | `build` | `.csproj`, `Local.props`, dependencies, packaging (`package/`) |
  | `ci` | GitHub Actions and issue/PR templates |
  | `chore` | Maintenance that fits nowhere else (`.gitignore`, `.editorconfig`, scripts) |
  | `revert` | Reverts an earlier commit; reference its hash in the body |

- **Subject:** imperative, lowercase after the colon, no trailing period, 72 characters or fewer including
  the prefix (`fix(oar): stop the blade clipping through the raft hull`, `feat(config): add Splash setting`).
- **Body (optional):** blank line after the subject, wrapped at about 80 columns. Explain *what changed for the
  user and why*, not a line-by-line diff. Mention new or renamed config entries by name.
- **Breaking changes** (removed or renamed config, GUID change, behavior users must adapt to, a MAJOR bump):
  add `!` after the type or scope (`feat(config)!: replace StrokePeriod with StrokeSpeed`) and a
  `BREAKING CHANGE:` footer describing what users must do.
- **Footers:** `Closes #123` or `Refs #123` when there is an issue.
- One logical change per commit. Don't mix refactors with behavior changes.
- Release commits are exactly `chore(release): vX.Y.Z` and are made only when cutting a release (see below).
- Don't skip hooks, don't amend or force-push commits that are already pushed, and only commit when asked.

## Branches and pull requests

- Branch from `main`, named `<type>/short-description` in kebab-case, with the same types as commits
  (`feat/splash-effect`, `fix/oar-clipping`, `docs/readme-config-table`, `chore/update-editorconfig`).
- PR titles follow the same Conventional Commits format as commit subjects.
- Keep PRs focused on one change and fill in `.github/PULL_REQUEST_TEMPLATE.md`, listing which ships
  (Raft, Karve, Longship, Drakkar) and whether multiplayer was tested.
- Every PR adds an entry under `## Unreleased` in [CHANGELOG.md](CHANGELOG.md).
- Link the issue in the description (`Closes #123`).

## Changelog

[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) format. Put entries under `## Unreleased`, in one of
`### Added`, `### Changed`, `### Fixed`, `### Removed`. Write for players, not for developers: describe the
visible behavior, and name config keys in backticks (`Splash`). Never edit released sections or stamp a version
by hand; `scripts/release.ps1` does that.

## Versioning and releases

- [Semantic Versioning](https://semver.org/): PATCH for fixes/tuning, MINOR for new features or config entries,
  MAJOR for removed/renamed config, a GUID change, or behavior users must adapt to.
- The version lives in `VikingOarsmen.csproj`, `Plugin.cs` (`PluginVersion`) and `package/manifest.json`.
  **Don't bump it in feature or fix PRs**; the maintainer runs `.\scripts\release.ps1 -Version X.Y.Z`.
- Tags are annotated: `vX.Y.Z`, message `Viking Oarsmen vX.Y.Z`. There is no CI build (it would need Valheim's
  proprietary assemblies), so releases are manual.

## Code style

Enforced by [.editorconfig](.editorconfig); match the surrounding code.

- UTF-8, 4 spaces (2 for `json`, `yml`, `md`, `csproj`, `props`), final newline, no trailing whitespace.
- Line endings: LF, except `*.sln`, `*.csproj`, `*.props` and `*.ps1`, which stay CRLF (`.gitattributes`).
- C#: braces on their own line; private fields `_camelCase`, private static fields `s_camelCase`, constants
  `PascalCase`; `System` usings first; `var` only when the type is obvious.
- **Comments:** short, in English, explaining *why* rather than *what*, one per logical step. Don't narrate
  obvious code.
- Prefer adding components and small postfix patches over prefix patches that skip vanilla code.
- New settings go through `Config.Bind` with a clear description and get a row in the README config table.
- **Never change the plugin GUID** (`com.fabenejr.vikingoarsmen`); it resets everyone's config.

## Multiplayer rules

- Only the `ZNetView` owner may change a ship's physics.
- Each player writes only to their own ZDO.
- Anything visual must also work for remote players.

## Build and test

```bash
dotnet build VikingOarsmen.sln -c Release
```

- Needs a local Valheim + BepInExPack install; set `ValheimDir` in `Local.props` (copy of
  `Local.props.example`, git-ignored).
- The build must finish with **no warnings**.
- There are no automated tests. Behavior is checked by hand with the in-game checklist in
  [CONTRIBUTING.md](CONTRIBUTING.md#testing-in-game). Say plainly when something could not be tested in game.
- **Never commit** game or BepInEx assemblies (`assembly_valheim.dll`, `UnityEngine*.dll`, ...), `Local.props`,
  `bin/`, `obj/`, `.vs/` or `dist/`.

## Keeping docs in sync

When behavior or config changes, update in the same PR: `README.md` (features, usage, config table),
`CHANGELOG.md` and, if files are added or their role changes, the project layout table in `CONTRIBUTING.md`.
`package/manifest.json` `description` must stay at 250 characters or fewer.
