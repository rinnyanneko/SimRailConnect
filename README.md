# SimRailConnect

SimRailConnect is a managed-only .NET 6 MelonLoader plugin experiment for SimRail. It is not currently safe to install into the game process with the tested SimRail/MelonLoader IL2CPP runtime.

The current build does not include Harmony, Unity, IL2CPP, native telemetry, native cache diagnostics, or write-command support. The tested MelonLoader IL2CPP support module can still crash after loading an otherwise managed-only plugin, so builds are not auto-deployed to SimRail.

## Safety Scope

This project is for interoperability, simulation research, safety-system fidelity, custom displays, and hardware interfaces.

Prohibited uses:

- Cheating or gaining multiplayer advantage
- DRM bypass or unauthorized content distribution
- Interference with other players

## Current API Behavior

Supported messages:

- `ping`
- `subscribe`
- `unsubscribe`
- `getSnapshot`

Native-dependent messages return `NATIVE_TELEMETRY_DISABLED`:

- `command`
- `debug`
- `invalidate`

See [WEBSOCKET_API.md](WEBSOCKET_API.md) for the intended WebSocket contract.

## Build

Prerequisites:

- .NET 6 SDK
- MelonLoader installed in SimRail
- `$(GameDir)\MelonLoader\net6\MelonLoader.dll`

Build with the default `GameDir` from the project file:

```bash
dotnet build SimRail.sln
```

Or override the game path:

```bash
dotnet build SimRail.sln -p:GameDir="F:\SteamLibrary\steamapps\common\SimRail"
```

Release build:

```bash
dotnet build SimRail.sln -c Release -p:GameDir="F:\SteamLibrary\steamapps\common\SimRail"
```

The build does not copy `SimRailConnect.dll` into the game directory.

## Install

Simpily put `SimRailConnect.dll` into `<SimRail>\Mods\`

## Configure

Edit `<SimRail>\UserData\MelonPreferences.cfg` under `[SimRailConnect]`.

| Key | Default | Description |
| :--- | :--- | :--- |
| `UpdateIntervalMs` | `100` | Reserved telemetry interval |
| `WebSocketPort` | `5556` | WebSocket API server port |
| `WebSocketMaxClients` | `3` | Maximum concurrent WebSocket clients |
| `WebSocketDefaultRateHz` | `10` | Default push rate |
| `WebSocketMaxRateHz` | `20` | Maximum per-client push rate |
| `WebSocketPayloadLimitBytes` | `16384` | Maximum inbound JSON payload size |
| `EnableTelemetryPatch` | `false` | Ignored by this managed-only build |
| `ApiToken` | empty | Optional token via `?token=...` or `Authorization: Bearer ...` |

## Logs

MelonLoader writes logs to:

```text
<SimRail>\MelonLoader\Latest.log
```

## License

SimRailConnect is released under GPLv3. See [LICENSE](LICENSE).

Not an official SimRail product.
