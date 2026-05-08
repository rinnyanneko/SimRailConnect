<!-- SPDX-License-Identifier: GPL-3.0-or-later -->

# AGENTS.md

Repository guidance for AI coding agents working on SimRailConnect.

## Project Overview

SimRailConnect is a .NET 6 C# MelonLoader mod for SimRail plugin developers. It exposes read-only SimRail telemetry through a local WebSocket API for external simulation, safety-system, display, and hardware-integration tooling.

The current mod reads Pyscreen telemetry from `VehiclePyscreenDataSource` on the Unity main thread. It also supports queued write paths for SimRail common driver input slots, `SetNoPowerAndBrake`, and Pyscreen command groups. It does not use Harmony patches, IL2CPP class injection, direct `Marshal` reads, or background-thread Unity access.

## Repository Layout

- `SimRail.sln` - Visual Studio solution.
- `src/SimRailConnect/SimRailConnect.csproj` - .NET 6 project file and game-reference paths.
- `src/SimRailConnect/Plugin.cs` - MelonLoader mod entry point, preferences, scene invalidation, and WebSocket startup.
- `src/SimRailConnect/PyscreenTelemetryCollector.cs` - read-only main-thread telemetry collector.
- `src/SimRailConnect/TelemetryCommandQueue.cs` - WebSocket-to-main-thread command queue.
- `src/SimRailConnect/WebSocketApiServer.cs` - localhost WebSocket telemetry push and request/response handling.
- `src/SimRailConnect/TelemetryState.cs` - shared state between Unity main thread and WebSocket background threads.
- `src/SimRailConnect/Models.cs` - JSON response/request models.
- `README.md` - user-facing overview, build, install, configuration, and API summary.
- `WEBSOCKET_API.md` - detailed WebSocket message contract.

## Build and Test

- Primary build command: `dotnet build SimRail.sln -p:GameDir="F:\SteamLibrary\steamapps\common\SimRail"`.
- Real telemetry builds require the .NET 6 SDK, `$(GameDir)\MelonLoader\net6\MelonLoader.dll`, and generated wrappers under `$(GameDir)\MelonLoader\Il2CppAssemblies`.
- `GameDir` is set in `src/SimRailConnect/SimRailConnect.csproj`; do not assume it is valid on every machine.
- The assembly is a `MelonMod`; deploy it to `<GameDir>/Mods`, not `<GameDir>/Plugins`.
- If build references fail, inspect `GameDir` and `MelonLoader/Il2CppAssemblies` before changing code.
- There is currently no dedicated automated test project. For behavioral changes, at minimum run the primary build command and document any blocker.

## Architecture Constraints

- Do not reintroduce `ClassInjector.RegisterTypeInIl2Cpp<T>()` or new IL2CPP class injection.
- Do not add Harmony patches unless the user explicitly asks and the change gets a fresh safety review.
- Do not access Unity, IL2CPP wrappers, native pointers, or game objects from `WebSocketApiServer`.
- Keep telemetry collection on the Unity main thread, preferably in `MelonMod.OnUpdate`.
- Keep write commands queued from WebSocket handlers and drained on the Unity main thread.
- Keep driver-control write support limited to documented `Input_General`, `SetNoPowerAndBrake`, and Pyscreen command-array operations unless the user asks for a fresh train-specific controller integration.
- Avoid `FindObjectOfType` or broad Unity scans on the hot telemetry path. Discovery scans must be throttled and cached.
- Invalidate cached IL2CPP/native references on scene load/unload and after repeated read failures.
- Avoid unsafe code, pointer arithmetic, direct array writes, and `Marshal` reads unless explicitly reviewed.

## Threading and Native Interop Rules

- Native-memory and IL2CPP object access must happen on the Unity main thread.
- WebSocket handlers run on background threads. They may read `TelemetryState.CurrentSnapshot` only.
- WebSocket `command` and `invalidate` enqueue work only; they must not touch Unity or IL2CPP directly.
- WebSocket `debug` returns `NATIVE_TELEMETRY_DISABLED` in this build.
- Shared WebSocket/telemetry state should stay simple and safe for cross-thread reads.

## API and Documentation

- When changing endpoint behavior, update `README.md` and `WEBSOCKET_API.md`.
- Preserve JSON field names unless a breaking change is explicitly requested.
- Keep WebSocket-only behavior explicit: the default WebSocket URL is `ws://localhost:5556/ws`.

## Coding Style

- Use C# with nullable reference types enabled.
- Follow the existing file style: GPL header, explicit `using` directives, file-scoped namespace, and clear comments for lifecycle/interoperability decisions.
- Do not introduce broad dependencies unless necessary for the MelonLoader runtime.
- Avoid changes that increase per-tick allocations or blocking work in the telemetry loop.

## Safety, Legal, and Scope

- This repository is for interoperability, simulation research, safety-system fidelity, custom displays, and hardware interfaces.
- Do not add features intended for cheating, multiplayer advantage, DRM bypass, unauthorized content distribution, or interference with other players.
- Preserve GPLv3 licensing notices in source files and derivative code.
- Prefer read-only telemetry unless the requested change is clearly within a reviewed write API scope.

## Agent Workflow

- Before editing, inspect the relevant source and docs instead of guessing from names.
- Keep changes narrow and explain tradeoffs involving MelonLoader loading, IL2CPP wrappers, scene lifecycle, main-thread execution, or native memory.
- If a command cannot run because local SimRail/MelonLoader dependencies are missing, report that exact blocker.
- Do not revert user changes unless explicitly asked.
