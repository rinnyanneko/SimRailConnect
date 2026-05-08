# SimRailConnect

SimRailConnect is a managed-only .NET 6 MelonLoader plugin experiment for SimRail plugin developers. It exposes a local WebSocket API that other tools can consume while telemetry providers are developed separately.

The core build does not include Harmony, Unity, IL2CPP, native telemetry, native cache diagnostics, or write-command support. It starts the WebSocket API and publishes an inactive baseline snapshot until a provider writes telemetry through `TelemetryState.PublishSnapshot`.

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

Provider-dependent messages return `NATIVE_TELEMETRY_DISABLED` until a native/provider assembly implements them:

- `command`
- `debug`
- `invalidate`

See [WEBSOCKET_API.md](WEBSOCKET_API.md) for the intended WebSocket contract.

## Build

Prerequisites:

- .NET 6 SDK
- For a real loadable plugin build: MelonLoader installed in SimRail and `$(GameDir)\MelonLoader\net6\MelonLoader.dll`
- For API/client development only: no SimRail install is required; the project compiles with local MelonLoader stubs and emits a warning

Build for API/client development:

```bash
dotnet build SimRail.sln
```

Build a real MelonLoader plugin by overriding the game path:

```bash
dotnet build SimRail.sln -p:GameDir="F:\SteamLibrary\steamapps\common\SimRail"
```

Release build:

```bash
dotnet build SimRail.sln -c Release -p:GameDir="F:\SteamLibrary\steamapps\common\SimRail"
```

The build does not copy `SimRailConnect.dll` into the game directory.

## Install

Build against the real MelonLoader assembly, then put `SimRailConnect.dll` into `<SimRail>\Plugins\`.

The known SimRail/MelonLoader IL2CPP support-module crash is treated as an upstream/runtime issue for plugin development. This repository does not auto-deploy the DLL; install manually when testing the runtime.

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
