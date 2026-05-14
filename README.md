<!-- SPDX-License-Identifier: GPL-3.0-or-later -->

# SimRailConnect

SimRailConnect is a .NET 6 MelonLoader mod for SimRail plugin developers. It reads SimRail Pyscreen telemetry on the Unity main thread and exposes snapshots through a local WebSocket API.

Default WebSocket URL: `ws://localhost:5556/ws`

## Scope

This project is for interoperability, simulation research, safety-system fidelity, custom displays, and hardware interfaces. Do not use it for cheating, multiplayer advantage, DRM bypass, unauthorized content distribution, or interfering with other players.

## What Works

- Loads as a `MelonMod` from `<SimRail>\Mods\SimRailConnect.dll`
- Starts the WebSocket API on localhost
- Publishes read-only train telemetry from `VehiclePyscreenDataSource`
- Publishes best-effort non-ETCS next-signal metadata from the current track scan, including distance, speed metadata, and inferred color when available
- Queues limited Pyscreen command writes from WebSocket clients
- Keeps Unity and IL2CPP object access on the Unity main thread
- Lets WebSocket clients read `TelemetryState.CurrentSnapshot`

Write commands are queued and applied on the Unity main thread. Named driver commands use SimRail's common `Input_General` slots and `SetNoPowerAndBrake` where available. Raw Pyscreen writes to `eimpcBool`, `eimpcInt`, and `eimpcFloat` remain available for plugin experiments. Native debug inspection remains disabled and returns `NATIVE_TELEMETRY_DISABLED`.

## Install from release

Prerequisites:

- .NET 6 SDK
- SimRail with [MelonLoader v0.7.3](https://nightly.link/LavaGang/MelonLoader/workflows/build/master) or above installed.
- Set SimRail startup command `--melonloader.unityversion 2023.1.8f1`.
- Run game once after MelonLoader installed.

Installation:

Download SimRailConnect.dll from [Releases](releases).

Copy `SimRailConnect.dll` into the game's Mods folder:

```text
<GameDir>\Mods\SimRailConnect.dll
```

For example:

```text
F:\SteamLibrary\steamapps\common\SimRail\Mods\SimRailConnect.dll
```

On startup, MelonLoader should report that the assembly loaded from `.\Mods\SimRailConnect.dll` and that `1 Mod loaded`.

## Build

Prerequisites:

- .NET 6 SDK
- SimRail with [MelonLoader v0.7.3](https://nightly.link/LavaGang/MelonLoader/workflows/build/master) or above installed
- Set SimRail startup command `--melonloader.unityversion 2023.1.8f1`
- Generated assemblies under `<SimRail>\MelonLoader\Il2CppAssemblies`

Build against the local game path:

```bash
dotnet build SimRail.sln -p:GameDir="X:\SteamLibrary\steamapps\common\SimRail"
```

Release build:

```bash
dotnet build SimRail.sln -c Release -p:GameDir="X:\SteamLibrary\steamapps\common\SimRail"
```

When `GameDir` points at a valid MelonLoader install, the build copies `SimRailConnect.dll` into `<GameDir>\Mods\`.

## Configure

Edit `<SimRail>\UserData\MelonPreferences.cfg` under `[SimRailConnect]`.

| Key | Default | Description |
| :--- | :--- | :--- |
| `UpdateIntervalMs` | `100` | Main-thread telemetry polling interval |
| `WebSocketPort` | `5556` | WebSocket API server port |
| `WebSocketMaxClients` | `3` | Maximum concurrent WebSocket clients |
| `WebSocketDefaultRateHz` | `10` | Default push rate |
| `WebSocketMaxRateHz` | `20` | Maximum per-client push rate |
| `WebSocketPayloadLimitBytes` | `16384` | Maximum inbound JSON payload size |
| `EnablePyscreenTelemetry` | `true` | Enables the read-only Pyscreen telemetry collector |
| `ApiToken` | empty | Optional token via `?token=...` or `Authorization: Bearer ...` |

## Logs

MelonLoader writes logs to:

```text
<SimRail>\MelonLoader\Latest.log
```

Useful SimRailConnect log lines include the detected game path, detected `Il2CppAssemblies` path, WebSocket URL, scene load/unload, telemetry cache invalidation, and Pyscreen source discovery.

If the log shows `Melon Assembly loaded: '.\Mods\SimRailConnect.dll'` followed by `0 Mods loaded`, the DLL was not built against the real net6 MelonLoader assemblies. Rebuild with `GameDir` pointing at the SimRail install so the output inherits from `MelonLoader.MelonMod` in `<SimRail>\MelonLoader\net6\MelonLoader.dll`.

## API

See [WEBSOCKET_API.md](WEBSOCKET_API.md).

## License

SimRailConnect is released under GPLv3. See [LICENSE](LICENSE).

Not an official SimRail product.
