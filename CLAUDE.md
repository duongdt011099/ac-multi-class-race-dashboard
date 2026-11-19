# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

Two cooperating pieces that ship together as one Windows installer (`EnduranceRaceDashboard.iss`):

1. **The dashboard** — a Blazor Server app (.NET 10, `net10.0-windows`, SQLite via EF Core) that manages multi-class championships (classes, teams, drivers, seasons, races, points) for Assetto Corsa. It runs windowless, hosted in a WebView2 window plus a tray icon (WinForms), and only answers requests carrying a per-process access token (`Web/` guard).
2. **The Lua app** in `assetto-corsa-app/Multi_Class` — a Custom Shaders Patch (CSP) in-game app (leaderboard, safety car, rolling start, yellow flags, timed race, track map, result exporter). `Multi_Class.lua` is only a wiring entry point; all logic lives in `src/`.

See `README.md` for the user-facing feature list and the folder table for the dashboard.

## Commands

```powershell
dotnet build multi-class-race-dashboard.csproj
dotnet test tests/MulticlassRace.Tests
dotnet test tests/MulticlassRace.Tests --filter "FullyQualifiedName~SomeTestClass.SomeTest"   # single test
dotnet ef migrations add <Name>   # migrations live in /Migrations; AppDbContextFactory supports design-time
```

Tests are xUnit + bUnit and use an in-memory SQLite database — they never touch `app.db`. There is no linter or Lua test harness; the Lua app is tested in-game.

## Architecture

### Dashboard ↔ Lua app contract (spans both codebases)
- **Dashboard → game:** when a session starts, `Builders/RaceIniBuilder.cs` writes the grid/`race.ini` and `Builders/SessionManifestBuilder.cs` writes `mcr_session.json` into `Documents/Assetto Corsa/cfg/`. The Lua side reads it in `src/classes/session_file.lua` (the header comment documents the JSON shape; keep both sides in sync, including the `version` field).
- **Game → dashboard:** the Lua exporter (`src/export/`) writes result JSON to `Documents/Assetto Corsa/mcr-results/{practice,qualifying,race}/yyMMdd-HHmmss.json` (quick races use a `quick-race-` prefix). `BackgroundServices/LuaResultImportWorker` + `Services/Results/LuaResult*` import these (tracked in the `ImportedLuaResult` table), and `LuaResultCleanupService` sweeps them. Paths/naming are defined in `Services/Results/LuaResultPaths.cs`; the Lua app always uses the default folder (any configured path is cleared at startup in `Program.cs`).

### Dashboard layering
Pages (`Components/Pages/*.razor` + `.razor.cs` code-behind, deriving `Components/Shared/PageBase`) → `ViewModels` (UI rules, form models, testable without rendering) → `Services/<Feature>` (interface beside implementation; each feature registers itself in a `*ServiceRegistration.cs`, aggregated by `RegisterServices`) → `Repositories` (EF Core) → `Data` (`AppDbContext`, one `IEntityTypeConfiguration` per entity, JSON converters, seed data). `Mapping/` holds entity↔view-model extension methods. Reuse `Components/Shared` building blocks (`Modal`, `DataTable`, `AppSelect`, `FormField`, ...) rather than writing new dialog/table/dropdown markup.

Startup (`Program.cs`): single-instance guard (`DashboardInstance`) → access-token guard middleware → `MigrateAsync()` on boot (migrations apply automatically) → Kestrel on a system-chosen port → WebView2 window + tray (`Infrastructure/`).

### Domain rules worth knowing
- Seasons have a fixed round count; when all rounds finish the season becomes read-only (no add/edit/delete of races, season not deletable) and achievements/trophies are awarded. Older seasons show "Rounds not set".
- Race status is derived: no data = not started, practice/qualifying loaded = in progress, race loaded = finished.
- Teams get one class per class setting; exactly one class setting is the default and cannot be unset.
- Timed races: AC only supports lap-limited races, so the Lua app (`src/timed_race.lua`) runs the clock and ends the race on the leader's final lap.
- Quick Race is a separate flow with no championship/points.

## Conventions and gotchas

- **Version must match in two places:** `<Version>` in `multi-class-race-dashboard.csproj` and `MyAppVersion` in `EnduranceRaceDashboard.iss`. The update check compares the assembly version to the latest GitHub release.
- **Installer ships only** `Multi_Class.lua`, `manifest.ini`, `icon.png` and `src\*.lua` (recursively) of the Lua app; a new top-level asset needs an entry in the `.iss` `[Files]` section.
- **After editing any file under `assetto-corsa-app/Multi_Class`, copy it (same relative path) to** `D:\Steam\steamapps\common\assettocorsa\apps\lua\Multi_Class` without asking — the game is tested from that folder.
- `bin/`, `obj/` (many `obj/validation*` dirs), `publish/` and `installer/` are build output; ignore them when searching.
- A `graphify-out/` knowledge graph of the repo exists (`GRAPH_REPORT.md`); consult it for architecture questions before grepping widely.
