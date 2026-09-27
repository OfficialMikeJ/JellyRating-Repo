# Parental Rating Manager — Build & Install

Jellyfin 12.1.0 plugin for manual US/Canadian parental rating assignment and
per-user maximum content levels, enforced server-side by Jellyfin's own
parental-control engine.

## Requirements

- Jellyfin Server **12.1.0** (targets `net10.0`)
- .NET SDK **10.x** to build

## Build

```powershell
# Windows
dotnet build Jellyfin.Plugin.ParentalRatingManager.sln -c Release
dotnet test  Jellyfin.Plugin.ParentalRatingManager.sln -c Release
```

The plugin DLL lands in
`Jellyfin.Plugin.ParentalRatingManager\bin\Release\net10.0\Jellyfin.Plugin.ParentalRatingManager.dll`.

## Manual install

1. Create `plugins\ParentalRatingManager\` inside your Jellyfin data directory
   (e.g. `%ProgramData%\Jellyfin\Server\plugins\ParentalRatingManager\`).
2. Copy `Jellyfin.Plugin.ParentalRatingManager.dll` into it.
3. Restart Jellyfin.
4. Dashboard → Plugins → **Parental Rating Manager**.

## Install via a plugin repository (recommended)

1. Run `package.ps1` — it builds, zips the DLL into `dist\` and writes
   `manifest.json` with the correct MD5 checksum:

   ```powershell
   .\package.ps1 -SourceUrl "https://<gitea>/<owner>/<repo>/raw/branch/main/dist/Jellyfin.Plugin.ParentalRatingManager_1.0.0.0.zip" -Owner "<you>"
   ```

2. Commit `manifest.json` and `dist\Jellyfin.Plugin.ParentalRatingManager_1.0.0.0.zip`
   to your Gitea repo.

   If your Gitea sits behind Cloudflare (or similar), raw endpoints are cached
   for hours — append `&v=N` (incremented per release) to both the manifest URL
   and `sourceUrl` so updated artifacts bypass the stale cache.

   On sign-in-required instances, use Gitea's tokenized API raw endpoint for
   both URLs:
   `/api/v1/repos/<owner>/<repo>/raw/<path>?token=<read-repo-token>`
3. In Jellyfin: Dashboard → Plugins → Repositories → **+**, and add the
   manifest URL:

   ```
   https://officialmikej.github.io/JellyRating-Repo/manifest.json
   ```

   The manifest + zip are served by GitHub Pages (anonymous +
   `application/json`), while the Gitea repo at
   `https://repo.mikeshomelabservices.xyz/OfficialMikeJ/JellyRating` remains the
   source of truth. The Gitea instance requires sign-in for all anonymous
   requests, which is why the release artifacts are mirrored to Pages.
4. The plugin appears in the catalogue; install, restart, configure.

## How enforcement works

- Ratings you assign are written to the item's `CustomRating` field and the
  plugin keeps its own override store (`plugins\Jellyfin.Plugin.ParentalRatingManager\data.json`,
  versioned, atomic writes).
- Series/season ratings inherit down to episodes through Jellyfin's built-in
  `CustomRatingForComparison` chain — no per-episode data needed.
- User limits write `MaxParentalRatingScore`/`SubScore` + `BlockUnratedItems`
  on the user record, which Jellyfin enforces at the **database query layer**
  and in `IsVisible`/`IsParentalAllowed` for every client/API — items are not
  merely hidden in the UI, they are inaccessible by id or playback URL for
  restricted users.

## Known limitations

- **Projection verification**: before writing `CustomRating`, the plugin
  checks the value against Jellyfin's own `LocalizationManager.GetRatingScore`.
  If a rating's configured `JellyfinValue` doesn't resolve to exactly the
  intended level, the plugin substitutes (in order) another catalogue value
  resolving to the same level, or a plain numeric score Jellyfin always
  parses — sub-score levels bump up one point so enforcement is never weaker
  than intended. Substitutions are logged and recorded in `data.json` as
  `projectedValue`. An unresolvable value can therefore never silently degrade
  an item to "unrated".
- Administrators are never restricted by Jellyfin parental controls.
- Rating projections update on save; a media item's *original* `OfficialRating`
  metadata is never modified, and rescans that refresh `OfficialRating` do not
  remove plugin overrides (they live in the plugin's own store).
