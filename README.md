# SimRailConnect: High-Fidelity Telemetry Bridge

SimRailConnect is a MelonLoader plugin that exposes SimRail telemetry through a local WebSocket API. The current default is a WebSocket-only safe mode with native telemetry disabled, because some SimRail/MelonLoader IL2CPP scene transitions can crash inside Il2CppInterop's field-default hook.

---

## Legal Disclosure & Compliance

### 1. Purpose of Interoperability
Under Article 80-2, Paragraph 3, Item 8 of the Taiwan Copyright Act, reverse engineering is permitted for achieving Interoperability between independently created software. This project acts as a bridge for external safety logic and monitoring tools to communicate with the game engine.

### 2. EULA Compliance & Necessity
This work falls under the "except as expressly permitted by applicable law" clause of the SimRail EULA. Reverse engineering (via MelonLoader injection) is a technical necessity to identify undocumented telemetry data structures within the IL2CPP environment (specifically `VehiclePyscreenDataSource`) required for high-fidelity safety system simulation.

### 3. Non-Destructive Implementation
The current managed-only plugin does not modify original game binaries on disk and does not include Harmony, Unity, or IL2CPP references. Native telemetry work remains isolated because the current SimRail/MelonLoader IL2CPP support path crashes on some scene transitions.

### ⚠️ IMPORTANT: PROHIBITED USES
- **No Cheating**: Strictly for simulation and research purposes. Using this tool to gain unfair advantages or interfere with other players in multiplayer mode is prohibited.
- **No Illegal Activity**: Do not use this tool to bypass DRM or distribute unauthorized game content.
- **User Responsibility**: Any misuse in multiplayer sessions may result in account bans. The maintainer accepts no liability for third-party claims or damages arising from the use of this repository.

---

## For Developers

### Architecture Overview
| Component | Role |
| :--- | :--- |
| **Plugin** (`Plugin.cs`) | MelonLoader plugin entry-point; manages preferences and WebSocket startup |
| **TelemetryMonitor** (`TelemetryMonitor.cs`) | Native telemetry prototype; excluded from the managed-only plugin build |
| **GameBridge** (`GameBridge.cs`) | Native telemetry prototype; excluded from the managed-only plugin build |
| **WebSocketApiServer** (`WebSocketApiServer.cs`) | Localhost WebSocket server for telemetry push, snapshot request/response, ping/pong, and queued commands |
| **ApiCommandRegistry** (`ApiCommandRegistry.cs`) | Network-thread-safe command whitelist, type validation, and range validation |
| **Models** (`Models.cs`) | Structured data model: `TelemetrySnapshot` and all sub-types |
| **TelemetryState** (`TelemetryState.cs`) | Shared volatile state between the Unity main thread and WebSocket background threads |

### Why Native Telemetry Is Disabled
`ClassInjector.RegisterTypeInIl2Cpp<T>()` and MelonLoader's IL2CPP support-module injection use `Class_GetFieldDefaultValue_Hook`, which can crash during specific SimRail scene transitions. The current plugin build therefore excludes Harmony, Unity, IL2CPP, `Assembly-CSharp`, `GameBridge`, `TelemetryMonitor`, and `ApiCommandRegistry` entirely.

### Current Safe Mode
`EnableTelemetryPatch` defaults to `false` and is currently ignored by the managed-only plugin build. The WebSocket server starts, but native telemetry, debug, invalidation, and write commands return `NATIVE_TELEMETRY_DISABLED`.

### The Write API Strategy
WebSocket `command` messages are designed specifically for Safety System implementation.
- **Intent**: To allow external logic to interact with dashboard indicators, reset safety timers, or trigger emergency braking sequences.
- **Technical Note**: Writing to Pyscreen arrays typically modifies **display/dashboard values** and may **not** modify the actual physical simulation state.
- **Execution Model**: Writes are queued from WebSocket background threads and executed on the Unity main thread at the next telemetry tick (~100 ms). Requests fail immediately if no active train snapshot exists; queued writes can still be skipped if the target array is unavailable by the time the main-thread tick runs.

---

## API Documentation

WebSocket URL: `ws://localhost:5556/ws` | Default push rate: `10Hz`

### WebSocket Quick Example

Subscribe:
```json
{
  "type": "subscribe",
  "id": "sub-001",
  "channels": ["train", "brakes", "doors", "safety"],
  "rateHz": 10
}
```

Command:
```json
{
  "type": "command",
  "id": "cmd-001",
  "target": "brakes",
  "action": "set_brake",
  "value": 4
}
```

Commands receive an immediate queued `ack`, then a later `commandResult` after the Unity main-thread telemetry tick applies or rejects the write.

