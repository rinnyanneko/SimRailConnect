# SimRailConnect API Documentation

SimRailConnect is WebSocket-only. The HTTP REST API has been removed.

Default URL: `ws://localhost:5556/ws`

The current build is a managed-only MelonLoader plugin installed in `<SimRail>\Plugins\`. It intentionally excludes Harmony, Unity, IL2CPP, `Assembly-CSharp`, `GameBridge`, `TelemetryMonitor`, and `ApiCommandRegistry` from the compiled assembly.

Native telemetry is not included in this build. Native-dependent messages return:

```json
{
  "type": "error",
  "id": "cmd-001",
  "code": "NATIVE_TELEMETRY_DISABLED",
  "message": "Native telemetry is not included in this managed-only plugin build"
}
```

Use [WEBSOCKET_API.md](WEBSOCKET_API.md) for the complete WebSocket envelope and examples.
