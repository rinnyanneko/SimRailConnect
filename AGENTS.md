# AGENTS.md

Repository guidance for AI coding agents working on SimRailConnect.

## Project Overview

SimRailConnect is a .NET 6 C# MelonLoader plugin for SimRail. It exposes telemetry through a local WebSocket API for external simulation, safety-system, display, and hardware-integration tooling.

The current plugin build is managed-only and excludes HarmonyX, Unity, IL2CPP, and native telemetry files. Native telemetry prototypes remain in the repository but are not compiled until the SimRail/MelonLoader IL2CPP support-module crash is isolated.

## Repository Layout

- `SimRail.sln` - Visual Studio solution.
- `src/SimRailConnect/SimRailConnect.csproj` - .NET 6 project file and game-reference paths.
- `src/SimRailConnect/Plugin.cs` - MelonLoader plugin entry point, preferences, and WebSocket startup.
- `src/SimRailConnect/TelemetryMonitor.cs` - native telemetry prototype; excluded from the managed-only plugin build.
- `src/SimRailConnect/GameBridge.cs` - native telemetry prototype; excluded from the managed-only plugin build.
- `src/SimRailConnect/WebSocketApiServer.cs` - localhost WebSocket telemetry push, request/response, and command handling.
- `src/SimRailConnect/ApiCommandRegistry.cs` - network-thread-safe write command validation and whitelist mapping.
- `src/SimRailConnect/TelemetryState.cs` - shared state between Unity main thread and WebSocket background threads.
- `src/SimRailConnect/Models.cs` - JSON response/request models.
- `README.md` - user-facing overview, legal disclosure, install, configuration, and API summary.
- `WEBSOCKET_API.md` - detailed WebSocket message contract.

## Build and Test

- Primary build command: `dotnet build SimRail.sln`.
- The project requires .NET 6 SDK and local SimRail/MelonLoader IL2CPP assemblies.
- `GameDir` is currently set in `src/SimRailConnect/SimRailConnect.csproj`; do not assume it is valid on every machine.
- If build references fail, inspect `GameDir` and the generated `MelonLoader/Il2CppAssemblies` folder before changing code.
- There is currently no dedicated automated test project. For behavioral changes, at minimum run `dotnet build SimRail.sln` when dependencies are available and document if it cannot be run.

## Architecture Constraints

- Do not reintroduce `ClassInjector.RegisterTypeInIl2Cpp<T>()` or new IL2CPP class injection. The current design intentionally avoids ClassInjector because its global field-metadata hook caused scene-transition crashes.
- `EnableTelemetryPatch` currently defaults to `false` and is ignored by the managed-only plugin build. The WebSocket server must not reference or call `GameBridge`, Harmony telemetry, Unity, IL2CPP wrappers, native pointers, or `Marshal`.
- Do not use `FindObjectOfType` or broad Unity object scans on the hot telemetry path.
- Do not cache IL2CPP/native references without a clear invalidation path for scene unloads and source replacement.
- Preserve cache invalidation on scene lifecycle changes. Stale native wrappers can become dangling pointers and cause `AccessViolationException`.
- Prefer reading typed source-object arrays directly, as documented in `GameBridge.cs`, rather than relying on flat Pyscreen arrays that may only update while rendered.

## Threading and Native Interop Rules

- Native-memory and IL2CPP object access must happen on the Unity main thread.
- WebSocket handlers run on background threads. They must not directly read or write IL2CPP/native game objects.
- Writes from WebSocket `command` messages, cache invalidations, and debug/native inspection work should be queued and drained from the telemetry tick on the Unity main thread.
- WebSocket handlers are network/background-thread code. They may read `TelemetryState.CurrentSnapshot` and enqueue validated commands only; they must not directly touch Unity, IL2CPP wrappers, native pointers, or `Marshal`.
- When `EnableTelemetryPatch=false`, WebSocket `command`, `debug`, and `invalidate` must return `NATIVE_TELEMETRY_DISABLED` rather than queuing native work.
- WebSocket `debug` messages must remain a queued diagnostic path. Do not read native array pointers directly from the WebSocket handler.
- Keep shared WebSocket/telemetry state simple, immutable where practical, and safe for cross-thread reads.
- Be conservative with unsafe code, pointer arithmetic, `Marshal`, and direct array writes. Add comments for non-obvious native layout assumptions.

## API and Documentation

- When changing endpoint behavior, update `README.md`, `API_DOCUMENTATION.md`, and `WEBSOCKET_API.md` if user-facing contracts change.
- Preserve JSON field names unless a breaking change is explicitly requested.
- Keep write API behavior explicit: writes are queued, execute on the Unity main thread at the next telemetry tick, and may affect dashboard/display state rather than physical simulation state.
- Keep WebSocket-only behavior explicit: the default WebSocket URL is `ws://localhost:5556/ws`.
- Document WebSocket `debug` as diagnostics only. It exposes native cache shape and raw samples for troubleshooting, not a stable integration contract.

## Coding Style

- Use C# with nullable reference types enabled.
- Follow the existing file style: GPL header, explicit `using` directives, file-scoped namespace, and clear section comments for complex lifecycle/interoperability code.
- Keep comments focused on why a native/threading/lifecycle decision is required; avoid restating straightforward code.
- Do not introduce broad dependencies unless necessary for the plugin runtime. Remember the final artifact is loaded by MelonLoader inside the game process.
- Avoid changes that increase per-tick allocations or blocking work in the telemetry loop.

## Safety, Legal, and Scope

- This repository is for interoperability, simulation research, safety-system fidelity, custom displays, and hardware interfaces.
- Do not add features intended for cheating, multiplayer advantage, DRM bypass, unauthorized content distribution, or interference with other players.
- Preserve GPLv3 licensing notices in source files and derivative code.
- Be careful with advice or changes that could modify multiplayer behavior; prefer read-only telemetry unless the requested change is clearly within the documented write API scope.

## Agent Workflow

- Before editing, inspect the relevant source and docs instead of guessing from names.
- Keep changes narrow and explain any architectural tradeoff involving managed-only loading, future native telemetry split, Harmony patches, scene lifecycle, main-thread execution, or native memory.
- If a command cannot run because local SimRail/MelonLoader dependencies are missing, report that exact blocker rather than masking it with unrelated changes.
- Do not revert user changes unless explicitly asked.
