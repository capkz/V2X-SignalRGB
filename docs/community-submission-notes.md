# SignalRGB Community Submission — Research Notes

## Where to submit

The de-facto community plugin repo is **SRGBmods/plugins** (no official SignalRGB org repo exists):
- https://github.com/SRGBmods/plugins

Closest architectural reference (bridge-style, same UDP protocol we use):
- https://github.com/hboyd2003/SignalRGB-Creative-Plugin

## Submission process

No formal process. Fork → add folder under `network/` → open PR.
No CI, no linting, no validator, no image size checker. Very lightweight review.

## Required file structure in the repo

```
network/
  Katana V2X/
    Katana V2X.js
    Katana V2X.qml
```

Folder name = JS base name = QML base name.

## Required JS exports

```js
// Mandatory
export function Name()        { return "Katana V2X"; }
export function Version()     { return "1.0.0"; }
export function Type()        { return "network"; }
export function Publisher()   { return "..."; }
export function Size()        { return [7, 1]; }
export function DefaultPosition() { return [0, 0]; }
export function DefaultScale()    { return 1.0; }

// Strongly recommended
export function SubdeviceController()   { return false; }
export function DefaultComponentBrand() { return "CompGen"; }
export function ImageUrl()              { return "..."; }  // or base64 in Initialize()

// User-visible service warning (important for bridge plugins)
export function DeviceMessage() {
    return [
        "This device requires the V2XBridge service.",
        "Install and start V2XBridge before using this plugin."
    ];
}
```

## ControllableParameters — minimum viable set

Other bridge plugins include at minimum:
- `LightingMode` combobox (`"Canvas"` / `"Forced"`)
- `forcedColor` color picker (used when mode is Forced)

```js
export function ControllableParameters() {
    return [
        {
            "property": "LightingMode",
            "group": "settings",
            "label": "Lighting Mode",
            "type": "combobox",
            "values": ["Canvas", "Forced"],
            "default": "Canvas"
        },
        {
            "property": "forcedColor",
            "group": "settings",
            "label": "Forced Color",
            "type": "color",
            "default": "#009bde"
        }
    ];
}
```

## JSDoc global comment convention

Add at top of JS to suppress linter warnings for injected globals:
```js
/* global
controller:readonly
device:readonly
service:readonly
udp:readonly
base64:readonly
LightingMode:readonly
forcedColor:readonly
*/
```

## Device image

- No image file required in the repo — embed base64 PNG in JS or use `ImageUrl()`
- No documented size spec; square PNG 64×64–256×256 is typical
- Option A (preferred): `device.setImageFromBase64(iconBase64)` in `Initialize()`
- Option B: `export function ImageUrl() { return "https://..."; }`

## Things our current plugin is missing for submission

- [ ] `DeviceMessage()` export — warn user about bridge service requirement
- [ ] `ControllableParameters()` — at minimum LightingMode + forcedColor
- [ ] `/* global ... */` comment block at top
- [ ] Brightness slider wired to `device.getBrightness()`
- [ ] Handle `LightingMode === "Forced"` in `Render()` (use `forcedColor` instead of canvas)
- [ ] Embedded base64 icon (or real hosted image URL)
