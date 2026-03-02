# SignalRGB — Creative Sound Blaster Katana V2X

Enables full RGB sync on the **SB Katana V2X** (VID `0x041E`, PID `0x3283`) through SignalRGB.

## Install

### 1. Add to SignalRGB

Click the button below to install the plugin directly from SignalRGB:

[**➕ Add to SignalRGB**](signalrgb://addon/install?url=https://github.com/capkz/V2X-SignalRGB)

Or paste this URL into your browser:
```
signalrgb://addon/install?url=https://github.com/capkz/V2X-SignalRGB
```

### 2. Install the bridge service (once)

The plugin needs a small background service to talk to the device.
`V2XBridge.exe` is included in the addon — no .NET install needed.

**Option A — from the plugin panel:** open the *Katana V2X* device panel in SignalRGB, click **Open addon folder**, then right-click `V2XBridge.exe` → *Run as administrator*.

**Option B — from PowerShell (admin):**
```powershell
& "$env:LOCALAPPDATA\WhirlwindFX\SignalRgb\cache\addons\V2X-SignalRGB\V2XBridge.exe" --install
```

The service starts immediately and auto-starts on every boot.

To uninstall the service:
```powershell
& "$env:LOCALAPPDATA\WhirlwindFX\SignalRgb\cache\addons\V2X-SignalRGB\V2XBridge.exe" --uninstall
```

### 3. Restart SignalRGB

The *Katana V2X* device will appear in the Devices tab.

---

## Architecture

```
SignalRGB (JS plugin)
    ↕  UDP 127.0.0.1:12346 / 12347
V2XBridge.exe  (Windows service, self-contained)
    ↕  Serial CDC ACM (COMx, 115200 baud)
Katana V2X
```

The bridge handles AES-256-GCM challenge-response auth and translates SignalRGB
color frames into the device's binary LED protocol.

## LED Layout

7 zones in a horizontal row across the soundbar front:

```
[1][2][3][4][5][6][7]
```

## Building from source

Requires [.NET 8 SDK](https://aka.ms/dotnet/download).

```powershell
# Produces V2XBridge.exe at the repo root (self-contained, no runtime needed)
.\publish.ps1
```

## Requirements

- Windows 10/11
- Creative drivers installed (CDC ACM COM port must appear in Device Manager)
- SignalRGB

## Protocol reference

- Auth reverse engineering: https://blog.nns.ee/2026/02/20/katana-v2x-re
- Reference Rust CLI: https://crates.io/crates/v2x
- Reference Creative plugin: https://github.com/hboyd2003/SignalRGB-Creative-Plugin
