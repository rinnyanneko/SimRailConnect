# SimRailConnect: High-Fidelity Telemetry Bridge

SimRailConnect is a MelonLoader mod that exposes SimRail's internal telemetry via a local HTTP REST API. This project provides a bridge for developers to build external safety systems, custom displays, or hardware interfaces by accessing deep-level data (such as brake pressures and traction force) not available through the official public API.

---

## Legal Disclosure & Compliance

### 1. Purpose of Interoperability
Under Article 80-2, Paragraph 3, Item 8 of the Taiwan Copyright Act, reverse engineering is permitted for achieving Interoperability between independently created software. This project acts as a bridge for external safety logic and monitoring tools to communicate with the game engine.

### 2. EULA Compliance & Necessity
This work falls under the "except as expressly permitted by applicable law" clause of the SimRail EULA. Reverse engineering (via MelonLoader injection) is a technical necessity to identify undocumented telemetry data structures within the IL2CPP environment (specifically `VehiclePyscreenDataSource`) required for high-fidelity safety system simulation.

### 3. Non-Destructive Implementation
This mod utilises Memory Injection (Hooking) via HarmonyX and does not modify original game binaries on disk. It complies with Art. 80-1 regarding Copyright Management Information.

### ⚠️ IMPORTANT: PROHIBITED USES
- **No Cheating**: Strictly for simulation and research purposes. Using this tool to gain unfair advantages or interfere with other players in multiplayer mode is prohibited.
- **No Illegal Activity**: Do not use this tool to bypass DRM or distribute unauthorized game content.
- **User Responsibility**: Any misuse in multiplayer sessions may result in account bans. The maintainer accepts no liability for third-party claims or damages arising from the use of this repository.

---

## For Developers

### Architecture Overview
| Component | Role |
| :--- | :--- |
| **Plugin** (`Plugin.cs`) | MelonLoader mod entry-point; manages lifecycle, preferences, scene events, and Harmony patching |
| **TelemetryMonitor** (`TelemetryMonitor.cs`) | HarmonyX postfix on `Pyscreen.Update()` — drives the telemetry collection loop on the Unity main thread |
| **GameBridge** (`GameBridge.cs`) | IL2CPP interop layer; reads typed `VehiclePyscreenDataSource` sub-object arrays (`generalFloat`, `generalInt`, `generalBool`, etc.) via direct memory access |
| **HttpApiServer** (`HttpApiServer.cs`) | Background HTTP listener serving JSON telemetry endpoints |
| **Models** (`Models.cs`) | Structured data model: `TelemetrySnapshot` and all sub-types |
| **TelemetryState** (`TelemetryState.cs`) | Shared volatile state between the Unity main thread and the HTTP background thread |

### Why HarmonyX instead of ClassInjector?
`ClassInjector.RegisterTypeInIl2Cpp<T>()` installs a global `Class_GetFieldDefaultValue_Hook` that intercepts field-metadata queries for every class during every scene load. On specific SimRail scene transitions (train approaching the player, spawn-outside-cab missions) the `Il2CppFieldInfo*` stored by that hook becomes a dangling pointer, causing an `AccessViolationException`. Removing `ClassInjector` entirely eliminates the hook and the crash.

### The Write API Strategy
The `POST /api/write` endpoint is designed specifically for Safety System implementation.
- **Intent**: To allow external logic to interact with dashboard indicators, reset safety timers, or trigger emergency braking sequences.
- **Technical Note**: Writing to Pyscreen arrays typically modifies **display/dashboard values** and may **not** modify the actual physical simulation state.
- **Execution Model**: Writes are queued from the HTTP thread and executed on the Unity main thread at the next telemetry tick (~100 ms). Requests fail immediately if no active train snapshot exists; queued writes can still be skipped if the target array is unavailable by the time the main-thread tick runs.

---

## API Documentation

Base URL: `http://localhost:5555` | Format: `JSON` (Use `?pretty=true` for formatted output)

### Read Endpoints (GET)

| Endpoint | Description | Key Data Points |
| :--- | :--- | :--- |
| `/api/telemetry` | Full snapshot | All subsystems (Train, Brakes, Safety, etc.) |
| `/api/train` | Movement data | `velocity`, `distance`, `direction` |
| `/api/brakes` | Brake pressures | `bc`, `bp`, `sp`, `cp` (in bar) |
| `/api/electrical` | Traction data | `voltage`, `tractionforce`, `power`, `rpm` |
| `/api/safety` | Safety Systems | `shp`, `ca`, `alarm_active` |
| `/api/doors` | Door states | Door/doorstep states, slip detection |
| `/api/controls` | Driver inputs | `throttle` (mainctrl_pos), `speed_control`, `lights` |
| `/api/station` | Timetable info | Next station and schedule fields |
| `/api/environment`| World data | Game time, radio channel, brightness |
| `/api/invalidate` | System Reset | Force rescan of game object references |
| `/api/debug` | Diagnostics | Native cache state, array lengths, raw samples |

### Write Endpoint (POST)

| Endpoint | Requirement | Intended Use |
| :--- | :--- | :--- |
| `/api/write` | `EnableWriteApi=true` | **Safety System Intervention only**: Triggering brakes, resetting safety timers, or dashboard alerts. |

`/api/invalidate`, `/api/debug`, and queued writes never access native IL2CPP memory directly from the HTTP background thread. They schedule work that is drained from the `Pyscreen.Update()` telemetry tick on the Unity main thread.

**Example Payload**:
```json
{
  "target": "generalBool",
  "field": "shp",
  "value": true
}
```

Check [API_DOCUMENTATION.md](API_DOCUMENTATION.md) for more details.

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
If a `Mods/` folder already exists in your SimRail directory the post-build target copies the DLL there automatically.

### Deploy (manual)
1. Copy `SimRailConnect.dll` to `<SimRail>\Mods\`.
2. Launch SimRail — MelonLoader will load the mod automatically.

### Configure
Edit `<SimRail>\UserData\MelonPreferences.cfg` under the `[SimRailConnect]` section:

| Key | Default | Description |
| :--- | :--- | :--- |
| `Port` | `5555` | HTTP API server port |
| `UpdateIntervalMs` | `100` | Telemetry poll interval in milliseconds |
| `EnableWriteApi` | `true` | Enable `POST /api/write` |

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

