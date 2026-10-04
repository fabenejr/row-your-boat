# Releasing

Releases are cut manually by a maintainer. There is no CI build because it would need Valheim's game
assemblies, which can't be redistributed. The build and packaging are scripted in `scripts/release.ps1`; pushing the
tag triggers `.github/workflows/release.yml`, which drafts the GitHub release.

## Versioning

The project follows [Semantic Versioning](https://semver.org/):

| Change | Bump | Example |
| --- | --- | --- |
| Bug fix, tuning, no config changes | PATCH | 1.2.0 → 1.2.1 |
| New feature or new config entry | MINOR | 1.2.1 → 1.3.0 |
| Removed/renamed config entries, changed GUID, behavior users must adapt to | MAJOR | 1.3.0 → 2.0.0 |

The version lives in three places, and the script keeps all of them in sync:
`VikingOarsmen/VikingOarsmen.csproj`, `VikingOarsmen/Plugin.cs` (`PluginVersion`) and `package/manifest.json`.

## Checklist

1. **Everything is merged into `main`** and `CHANGELOG.md` lists the changes under `## Unreleased`.
2. **Run the script** from the repository root:
   ```powershell
   .\scripts\release.ps1 -Version 1.2.0
   ```
   It stamps the changelog, bumps the version, builds Release and creates:
   - `dist/VikingOarsmen-<version>.zip`: Thunderstore package (manifest, icon, README, CHANGELOG, LICENSE, DLL)
   - `dist/VikingOarsmen.dll`: the plugin alone, for manual installs
   - `dist/release-notes-<version>.md`: this version's changelog section
3. **Smoke test** `dist/VikingOarsmen.dll` in game (see the checklist in [CONTRIBUTING.md](CONTRIBUTING.md#testing-in-game)).
4. **Commit, tag and push:**
   ```powershell
   git add -A
   git commit -m "chore(release): v1.2.0"
   git tag -a v1.2.0 -m "Viking Oarsmen v1.2.0"
   git push origin main --follow-tags
   ```
5. **Publish the GitHub release.** Pushing the tag runs the *Release* workflow, which checks that the tag matches
   the version in the three files and creates a **draft** release with this version's changelog section as
   notes. Attach the build and publish the draft:
   ```powershell
   gh release upload v1.2.0 dist\VikingOarsmen-1.2.0.zip dist\VikingOarsmen.dll
   gh release edit v1.2.0 --draft=false
   ```
   The draft is not published automatically because the CI can't build the DLL.
6. **(Optional) Thunderstore:** upload `dist/VikingOarsmen-<version>.zip` at
   <https://thunderstore.io/c/valheim/create/> under the Valheim community.

The script prints steps 4 to 6 with the right version filled in.

## If something goes wrong

- **The script stops before building:** nothing was changed. Fix the reported problem and run it again.
- **The build fails after the version bump:** fix the code, then run with `-AllowDirty`. The changelog
  is already stamped, so first move the entries back under `## Unreleased` and delete the version heading.
- **A tag was pushed by mistake:**
  ```powershell
  git push origin :refs/tags/v1.2.0
  git tag -d v1.2.0
  ```
  If a GitHub release was created for it, delete it with `gh release delete v1.2.0`.

## Thunderstore package notes

- `package/manifest.json`: `name` must stay `VikingOarsmen` (no spaces), and `description` must be 250 characters or fewer.
- The BepInExPack dependency version only needs updating when the mod requires a newer BepInEx.
- `package/icon.png` must be exactly 256×256 PNG.
