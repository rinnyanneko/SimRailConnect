# AGENTS.md

Repository guidance for AI coding agents working on SimRailConnect.

## Project Overview

SimRailConnect is a .NET 6 C# MelonLoader mod for SimRail. It exposes in-game telemetry through a local HTTP REST API for external simulation, safety-system, display, and hardware-integration tooling.

This project uses HarmonyX patches and IL2CPP interop. Treat native-object lifetime, Unity main-thread access, and scene transitions as high-risk areas.

## Repository Layout

- `SimRail.sln` - Visual Studio solution.
- `src/SimRailConnect/SimRailConnect.csproj` - .NET 6 project file and game-reference paths.
- `src/SimRailConnect/Plugin.cs` - MelonLoader entry point, preferences, lifecycle, and Harmony patch setup.
- `src/SimRailConnect/TelemetryMonitor.cs` - Harmony postfix around `Pyscreen.Update()` and telemetry tick coordination.
- `src/SimRailConnect/GameBridge.cs` - IL2CPP/native interop, cached game references, snapshot construction, and queued write handling.
- `src/SimRailConnect/HttpApiServer.cs` - local HTTP listener, JSON endpoint routing, write queue entry point, and native debug endpoint.
- `src/SimRailConnect/TelemetryState.cs` - shared state between Unity main thread and HTTP background thread.
- `src/SimRailConnect/Models.cs` - JSON response/request models.
- `README.md` - user-facing overview, legal disclosure, install, configuration, and API summary.
- `API_DOCUMENTATION.md` - detailed endpoint contract.

## Build and Test

- Primary build command: `dotnet build SimRail.sln`.
- The project requires .NET 6 SDK and local SimRail/MelonLoader IL2CPP assemblies.
- `GameDir` is currently set in `src/SimRailConnect/SimRailConnect.csproj`; do not assume it is valid on every machine.
- If build references fail, inspect `GameDir` and the generated `MelonLoader/Il2CppAssemblies` folder before changing code.
- There is currently no dedicated automated test project. For behavioral changes, at minimum run `dotnet build SimRail.sln` when dependencies are available and document if it cannot be run.

## Architecture Constraints

- Do not reintroduce `ClassInjector.RegisterTypeInIl2Cpp<T>()` or new IL2CPP class injection. The current design intentionally avoids ClassInjector because its global field-metadata hook caused scene-transition crashes.
- Keep telemetry collection driven by the HarmonyX `Pyscreen.Update()` hook unless there is a well-documented reason to change it.
- Do not use `FindObjectOfType` or broad Unity object scans on the hot telemetry path.
- Do not cache IL2CPP/native references without a clear invalidation path for scene unloads and source replacement.
- Preserve cache invalidation on scene lifecycle changes. Stale native wrappers can become dangling pointers and cause `AccessViolationException`.
- Prefer reading typed source-object arrays directly, as documented in `GameBridge.cs`, rather than relying on flat Pyscreen arrays that may only update while rendered.

## Threading and Native Interop Rules

- Native-memory and IL2CPP object access must happen on the Unity main thread.
- HTTP handlers run on a background thread. They must not directly read or write IL2CPP/native game objects.
- Writes from `POST /api/write`, cache invalidations, and debug/native inspection work should be queued and drained from the telemetry tick on the Unity main thread.
- `GET /api/debug` must remain a queued diagnostic path. Do not read native array pointers directly from the HTTP handler.
- Keep shared HTTP/telemetry state simple, immutable where practical, and safe for cross-thread reads.
- Be conservative with unsafe code, pointer arithmetic, `Marshal`, and direct array writes. Add comments for non-obvious native layout assumptions.

## API and Documentation

- When changing endpoint behavior, update both `README.md` and `API_DOCUMENTATION.md` if user-facing contracts change.
- Preserve JSON field names unless a breaking change is explicitly requested.
- Keep write API behavior explicit: writes are queued, execute on the Unity main thread at the next telemetry tick, and may affect dashboard/display state rather than physical simulation state.
- Document `/api/debug` as diagnostics only. It exposes native cache shape and raw samples for troubleshooting, not a stable integration contract.
- The default base URL is `http://localhost:5555`; update docs if defaults change.

## Coding Style

- Use C# with nullable reference types enabled.
- Follow the existing file style: GPL header, explicit `using` directives, file-scoped namespace, and clear section comments for complex lifecycle/interoperability code.
- Keep comments focused on why a native/threading/lifecycle decision is required; avoid restating straightforward code.
- Do not introduce broad dependencies unless necessary for the mod runtime. Remember the final artifact is loaded by MelonLoader inside the game process.
- Avoid changes that increase per-tick allocations or blocking work in the telemetry loop.

## Safety, Legal, and Scope

- This repository is for interoperability, simulation research, safety-system fidelity, custom displays, and hardware interfaces.
- Do not add features intended for cheating, multiplayer advantage, DRM bypass, unauthorized content distribution, or interference with other players.
- Preserve GPLv3 licensing notices in source files and derivative code.
- Be careful with advice or changes that could modify multiplayer behavior; prefer read-only telemetry unless the requested change is clearly within the documented write API scope.

## Agent Workflow

- Before editing, inspect the relevant source and docs instead of guessing from names.
- Keep changes narrow and explain any architectural tradeoff involving Harmony patches, scene lifecycle, main-thread execution, or native memory.
- If a command cannot run because local SimRail/MelonLoader dependencies are missing, report that exact blocker rather than masking it with unrelated changes.
- Do not revert user changes unless explicitly asked.
