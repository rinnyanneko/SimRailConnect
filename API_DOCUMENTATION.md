<!--
  SimRailConnect API Documentation
  Copyright © 2026 rinnyanneko — GPLv3
  Language: English (primary) · 繁體中文 (Traditional Chinese)
-->

# SimRailConnect — API Documentation

> **Base URL:** `http://localhost:5555`  (default; see [Configuration](#7-configuration--設定))

> **Format:** JSON — append `?pretty=true` to any endpoint for human-readable output

> **CORS:** All origins accepted (`Access-Control-Allow-Origin: *`)

---

## Table of Contents

**English**
1. [Overview](#1-overview)
2. [Quick Start](#2-quick-start)
3. [Endpoint Reference](#3-endpoint-reference)
4. [Data Models Reference](#4-data-models-reference)
5. [Write API](#5-write-api)
6. [Error Handling](#6-error-handling)
7. [Configuration](#7-configuration)
8. [Integration Example — Live Monitor](#8-integration-example--live-monitor)

---

## 1. Overview

SimRailConnect is a **MelonLoader mod** for **SimRail — The Railway Simulator** that exposes the game's internal telemetry data through a local HTTP REST API. It is designed for developers building:

- External **safety system simulations** (SHP, CA, ETCS overrides)
- Custom **cab displays**, speedometers, or brake gauges
- **Hardware interfaces** (Arduino panels, LED indicators)
- **Data-logging** and replay tools
- **Automation** and testing scripts

The mod samples internal `VehiclePyscreenDataSource` typed sub-object arrays on the Unity main thread at a configurable interval (default 100 ms) and makes them available over HTTP.

### Architecture at a glance

| Component | Role |
|---|---|
| `Plugin` | MelonLoader mod entry-point; manages lifecycle, preferences, scene events, and Harmony patching |
| `TelemetryMonitor` | HarmonyX postfix on `Pyscreen.Update()` — samples game state on the Unity main thread |
| `GameBridge` | IL2CPP interop — reads typed `VehiclePyscreenDataSource` sub-object arrays and queues writes/debug work for the Unity main thread |
| `HttpApiServer` | Background `HttpListener` — serves JSON endpoints |
| `Models` | C# POCOs serialised with `System.Text.Json` (camelCase) |

### Important note on the Write API

Writing via `POST /api/write` modifies **Pyscreen display/dashboard arrays**. This typically affects indicator lights and gauge readings visible on the cab screen. It may **not** alter the underlying physics simulation state. Write requests are accepted on the HTTP background thread, then applied on the Unity main thread at the next telemetry tick. The Write API is intended for safety system research (e.g. triggering emergency-brake indicators, resetting SHP timers) — **not** for gaining unfair advantages in multiplayer.

---

## 2. Quick Start

### Prerequisites

- SimRail installed with **MelonLoader 0.6.x** (IL2CPP build) configured
- `SimRailConnect.dll` placed in `SimRail\Mods\`
- Start SimRail and load into a train cab — the mod starts its HTTP server automatically

### Verify the server is running

```sh
curl http://localhost:5555/api
```

Expected response:

```json
{
  "name": "SimRailConnect API",
  "version": "1.0.0",
  "endpoints": {
    "GET /api/telemetry": "Complete telemetry snapshot",
    "GET /api/train": "Train movement data (speed, distance, direction)",
    "GET /api/brakes": "Brake pressures (BC, BP, SP, CP)",
    "GET /api/electrical": "Electrical/traction data (voltage, power, RPM)",
    "GET /api/safety": "Safety systems (SHP, CA, alarms)",
    "GET /api/doors": "Door and doorstep states",
    "GET /api/controls": "Driver control positions",
    "GET /api/station": "Next station information",
    "GET /api/environment": "Time, weather, radio",
    "POST /api/write": "Write value to telemetry (JSON body: {target, field, value})",
    "GET /api/invalidate": "Invalidate cached game object references",
    "GET /api/debug": "Native cache diagnostics (data offsets, array lengths, raw samples)"
  },
  "hint": "Add ?pretty=true to any endpoint for formatted JSON"
}
```

### curl examples

```sh
# Full telemetry snapshot (pretty-printed)
curl "http://localhost:5555/api/telemetry?pretty=true"

# Speed and distance only
curl "http://localhost:5555/api/train?pretty=true"

# Brake pressures
curl "http://localhost:5555/api/brakes?pretty=true"

# Trigger SHP safety system via Write API
curl -X POST http://localhost:5555/api/write \
  -H "Content-Type: application/json" \
  -d "{\"target\":\"generalBool\",\"field\":\"shp\",\"value\":true}"

# Force rescan after switching trains
curl http://localhost:5555/api/invalidate
```

### Python `requests` examples

```python
import requests

BASE = "http://localhost:5555"

# --- Read full telemetry ---
resp = requests.get(f"{BASE}/api/telemetry")
data = resp.json()
train = data["data"]["train"]
print(f"Speed: {train['velocity']:.1f} km/h  |  Direction: {train['direction']}")

# --- Read brake pressures ---
resp = requests.get(f"{BASE}/api/brakes")
brakes = resp.json()["data"]
print(f"BC: {brakes['bc']:.2f} bar  |  BP: {brakes['bp']:.2f} bar")

# --- Write: activate sanding ---
payload = {"target": "generalBool", "field": "sanding", "value": True}
resp = requests.post(f"{BASE}/api/write", json=payload)
print(resp.json())

# --- Write: set throttle notch to 3 ---
payload = {"target": "generalInt", "field": "throttle", "value": 3}
resp = requests.post(f"{BASE}/api/write", json=payload)
print(resp.json())
```

---

## 3. Endpoint Reference

All responses are wrapped in an [`ApiResponse<T>`](#apiresponset) envelope.

### `GET /` or `GET /api` — Index

Returns a directory of all available endpoints and the running plugin version.

**Request**
```sh
GET http://localhost:5555/api
```

**Response** `200 OK`
```json
{
  "name": "SimRailConnect API",
  "version": "1.0.0",
  "endpoints": { "...": "..." },
  "hint": "Add ?pretty=true to any endpoint for formatted JSON"
}
```

> Note: The index endpoint returns a plain object, not an `ApiResponse` wrapper.

---

### `GET /api/telemetry` — Full Snapshot

Returns the complete [`TelemetrySnapshot`](#telemetrysnapshot) containing all subsystems in a single request. This is the most convenient endpoint if you need data from multiple subsystems.

**Request**
```sh
GET http://localhost:5555/api/telemetry?pretty=true
```

**Response** `200 OK` — train active
```json
{
  "success": true,
  "data": {
    "timestamp": "2026-03-15T14:22:05.123Z",
    "isActive": true,
    "train": {
      "velocity": 87.4,
      "velocityInt": 87,
      "newSpeed": 100.0,
      "distanceDriven": 12.345,
      "distanceCounter": 12.345,
      "trainLength": 193.5,
      "direction": 1,
      "cabinDirection": 1,
      "cab": 1,
      "unitNo": 0,
      "carNo": 0
    },
    "brakes": {
      "bc": 0.0,
      "bp": 5.0,
      "sp": 8.5,
      "cp": 5.0,
      "pneumaticBrakeStatus": 0.0,
      "springActive": false,
      "springShutoff": false,
      "edBrakeActive": 0,
      "brakeDelayFlag": 0
    },
    "electrical": {
      "voltage": 3000.0,
      "tractionForce": 112.4,
      "tractionPercent": 62.0,
      "motorFrequency": 38.2,
      "powerConsumption": 1840.0,
      "currentVoltageRatio": 0.61,
      "pantographPressure": 4.8,
      "converter": true,
      "acStatus": true,
      "battery": true,
      "combustionEngineActive": 0,
      "combustionEngineRPM": 0.0,
      "coolantTemperature": 0.0,
      "dieselMode": false
    },
    "safety": {
      "shp": false,
      "ca": false,
      "alarmActive": false,
      "fireDetectionActive": false,
      "absStatus": 0,
      "sagStatus": 0
    },
    "doors": {
      "doors": false,
      "doorsLeft": false,
      "doorsRight": false,
      "doorstepLeft": false,
      "doorstepRight": false,
      "slip": false,
      "brakesEngaged": false
    },
    "controls": {
      "mainCtrlPos": 3,
      "mainCtrlActualPos": 3,
      "speedCtrl": 0.0,
      "speedCtrlPower": 0.0,
      "speedCtrlActive": false,
      "speedCtrlStandby": false,
      "speedCtrlStatus": 0,
      "sanding": false,
      "soloDriveActive": 0,
      "lightsFront": 2,
      "lightsRear": 1,
      "lightsCompartments": false
    },
    "station": {
      "nextStation": "Warszawa Centralna",
      "distance": "12.3 km",
      "stationCount": 14
    },
    "environment": {
      "hours": 14,
      "minutes": 22,
      "seconds": 5,
      "day": 15,
      "month": 3,
      "year": 2026,
      "dayOfWeek": 0,
      "lightLevel": 0.82,
      "radioActive": true,
      "radioChannel": 3,
      "radioVolume": 0.75,
      "screenBrightness": 8
    }
  },
  "timestamp": "2026-03-15T14:22:05.131Z"
}
```

**Response** `200 OK` — no active train
```json
{
  "success": false,
  "error": "No telemetry data available. Is a train active?",
  "timestamp": "2026-03-15T14:22:05.131Z"
}
```

---

### `GET /api/train` — Train Movement Data

Returns [`TrainInfo`](#traininfo): speed, distance, direction, and cab identifiers.

**Request**
```sh
GET http://localhost:5555/api/train
```

**Response** `200 OK`
```json
{
  "success": true,
  "data": {
    "velocity": 87.4,
    "velocityInt": 87,
    "newSpeed": 100.0,
    "distanceDriven": 12.345,
    "distanceCounter": 12.345,
    "trainLength": 193.5,
    "direction": 1,
    "cabinDirection": 1,
    "cab": 1,
    "unitNo": 0,
    "carNo": 0
  },
  "timestamp": "2026-03-15T14:22:05.131Z"
}
```

---

### `GET /api/brakes` — Brake System

Returns [`BrakeInfo`](#brakeinfo): all pneumatic pressures, spring brake states, and electrodynamic brake status.

**Request**
```sh
GET http://localhost:5555/api/brakes
```

**Response** `200 OK` — train braking
```json
{
  "success": true,
  "data": {
    "bc": 2.4,
    "bp": 3.6,
    "sp": 8.5,
    "cp": 3.6,
    "pneumaticBrakeStatus": 1.0,
    "springActive": false,
    "springShutoff": false,
    "edBrakeActive": 1,
    "brakeDelayFlag": 0
  },
  "timestamp": "2026-03-15T14:22:05.131Z"
}
```

---

### `GET /api/electrical` — Electrical & Traction

Returns [`ElectricalInfo`](#electricalinfo): overhead line voltage, traction force/percentage, motor frequency, power consumption, pantograph, and diesel engine data.

**Request**
```sh
GET http://localhost:5555/api/electrical
```

**Response** `200 OK` — electric locomotive at traction
```json
{
  "success": true,
  "data": {
    "voltage": 3000.0,
    "tractionForce": 112.4,
    "tractionPercent": 62.0,
    "motorFrequency": 38.2,
    "powerConsumption": 1840.0,
    "currentVoltageRatio": 0.61,
    "pantographPressure": 4.8,
    "converter": true,
    "acStatus": true,
    "battery": true,
    "combustionEngineActive": 0,
    "combustionEngineRPM": 0.0,
    "coolantTemperature": 0.0,
    "dieselMode": false
  },
  "timestamp": "2026-03-15T14:22:05.131Z"
}
```

**Response** `200 OK` — diesel locomotive
```json
{
  "success": true,
  "data": {
    "voltage": 0.0,
    "tractionForce": 85.2,
    "tractionPercent": 45.0,
    "motorFrequency": 0.0,
    "powerConsumption": 620.0,
    "currentVoltageRatio": 0.0,
    "pantographPressure": 0.0,
    "converter": false,
    "acStatus": false,
    "battery": true,
    "combustionEngineActive": 1,
    "combustionEngineRPM": 1200.0,
    "coolantTemperature": 82.5,
    "dieselMode": true
  },
  "timestamp": "2026-03-15T14:22:05.131Z"
}
```

---

### `GET /api/safety` — Safety Systems

Returns [`SafetyInfo`](#safetyinfo): SHP/CA vigilance system states, alarm, fire detection, ABS and SAG statuses.

**Request**
```sh
GET http://localhost:5555/api/safety
```

**Response** `200 OK` — SHP alarm triggered
```json
{
  "success": true,
  "data": {
    "shp": true,
    "ca": false,
    "alarmActive": true,
    "fireDetectionActive": false,
    "absStatus": 0,
    "sagStatus": 0
  },
  "timestamp": "2026-03-15T14:22:05.131Z"
}
```

---

### `GET /api/doors` — Door States

Returns [`DoorInfo`](#doorinfo): individual door and doorstep states for left/right sides, wheel slip detection, and EMU brake engagement.

**Request**
```sh
GET http://localhost:5555/api/doors
```

**Response** `200 OK` — left doors open at station
```json
{
  "success": true,
  "data": {
    "doors": true,
    "doorsLeft": true,
    "doorsRight": false,
    "doorstepLeft": true,
    "doorstepRight": false,
    "slip": false,
    "brakesEngaged": true
  },
  "timestamp": "2026-03-15T14:22:05.131Z"
}
```

---

### `GET /api/controls` — Driver Controls

Returns [`ControlInfo`](#controlinfo): throttle notch position, speed controller setpoint/status, sanding, lights, and solo-drive state.

**Request**
```sh
GET http://localhost:5555/api/controls
```

**Response** `200 OK`
```json
{
  "success": true,
  "data": {
    "mainCtrlPos": 3,
    "mainCtrlActualPos": 3,
    "speedCtrl": 120.0,
    "speedCtrlPower": 0.85,
    "speedCtrlActive": true,
    "speedCtrlStandby": false,
    "speedCtrlStatus": 1,
    "sanding": false,
    "soloDriveActive": 0,
    "lightsFront": 2,
    "lightsRear": 1,
    "lightsCompartments": false
  },
  "timestamp": "2026-03-15T14:22:05.131Z"
}
```

---

### `GET /api/station` — Station Information

Returns [`StationInfo`](#stationinfo): the name and distance to the next timetable station, and the total number of stations on the active roster.

**Request**
```sh
GET http://localhost:5555/api/station
```

**Response** `200 OK`
```json
{
  "success": true,
  "data": {
    "nextStation": "Warszawa Centralna",
    "distance": "12.3 km",
    "stationCount": 14
  },
  "timestamp": "2026-03-15T14:22:05.131Z"
}
```

---

### `GET /api/environment` — Environment & Time

Returns [`EnvironmentInfo`](#environmentinfo): the in-game clock and calendar date, cabin light level, radio state/channel/volume, and screen brightness.

**Request**
```sh
GET http://localhost:5555/api/environment
```

**Response** `200 OK`
```json
{
  "success": true,
  "data": {
    "hours": 14,
    "minutes": 22,
    "seconds": 5,
    "day": 15,
    "month": 3,
    "year": 2026,
    "dayOfWeek": 0,
    "lightLevel": 0.82,
    "radioActive": true,
    "radioChannel": 3,
    "radioVolume": 0.75,
    "screenBrightness": 8
  },
  "timestamp": "2026-03-15T14:22:05.131Z"
}
```

---

### `POST /api/write` — Write a Value

Queues a value write to the Pyscreen data arrays. Requires `EnableWriteApi = true` in `UserData/MelonPreferences.cfg` (default: `true`).

> ⚠️ **Important:** This endpoint modifies **dashboard/display register values**. It is designed for safety-system simulation (triggering brake indicators, resetting vigilance timers). It does **not** necessarily alter the underlying physics engine state.

The HTTP handler validates the request and enqueues the write. The native array write itself runs on the Unity main thread during the next `Pyscreen.Update()` telemetry tick.

**Request**
```sh
POST http://localhost:5555/api/write
Content-Type: application/json
```

**Request Body** — [`ControlCommand`](#controlcommand)
```json
{
  "target": "<target-type>",
  "field": "<field-name>",
  "value": <number-or-boolean>
}
```

See [Section 5 — Write API](#5-write-api) for the full field table.

**Response** `200 OK` — success
```json
{
  "success": true,
  "data": "Write queued: generalBool.shp = True (applies on next main-thread tick)",
  "timestamp": "2026-03-15T14:22:05.131Z"
}
```

**Response** `200 OK` — no active train snapshot
```json
{
  "success": false,
  "error": "No active data source — board a train first.",
  "timestamp": "2026-03-15T14:22:05.131Z"
}
```

**Response** `200 OK` — Write API disabled
```json
{
  "success": false,
  "error": "Write API is disabled. Enable in config.",
  "timestamp": "2026-03-15T14:22:05.131Z"
}
```

**Response** `200 OK` — invalid field
```json
{
  "success": false,
  "error": "Unknown field: generalFloat.badfield. Use GET / to see available field names.",
  "timestamp": "2026-03-15T14:22:05.131Z"
}
```

**Response** `200 OK` — wrong HTTP method
```json
{
  "success": false,
  "error": "Use POST method",
  "timestamp": "2026-03-15T14:22:05.131Z"
}
```

---

### `GET /api/invalidate` — Invalidate Cache

Schedules `GameBridge` to release its cached references to game objects on the next Unity main-thread telemetry tick and re-scan afterward. Use this after switching between trains or if telemetry data appears stale.

**Request**
```sh
GET http://localhost:5555/api/invalidate
```

**Response** `200 OK`
```json
{
  "success": true,
  "data": "Cache invalidation scheduled for the next main-thread tick.",
  "timestamp": "2026-03-15T14:22:05.131Z"
}
```

---

### `GET /api/debug` — Native Cache Diagnostics

Requests a diagnostic snapshot from the Unity main thread. The endpoint reports whether each cached native sub-object is present, whether its backing data array was found, detected array lengths, raw sample values, and the native array offsets currently in use.

Use this endpoint when telemetry appears stale, a train-specific source array looks empty, or build/game updates may have changed native layout assumptions.

**Request**
```sh
GET http://localhost:5555/api/debug?pretty=true
```

**Response** `200 OK` — success
```json
{
  "success": true,
  "data": {
    "dataSourceFound": true,
    "dataFieldOffset": 24,
    "arrayDataOffset": 32,
    "arrayMaxLenOfs": 24,
    "generalFloat": {
      "objectFound": true,
      "dataFound": true,
      "maxLength": 21,
      "sample": [0.82, 0.75, 87.4, 1840.0]
    },
    "generalInt": {
      "objectFound": true,
      "dataFound": true,
      "maxLength": 43,
      "sample": [0, 0, 0, 3]
    },
    "generalBool": {
      "objectFound": true,
      "dataFound": true,
      "maxLength": 14,
      "sample": [false, false, true, true]
    },
    "eimppn": {
      "objectFound": true,
      "dataFound": true,
      "maxLength": 4,
      "sample": [0.0, 5.0, 8.5, 5.0]
    },
    "brakes": {
      "objectFound": true,
      "dataFound": true,
      "maxLength": 2,
      "sample": [false, false]
    },
    "emu": {
      "objectFound": true,
      "dataFound": true,
      "maxLength": 8,
      "sample": [false, false, false, false]
    },
    "nextStationPanel": true
  },
  "timestamp": "2026-03-15T14:22:05.131Z"
}
```

**Response** `200 OK` — no active train
```json
{
  "success": false,
  "error": "No active telemetry yet (IsActive=false). Board a train, wait one tick, then try again.",
  "timestamp": "2026-03-15T14:22:05.131Z"
}
```

**Response** `200 OK` — timed out waiting for telemetry tick
```json
{
  "success": false,
  "error": "Debug snapshot timed out — is Pyscreen.Update() still ticking? Try boarding a train and retrying.",
  "timestamp": "2026-03-15T14:22:05.131Z"
}
```

---

## 4. Data Models Reference

### `ApiResponse<T>`

All read endpoints return this envelope. On success, `data` contains the payload and `error` is omitted. On failure, `data` is omitted and `error` contains a description.

| Field | Type | Description |
|---|---|---|
| `success` | `bool` | `true` if the request succeeded |
| `data` | `T` \| `null` | The response payload (omitted on failure) |
| `error` | `string` \| `null` | Error message (omitted on success) |
| `timestamp` | `string` | ISO 8601 UTC datetime of the server response |

---

### `TelemetrySnapshot`

Root object returned by `GET /api/telemetry`. All sub-object fields are `null` when `isActive` is `false`.

| Field | Type | Description |
|---|---|---|
| `timestamp` | `string` | ISO 8601 UTC — time the snapshot was captured |
| `isActive` | `bool` | `false` if no active train is found in the scene |
| `train` | `TrainInfo` \| `null` | Movement data |
| `brakes` | `BrakeInfo` \| `null` | Brake system pressures and states |
| `electrical` | `ElectricalInfo` \| `null` | Traction and electrical data |
| `safety` | `SafetyInfo` \| `null` | Safety system states |
| `doors` | `DoorInfo` \| `null` | Door and doorstep states |
| `controls` | `ControlInfo` \| `null` | Driver control positions |
| `station` | `StationInfo` \| `null` | Next station information |
| `environment` | `EnvironmentInfo` \| `null` | Time, radio, lighting |

---

### `TrainInfo`

| Field | Type | Unit | Description |
|---|---|---|---|
| `velocity` | `double` | km/h | Current speed (floating-point) |
| `velocityInt` | `int` | km/h | Current speed rounded to nearest integer |
| `newSpeed` | `double` | km/h | Target / permitted speed |
| `distanceDriven` | `double` | km | Cumulative distance driven this session |
| `distanceCounter` | `double` | km | Resettable distance counter |
| `trainLength` | `double` | m | Total consist length in metres |
| `direction` | `int` | — | `-1` = Reverse, `0` = Neutral, `1` = Forward |
| `cabinDirection` | `int` | — | Direction relative to cab orientation |
| `cab` | `int` | — | Active cab number |
| `unitNo` | `int` | — | Unit/locomotive number |
| `carNo` | `int` | — | Car/wagon number |

---

### `BrakeInfo`

| Field | Type | Unit | Description |
|---|---|---|---|
| `bc` | `double` | bar | Brake Cylinder pressure |
| `bp` | `double` | bar | Brake Pipe pressure |
| `sp` | `double` | bar | Supply / Main Reservoir pressure |
| `cp` | `double` | bar | Control Pressure |
| `pneumaticBrakeStatus` | `double` | — | Pneumatic brake engagement status value |
| `springActive` | `bool` | — | `true` if spring (parking) brake is applied |
| `springShutoff` | `bool` | — | `true` if spring brake shutoff valve is closed |
| `edBrakeActive` | `int` | — | Electrodynamic (regenerative) brake: `0` = off, `1` = on |
| `brakeDelayFlag` | `int` | — | Brake delay mode flag |

---

### `ElectricalInfo`

| Field | Type | Unit | Description |
|---|---|---|---|
| `voltage` | `double` | V | Overhead catenary line voltage |
| `tractionForce` | `double` | kN | Current traction force |
| `tractionPercent` | `double` | % | Traction effort as percentage of maximum |
| `motorFrequency` | `double` | Hz | Traction motor supply frequency |
| `powerConsumption` | `double` | kW | Instantaneous power draw |
| `currentVoltageRatio` | `double` | — | Current-to-voltage ratio (unitless) |
| `pantographPressure` | `double` | bar | Pantograph contact pressure |
| `converter` | `bool` | — | `true` if the static converter is active |
| `acStatus` | `bool` | — | `true` if AC power supply is healthy |
| `battery` | `bool` | — | `true` if battery is switched on |
| `combustionEngineActive` | `int` | — | Diesel engine running: `0` = off, `1` = on |
| `combustionEngineRPM` | `double` | RPM | Diesel engine rotational speed |
| `coolantTemperature` | `double` | °C | Engine coolant temperature |
| `dieselMode` | `bool` | — | `true` if the unit is operating in diesel/thermal mode |

---

### `SafetyInfo`

| Field | Type | Description |
|---|---|---|
| `shp` | `bool` | `true` if SHP (*Samoczynne Hamowanie Pociągu* — automatic train protection) system is demanding acknowledgement |
| `ca` | `bool` | `true` if CA (*Czuwak Aktywny* — active vigilance device) alarm is active |
| `alarmActive` | `bool` | General alarm active |
| `fireDetectionActive` | `bool` | `true` if the fire detection system has triggered |
| `absStatus` | `int` | Anti-lock Brake System status code |
| `sagStatus` | `int` | SAG (speed-dependent brake control) status code |

---

### `DoorInfo`

| Field | Type | Description |
|---|---|---|
| `doors` | `bool` | `true` if any door is open (aggregate status) |
| `doorsLeft` | `bool` | Left-side doors open |
| `doorsRight` | `bool` | Right-side doors open |
| `doorstepLeft` | `bool` | Left doorstep deployed |
| `doorstepRight` | `bool` | Right doorstep deployed |
| `slip` | `bool` | `true` if wheel slip is currently detected |
| `brakesEngaged` | `bool` | EMU parking brakes engaged (door interlock) |

---

### `ControlInfo`

| Field | Type | Unit | Description |
|---|---|---|---|
| `mainCtrlPos` | `int` | notch | Main controller (throttle) commanded notch position |
| `mainCtrlActualPos` | `int` | notch | Main controller actual/current notch position |
| `speedCtrl` | `double` | km/h | Speed controller setpoint |
| `speedCtrlPower` | `double` | — | Speed controller output power (0.0–1.0) |
| `speedCtrlActive` | `bool` | — | `true` if speed controller is actively regulating |
| `speedCtrlStandby` | `bool` | — | `true` if speed controller is in standby mode |
| `speedCtrlStatus` | `int` | — | Speed controller status code |
| `sanding` | `bool` | — | `true` if sanding is active |
| `soloDriveActive` | `int` | — | Solo-drive (single-unit) mode active flag |
| `lightsFront` | `int` | — | Front lights setting (e.g. `0` = off, `1` = dim, `2` = full) |
| `lightsRear` | `int` | — | Rear lights setting |
| `lightsCompartments` | `bool` | — | `true` if passenger compartment lights are on |

---

### `StationInfo`

| Field | Type | Description |
|---|---|---|
| `nextStation` | `string` \| `null` | Name of the next station in the active timetable |
| `distance` | `string` \| `null` | Distance to the next station as a display string (e.g. `"12.3 km"`) |
| `stationCount` | `int` | Total number of stations in the current timetable roster |

---

### `EnvironmentInfo`

| Field | Type | Range | Description |
|---|---|---|---|
| `hours` | `int` | 0–23 | In-game clock: hours |
| `minutes` | `int` | 0–59 | In-game clock: minutes |
| `seconds` | `int` | 0–59 | In-game clock: seconds |
| `day` | `int` | 1–31 | In-game calendar: day |
| `month` | `int` | 1–12 | In-game calendar: month |
| `year` | `int` | — | In-game calendar: year |
| `dayOfWeek` | `int` | 0–6 | Day of week (`0` = Sunday per .NET `DayOfWeek`) |
| `lightLevel` | `double` | 0.0–1.0 | Cabin ambient light level |
| `radioActive` | `bool` | — | `true` if cab radio is switched on |
| `radioChannel` | `int` | — | Active radio channel number |
| `radioVolume` | `double` | 0.0–1.0 | Radio volume level |
| `screenBrightness` | `int` | — | Cab screen brightness setting |

---

### `ControlCommand`

Request body for `POST /api/write`.

| Field | Type | Description |
|---|---|---|
| `target` | `string` | Subsystem selector — see [Write API](#5-write-api) for valid values |
| `field` | `string` | Field name within the selected subsystem |
| `value` | `number` \| `boolean` | The value to write; must match the type implied by `target` |

---

## 5. Write API

### Enabling the Write API

The Write API is enabled by default. To disable it, set `EnableWriteApi = false` in the config file (see [Section 7](#7-configuration)). When disabled, all `POST /api/write` requests return a `success: false` error.

### Target types

| `target` value | Accepted `value` type | Description |
|---|---|---|
| `generalFloat` or `float` | `number` (floating-point) | Write to the float Pyscreen array |
| `generalInt` or `int` | `number` (integer) | Write to the int Pyscreen array |
| `generalBool` or `bool` | `true` \| `false` | Write to the bool Pyscreen array |

Both the full name (`generalFloat`) and the short alias (`float`) are accepted interchangeably.

### Float fields (`generalFloat` / `float`)

| `field` value | Unit | Description | Pyscreen Index |
|---|---|---|---|
| `velocity` | km/h | Train speed | 2 |
| `new_speed` | km/h | Target / permitted speed | 8 |
| `voltage` | V | Overhead line voltage | 7 |
| `speedctrl` | km/h | Speed controller setpoint | 5 |
| `speedctrlpower` | — | Speed controller power output | 6 |
| `pantpress` | bar | Pantograph pressure | 9 |
| `distance_counter` | km | Distance counter | 10 |
| `train_length` | m | Train length | 11 |
| `distance_driven` | km | Distance driven | 12 |
| `pneumatic_brake_status` | — | Pneumatic brake status | 13 |
| `tractionforce` | kN | Traction force | 16 |
| `tractionpercent` | % | Traction percentage | 20 |
| `motor_freq` | Hz | Motor frequency | 19 |
| `power` | kW | Power consumption | 3 |
| `rpm` | RPM | Combustion engine RPM | 17 |
| `coolant` | °C | Coolant temperature | 18 |
| `light_level` | 0.0–1.0 | Cabin light level | 0 |
| `radio_volume` | 0.0–1.0 | Radio volume | 1 |

### Integer fields (`generalInt` / `int`)

| `field` value | Alias | Description | Pyscreen Index |
|---|---|---|---|
| `direction` | — | Direction (`-1` / `0` / `1`) | 10 |
| `mainctrl_pos` | `throttle` | Throttle notch position | 11 |
| `mainctrl_actual` | — | Actual controller position | 12 |
| `radio_channel` | — | Radio channel number | 3 |
| `lights_front` | — | Front light setting | 8 |
| `lights_rear` | — | Rear light setting | 9 |
| `cab` | — | Active cab number | 7 |
| `brightness` | — | Screen brightness | 42 |
| `brake_delay` | — | Brake delay flag | 35 |

### Boolean fields (`generalBool` / `bool`)

| `field` value | Description | Pyscreen Index |
|---|---|---|
| `shp` | SHP safety system state | 0 |
| `ca` | CA vigilance device state | 1 |
| `battery` | Battery on/off | 2 |
| `converter` | Static converter on/off | 3 |
| `sanding` | Sanding on/off | 4 |
| `speedctrl` | Speed controller active | 5 |
| `radio` | Radio on/off | 7 |
| `lights` | Compartment lights on/off | 9 |
| `alarm` | Alarm active | 11 |
| `diesel` | Diesel mode on/off | 13 |

### Write API examples

```sh
# Trigger SHP emergency brake indicator
curl -X POST http://localhost:5555/api/write \
  -H "Content-Type: application/json" \
  -d "{\"target\":\"generalBool\",\"field\":\"shp\",\"value\":true}"

# Clear SHP alarm
curl -X POST http://localhost:5555/api/write \
  -H "Content-Type: application/json" \
  -d "{\"target\":\"generalBool\",\"field\":\"shp\",\"value\":false}"

# Activate sanding
curl -X POST http://localhost:5555/api/write \
  -H "Content-Type: application/json" \
  -d "{\"target\":\"bool\",\"field\":\"sanding\",\"value\":true}"

# Set throttle to notch 5
curl -X POST http://localhost:5555/api/write \
  -H "Content-Type: application/json" \
  -d "{\"target\":\"generalInt\",\"field\":\"throttle\",\"value\":5}"

# Set speed controller setpoint to 80 km/h
curl -X POST http://localhost:5555/api/write \
  -H "Content-Type: application/json" \
  -d "{\"target\":\"generalFloat\",\"field\":\"speedctrl\",\"value\":80.0}"

# Set radio channel to 5
curl -X POST http://localhost:5555/api/write \
  -H "Content-Type: application/json" \
  -d "{\"target\":\"int\",\"field\":\"radio_channel\",\"value\":5}"
```

```python
import requests

BASE = "http://localhost:5555"

def write(target: str, field: str, value):
    """Send a write command to the SimRailConnect API."""
    resp = requests.post(f"{BASE}/api/write", json={
        "target": target,
        "field": field,
        "value": value,
    })
    result = resp.json()
    if not result["success"]:
        raise RuntimeError(f"Write failed: {result.get('error')}")
    return result["data"]

# Trigger SHP alarm
write("generalBool", "shp", True)

# Set throttle notch
write("generalInt", "throttle", 3)

# Set speed controller to 100 km/h and activate it
write("generalFloat", "speedctrl", 100.0)
write("generalBool",  "speedctrl", True)

# Activate sanding
write("bool", "sanding", True)
```

---

## 6. Error Handling

### HTTP status codes

| Code | Meaning |
|---|---|
| `200 OK` | Request was processed. Check `success` in the response body to determine outcome. |
| `404 Not Found` | The requested endpoint path does not exist. |
| `500 Internal Server Error` | An unhandled exception occurred on the server side. |

> **Note:** The API returns HTTP `200` even for logical errors (e.g. unknown field, Write API disabled). Always check the `success` field in the response body.

### Error response shape

```json
{
  "success": false,
  "error": "Human-readable error description",
  "timestamp": "2026-03-15T14:22:05.131Z"
}
```

### Common error messages

| Error Message | Cause | Resolution |
|---|---|---|
| `No telemetry data available. Is a train active?` | The plugin cannot find an active train in the scene | Board a train and enter the cab |
| `No train data available` | Sub-snapshot is null (train not active) | Same as above |
| `Write API is disabled. Enable in config.` | `EnableWriteApi = false` in config | Set `EnableWriteApi = true` and restart |
| `No active data source — board a train first.` | Write requested before an active telemetry snapshot exists | Board a train, wait for telemetry, and retry |
| `Unknown field: <target>.<field>` | Invalid `target` or `field` combination | Consult the [Write API field tables](#5-write-api) |
| `Value must be a number for float fields` | Non-numeric `value` sent for a float field | Send a JSON number, e.g. `3.14` |
| `Value must be an integer for int fields` | Non-integer or non-numeric value | Send a JSON integer, e.g. `3` |
| `Value must be true/false for bool fields` | Non-boolean value | Send JSON `true` or `false` |
| `JSON parse error: ...` | Malformed request body | Fix the JSON syntax in the request body |
| `Use POST method` | `GET /api/write` was requested | Use `POST` method |
| `Debug snapshot timed out — is Pyscreen.Update() still ticking? Try boarding a train and retrying.` | `/api/debug` was requested, but the main-thread telemetry tick did not service it within 600 ms | Board a train, wait for the cab screen to update, and retry |
| `Unknown endpoint: /api/xyz` | Path not recognised | Check [Endpoint Reference](#3-endpoint-reference) |

### Handling errors in Python

```python
import requests

def safe_get(endpoint: str) -> dict | None:
    """Fetch a telemetry endpoint, returning data dict or None on error."""
    try:
        resp = requests.get(f"http://localhost:5555{endpoint}", timeout=2.0)
        resp.raise_for_status()
        body = resp.json()
        if not body.get("success"):
            print(f"[WARN] API error: {body.get('error')}")
            return None
        return body.get("data")
    except requests.exceptions.ConnectionError:
        print("[ERROR] Cannot connect to SimRailConnect. Is the game running?")
        return None
    except requests.exceptions.Timeout:
        print("[ERROR] Request timed out.")
        return None
```

---

## 7. Configuration

The mod stores its settings in the MelonLoader preferences file, updated automatically on first run.

**Path:** `SimRail\UserData\MelonPreferences.cfg`

### Available options

| Key | Default | Description |
|---|---|---|
| `Port` | `5555` | TCP port the HTTP server listens on |
| `UpdateIntervalMs` | `100` | Telemetry sampling interval in milliseconds |
| `EnableWriteApi` | `true` | Whether to allow `POST /api/write` requests |

### Example config file

```ini
[SimRailConnect]
Port = 5555
UpdateIntervalMs = 100
EnableWriteApi = true
```

### Changing the port

If port `5555` is in use, change the `Port` value and update your client code accordingly:

```python
BASE_URL = "http://localhost:9000"   # if Port = 9000 in config
```

### Network binding

On first start, the server attempts to bind to `http://+:<port>/` (all network interfaces). This requires administrative privileges on Windows. If that fails, it automatically falls back to `http://localhost:<port>/` (loopback only).

To expose the API on your local network (e.g. for a remote hardware display), run SimRail as administrator or add a `netsh` URL reservation:

```sh
netsh http add urlacl url=http://+:5555/ user=DOMAIN\username
```

> ⚠️ **Security Warning:** Never expose the API to an untrusted network, especially with `EnableWriteApi = true`.

---

## 8. Integration Example — Live Monitor

The following Python script connects to SimRailConnect and prints a live dashboard of speed, brake pressures, and safety system state to the terminal. It polls every second and handles all common error cases gracefully.

```python
#!/usr/bin/env python3
"""
SimRailConnect Live Monitor
===========================
Continuously polls the SimRailConnect API and displays a live
terminal dashboard showing speed, brake pressures, throttle
position, and safety-system states.

Requirements:
    pip install requests

Usage:
    python monitor.py
    python monitor.py --port 9000   # if using a custom port
    python monitor.py --interval 0.5  # poll twice per second
"""

import argparse
import sys
import time
from datetime import datetime

try:
    import requests
except ImportError:
    sys.exit("Missing dependency: run  pip install requests")


# ─── Configuration ────────────────────────────────────────────────────────────

DEFAULT_HOST = "http://localhost"
DEFAULT_PORT = 5555
DEFAULT_INTERVAL = 1.0   # seconds between polls
REQUEST_TIMEOUT = 2.0    # seconds before giving up on a single request


# ─── API helpers ──────────────────────────────────────────────────────────────

def build_url(base: str, path: str) -> str:
    return base.rstrip("/") + path


def fetch(base_url: str, path: str) -> dict | None:
    """
    GET a SimRailConnect endpoint.
    Returns the parsed 'data' dict on success, or None on any error.
    """
    url = build_url(base_url, path)
    try:
        resp = requests.get(url, timeout=REQUEST_TIMEOUT)
        resp.raise_for_status()
        body = resp.json()
        if body.get("success"):
            return body.get("data")
        # API returned success=false
        return None
    except requests.exceptions.ConnectionError:
        return None
    except requests.exceptions.Timeout:
        return None
    except Exception:
        return None


# ─── Display helpers ──────────────────────────────────────────────────────────

def bar(value: float, max_val: float, width: int = 20, fill: str = "█") -> str:
    """Render a simple ASCII progress bar."""
    ratio = max(0.0, min(1.0, value / max_val)) if max_val else 0.0
    filled = int(ratio * width)
    return fill * filled + "░" * (width - filled)


def fmt_direction(d: int) -> str:
    return {-1: "◄ REVERSE", 0: "● NEUTRAL", 1: "► FORWARD"}.get(d, f"? ({d})")


def fmt_bool(val: bool, true_label: str = "ON ", false_label: str = "---") -> str:
    return f"\033[92m{true_label}\033[0m" if val else f"\033[90m{false_label}\033[0m"


def fmt_alarm(val: bool, label: str) -> str:
    return f"\033[91m⚠ {label}\033[0m" if val else f"\033[90m  {label}\033[0m"


def clear_screen():
    # ANSI escape: move cursor to top-left and clear screen
    print("\033[H\033[J", end="")


# ─── Main render loop ─────────────────────────────────────────────────────────

def render(base_url: str):
    """Fetch all relevant endpoints and render a single dashboard frame."""

    # Fetch in parallel would be faster, but sequential is simpler and readable
    train     = fetch(base_url, "/api/train")
    brakes    = fetch(base_url, "/api/brakes")
    electrical = fetch(base_url, "/api/electrical")
    safety    = fetch(base_url, "/api/safety")
    controls  = fetch(base_url, "/api/controls")
    station   = fetch(base_url, "/api/station")
    env_data  = fetch(base_url, "/api/environment")

    clear_screen()
    now = datetime.now().strftime("%H:%M:%S")
    print(f"╔══════════════════════════════════════════════════════╗")
    print(f"║       SimRailConnect Live Monitor  [{now}]      ║")
    print(f"╚══════════════════════════════════════════════════════╝")

    # ── Connection status ────────────────────────────────────────────────────
    if train is None:
        print("\n  \033[93m⚡ Waiting for active train...\033[0m")
        print("  Make sure SimRail is running and you are in a cab.")
        return

    # ── Train movement ───────────────────────────────────────────────────────
    speed     = train.get("velocity", 0.0)
    speed_int = train.get("velocityInt", 0)
    new_speed = train.get("newSpeed", 0.0)
    direction = train.get("direction", 0)
    distance  = train.get("distanceDriven", 0.0)
    cab       = train.get("cab", 0)

    print(f"\n  ┌─ MOVEMENT ────────────────────────────────────────┐")
    print(f"  │  Speed:      {speed:6.1f} km/h  {bar(speed, 200)}  │")
    print(f"  │  Permitted:  {new_speed:6.1f} km/h  {fmt_direction(direction):>10}  Cab: {cab}  │")
    print(f"  │  Distance:   {distance:8.3f} km                                │")
    print(f"  └───────────────────────────────────────────────────┘")

    # ── Brake pressures ──────────────────────────────────────────────────────
    if brakes:
        bc = brakes.get("bc", 0.0)
        bp = brakes.get("bp", 0.0)
        sp = brakes.get("sp", 0.0)
        cp = brakes.get("cp", 0.0)
        spring = brakes.get("springActive", False)
        ed     = brakes.get("edBrakeActive", 0)

        bc_color = "\033[91m" if bc > 0.5 else "\033[92m"
        bp_color = "\033[91m" if bp < 4.0 else "\033[92m"

        print(f"\n  ┌─ BRAKES (bar) ────────────────────────────────────┐")
        print(f"  │  BC (Cyl):  {bc_color}{bc:5.2f}\033[0m  {bar(bc, 5.0)}             │")
        print(f"  │  BP (Pipe): {bp_color}{bp:5.2f}\033[0m  {bar(bp, 6.0)}             │")
        print(f"  │  SP (Main): {sp:5.2f}  CP: {cp:5.2f}                          │")
        print(f"  │  Spring: {fmt_bool(spring, 'ACTIVE', 'off   ')}   ED Brake: {fmt_bool(bool(ed), 'ON', '--')}        │")
        print(f"  └───────────────────────────────────────────────────┘")
    else:
        print("\n  [Brake data unavailable]")

    # ── Electrical ───────────────────────────────────────────────────────────
    if electrical:
        voltage  = electrical.get("voltage", 0.0)
        power    = electrical.get("powerConsumption", 0.0)
        tforce   = electrical.get("tractionForce", 0.0)
        tpct     = electrical.get("tractionPercent", 0.0)
        diesel   = electrical.get("dieselMode", False)
        rpm      = electrical.get("combustionEngineRPM", 0.0)
        coolant  = electrical.get("coolantTemperature", 0.0)

        print(f"\n  ┌─ ELECTRICAL / TRACTION ───────────────────────────┐")
        if diesel:
            print(f"  │  Mode: \033[93mDIESEL\033[0m   RPM: {rpm:6.0f}   Coolant: {coolant:5.1f} °C     │")
        else:
            print(f"  │  Mode: \033[96mELECTRIC\033[0m  Voltage: {voltage:6.0f} V                   │")
        print(f"  │  Traction: {tforce:6.1f} kN ({tpct:5.1f}%)  Power: {power:7.0f} kW  │")
        print(f"  └───────────────────────────────────────────────────┘")

    # ── Controls ─────────────────────────────────────────────────────────────
    if controls:
        notch   = controls.get("mainCtrlPos", 0)
        sc_kmh  = controls.get("speedCtrl", 0.0)
        sc_on   = controls.get("speedCtrlActive", False)
        sanding = controls.get("sanding", False)
        lf      = controls.get("lightsFront", 0)
        lr      = controls.get("lightsRear", 0)

        print(f"\n  ┌─ CONTROLS ────────────────────────────────────────┐")
        print(f"  │  Throttle notch: {notch:3d}   Sanding: {fmt_bool(sanding)}             │")
        print(f"  │  Speed ctrl: {fmt_bool(sc_on, 'ACTIVE', 'off   ')} @ {sc_kmh:5.1f} km/h               │")
        print(f"  │  Lights — Front: {lf}  Rear: {lr}                          │")
        print(f"  └───────────────────────────────────────────────────┘")

    # ── Safety systems ───────────────────────────────────────────────────────
    if safety:
        shp   = safety.get("shp", False)
        ca    = safety.get("ca", False)
        alarm = safety.get("alarmActive", False)
        fire  = safety.get("fireDetectionActive", False)

        print(f"\n  ┌─ SAFETY ──────────────────────────────────────────┐")
        print(f"  │  {fmt_alarm(shp, 'SHP')}   {fmt_alarm(ca, 'CA')}   "
              f"{fmt_alarm(alarm, 'ALARM')}   {fmt_alarm(fire, 'FIRE')}     │")
        print(f"  └───────────────────────────────────────────────────┘")

    # ── Station / time ───────────────────────────────────────────────────────
    if station:
        next_stn = station.get("nextStation") or "—"
        dist     = station.get("distance") or "—"
        print(f"\n  ┌─ NEXT STATION ────────────────────────────────────┐")
        print(f"  │  {next_stn:<35}  {dist:>10}  │")
        print(f"  └───────────────────────────────────────────────────┘")

    if env_data:
        h, m, s = env_data.get("hours", 0), env_data.get("minutes", 0), env_data.get("seconds", 0)
        radio   = env_data.get("radioActive", False)
        ch      = env_data.get("radioChannel", 0)
        print(f"\n  Game time: {h:02d}:{m:02d}:{s:02d}   "
              f"Radio: {fmt_bool(radio, f'CH {ch:02d}', 'off')}")

    print(f"\n  Press Ctrl+C to quit.")


# ─── Entry point ─────────────────────────────────────────────────────────────

def main():
    parser = argparse.ArgumentParser(description="SimRailConnect Live Monitor")
    parser.add_argument("--port",     type=int,   default=DEFAULT_PORT,
                        help=f"API port (default: {DEFAULT_PORT})")
    parser.add_argument("--interval", type=float, default=DEFAULT_INTERVAL,
                        help=f"Poll interval in seconds (default: {DEFAULT_INTERVAL})")
    args = parser.parse_args()

    base_url = f"{DEFAULT_HOST}:{args.port}"

    print(f"Connecting to SimRailConnect at {base_url} ...")
    print("Press Ctrl+C to quit.\n")
    time.sleep(1)

    try:
        while True:
            render(base_url)
            time.sleep(args.interval)
    except KeyboardInterrupt:
        print("\nMonitor stopped.")


if __name__ == "__main__":
    main()
```

**Sample output:**
```
╔══════════════════════════════════════════════════════╗
║       SimRailConnect Live Monitor  [14:22:07]      ║
╚══════════════════════════════════════════════════════╝

  ┌─ MOVEMENT ────────────────────────────────────────┐
  │  Speed:        87.4 km/h  █████████░░░░░░░░░░░░  │
  │  Permitted:   100.0 km/h   ► FORWARD  Cab: 1      │
  │  Distance:    12.345 km                            │
  └───────────────────────────────────────────────────┘

  ┌─ BRAKES (bar) ────────────────────────────────────┐
  │  BC (Cyl):   0.00  ░░░░░░░░░░░░░░░░░░░░           │
  │  BP (Pipe):  5.00  █████████████████░░░           │
  │  SP (Main):  8.50  CP:  5.00                       │
  │  Spring: off     ED Brake: --                      │
  └───────────────────────────────────────────────────┘
```

---

*SimRailConnect API Documentation — Copyright © 2026 rinnyanneko — GPLv3*
*Not an official SimRail product. For simulation research and safety system fidelity.*