Field-style command payload:
```json
{
  "type": "command",
  "id": "cmd-002",
  "target": "generalBool",
  "field": "shp",
  "value": true
}
```

Check [WEBSOCKET_API.md](WEBSOCKET_API.md) for more detail.

---

## Build & Install

### Prerequisites
- **.NET 6.0 SDK**
- **MelonLoader v0.6.x or later** (IL2CPP build) installed into SimRail
- **Cpp2IL ≥ 2022.1.0-pre-release.21** (see note below)

### ⚠️ Cpp2IL Compatibility Issue

MelonLoader bundles Cpp2IL `2022.1.0-pre-release.12`, which **fails to parse SimRail's GameAssembly.dll** (IL2CPP metadata version 29.1) with:

```
OverflowException: Provided address, 0x8D8A, was less than image base, 0x180000000
```

This prevents MelonLoader from generating the `Il2CppAssemblies\` folder on first run, causing all game types to be unresolvable. **You must manually replace the bundled Cpp2IL with `pre-release.21`** before the first launch:

1. Download `Cpp2IL-2022.1.0-pre-release.21-Windows.exe` from the [Cpp2IL releases page](https://github.com/SamboyCoding/Cpp2IL/releases/tag/2022.1.0-pre-release.21)
2. Rename it to `Cpp2IL.exe` and copy it to:
   ```
   <SimRail>\MelonLoader\Dependencies\Il2CppAssemblyGenerator\Cpp2IL\
   ```
   (overwrite the existing `Cpp2IL.exe`)
3. Launch SimRail once — MelonLoader will regenerate all interop assemblies under `<SimRail>\MelonLoader\Il2CppAssemblies\` using the fixed binary (~10 seconds)
4. Exit and proceed to build

This is a one-time setup. Once `Il2CppAssemblies\` exists, subsequent builds and launches work normally.

### Build
```bash
dotnet build -c Release
```
The build output is written to `src/SimRailConnect/bin/Release/net6.0/SimRailConnect.dll`.  
If a `Plugins/` folder already exists in your SimRail directory the post-build target copies the DLL there automatically.

### Deploy (manual)
1. Copy `SimRailConnect.dll` to `<SimRail>\Plugins\`.
2. Remove any stale `SimRailConnect.dll` from `<SimRail>\Mods\`.
3. Launch SimRail — MelonLoader will load the plugin automatically.

### Configure
Edit `<SimRail>\UserData\MelonPreferences.cfg` under the `[SimRailConnect]` section:

| Key | Default | Description |
| :--- | :--- | :--- |
| `UpdateIntervalMs` | `100` | Telemetry poll interval in milliseconds |
| `WebSocketPort` | `5556` | WebSocket API server port |
| `WebSocketMaxClients` | `3` | Maximum concurrent WebSocket clients |
| `WebSocketDefaultRateHz` | `10` | Default telemetry push rate |
| `WebSocketMaxRateHz` | `20` | Maximum per-client push rate |
| `WebSocketPayloadLimitBytes` | `16384` | Maximum inbound WebSocket JSON size |
| `WebSocketCommandRateLimitPerSecond` | `5` | Per-client command rate limit |
| `WebSocketReadOnly` | `false` | Disable WebSocket commands while keeping telemetry push |
| `EnableTelemetryPatch` | `false` | Reserved for a future separate native telemetry assembly; ignored by this managed-only plugin |
| `ApiToken` | empty | Optional WebSocket token; pass as `?token=...` or `Authorization: Bearer ...` |

### Logs
MelonLoader writes all mod output (including SimRailConnect messages) to:
```
<SimRail>\MelonLoader\Latest.log
```

---

## License & Support

### 1. License
This project is released under the GNU General Public License v3 (GPLv3). Any derivative works or modifications must remain open-source under the same license.

### 2. AI Disclosure & Bug Warning
Parts of this project (including documentation and code structures) were authored or optimized with the assistance of AI tools. While tested, there remains a possibility of unexpected bugs or behavior. Users should review the code before deployment in critical environments.

### 3. Legal Jurisdiction
Any disputes arising from this project shall be governed by the laws of Taiwan (R.O.C.), with the Taiwan Taichung District Court as the court of first instance.

### 4. Contact & Rights Inquiries
If you believe this project infringes upon your rights or requires explicit authorization evidence, please contact:
- **Email**: **support@mirukuneko.cc**
- **Issues**: Open an Issue on this repository.

### 5. Support
If you find this tool useful and wish to support further development, please visit:
- **Donate**: [https://mirukuneko.cc/donate](https://mirukuneko.cc/donate)

---
*Disclaimer: Not an official SimRail product. Developed for simulation research and safety system fidelity.*

