# JellyRating — project notes

Jellyfin 12.1.0 plugin "Parental Rating Manager": manual US/CA parental ratings
+ per-user max content levels via Jellyfin's native parental-control engine.

## Build / test / package

.NET 10 SDK is installed at `C:\Users\Desktop\AppData\Local\Microsoft\dotnet\dotnet.exe`
(not on PATH — use the full path or prepend it to PATH).

```powershell
dotnet build Jellyfin.Plugin.ParentalRatingManager.sln -c Release
dotnet test  Jellyfin.Plugin.ParentalRatingManager.sln -c Release   # 83 tests
.\package.ps1 -SourceUrl <zip-url>                                   # dist zip + manifest.json + md5
```

## Layout

- `Jellyfin.Plugin.ParentalRatingManager/` — plugin
  - `Plugin.cs` — BasePlugin + IHasWebPages (guid `77304605-bb68-4844-8aed-251df4e20e83`)
  - `ServiceRegistrator.cs` — IPluginServiceRegistrator DI wiring
  - `Controllers/ParentalRatingController.cs` — admin API, `[Authorize(Policy = RequiresElevation)]`, route `/ParentalRatingManager`
  - `Services/` — RatingCatalog (embedded ratings.json), RatingNormalizer (pure),
    PluginDataStore (versioned JSON, atomic writes), EffectiveRatingService
    (override→season→series→metadata→unrated), RatingAssignmentService
    (projects to `BaseItem.CustomRating` + cascade), UserRestrictionService
    (`MaxParentalRatingScore`/`SubScore` + `BlockUnratedItems`),
    LibraryContentService (browse/search/page, seasons, no episodes)
  - `Configuration/configPage.html` — embedded dashboard page
  - `Ratings/ratings.json` — embedded rating catalogue (us/ca + unrated)
- `Jellyfin.Plugin.ParentalRatingManager.Tests/` — xUnit (needs Jellyfin pkgs
  *with* runtime assets — do NOT add ExcludeAssets there)
- `build.yaml` — repo metadata; `package.ps1` produces `dist/` + `manifest.json`
- `.ref/jellyfin-src/` — local Jellyfin v12.1 source checkout for API reference

## Conventions / gotchas

- Keep pure logic (normalizer, catalog, policy) free of Jellyfin type
  references — `RatingCatalog` takes `ICustomRatingsSource` so tests never
  touch `Plugin.Instance`.
- Never save arbitrary rating strings: everything goes through
  `RatingNormalizer`/`RatingCatalog` ids.
- `RatingAssignmentService.SelectJellyfinValue` verifies each projected
  `CustomRating` via `ILocalizationManager.GetRatingScore`; unresolvable or
  wrong-level values are replaced by a same-level catalogue value or a numeric
  string (score+1 for sub-scores) so enforcement never weakens silently. The
  written value is audited in `data.json` (`projectedValue`).
- Episodes are never assignment targets; they inherit via `DisplayParent`.
