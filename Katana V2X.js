// SignalRGB Plugin — Creative Sound Blaster Katana V2X
// Communicates with the V2XBridge Windows service over UDP loopback.
// Bridge listens on 127.0.0.1:12346, plugin listens on :12347.

/* global
controller:readonly
device:readonly
service:readonly
udp:readonly
base64:readonly
LightingMode:readonly
forcedColor:readonly
brightness:readonly
*/

export function Name()            { return "Katana V2X"; }
export function Version()         { return "1.0.0"; }
export function Type()            { return "network"; }
export function Publisher()       { return "capkz"; }
export function Size()            { return [7, 1]; }
export function DefaultPosition() { return [75, 70]; }
export function DefaultScale()    { return 10.0; }
export function SubdeviceController()   { return false; }
export function DefaultComponentBrand() { return "CompGen"; }
export function ImageUrl() {
    return "https://m.media-amazon.com/images/I/41sssrRamoL._AC_UF894,1000_QL80_.jpg";
}

export function LedNames() {
    return [["LED 1", "LED 2", "LED 3", "LED 4", "LED 5", "LED 6", "LED 7"]];
}

export function LedPositions() {
    return [[0,0],[1,0],[2,0],[3,0],[4,0],[5,0],[6,0]];
}

export function DeviceMessage() {
    return [
        "This device requires the V2XBridge service to be running.",
        "See the project README for installation instructions.",
    ];
}

export function ControllableParameters() {
    return [
        {
            "property": "LightingMode",
            "group": "settings",
            "label": "Lighting Mode",
            "type": "combobox",
            "values": ["Canvas", "Forced"],
            "default": "Canvas",
            "tooltip": "Canvas syncs with SignalRGB. Forced applies a single static color.",
        },
        {
            "property": "forcedColor",
            "group": "settings",
            "label": "Forced Color",
            "type": "color",
            "default": "#009bde",
            "tooltip": "Color used when Lighting Mode is set to Forced.",
        },
        {
            "property": "brightness",
            "group": "settings",
            "label": "Brightness",
            "type": "number",
            "min": "0",
            "max": "100",
            "step": "5",
            "default": "100",
            "tooltip": "LED brightness (0–100).",
        },
    ];
}

// ---------------------------------------------------------------------------
// Discovery Service — runs on the SignalRGB service thread, not the device thread
// ---------------------------------------------------------------------------

export function DiscoveryService() {
    this.IconUrl = "https://m.media-amazon.com/images/I/41sssrRamoL._AC_UF894,1000_QL80_.jpg";

    this.Initialize = function() {
        service.log("Katana V2X: DiscoveryService initialized");
    };

    this.Update = function() {
        // Bridge is always at 127.0.0.1:12346 — add controller once if not already present.
        if (service.getController("katana-v2x-0001") === undefined) {
            service.log("Katana V2X: adding controller");
            const ctrl = {
                id:   "katana-v2x-0001",
                name: "Katana V2X",
                ip:   "127.0.0.1",
                port: 12346,
            };
            service.addController(ctrl);
            service.announceController(ctrl);
        }
    };
}

// ---------------------------------------------------------------------------
// Device lifecycle — runs on the device thread, one instance per controller
// ---------------------------------------------------------------------------

let _initialized = false;

export function Initialize() {
    device.addFeature("udp");
    device.addFeature("base64");
    _initialized = true;
    device.log("Katana V2X: Initialize");
}

export function Render() {
    if (!_initialized) return;

    const bytes = [];

    if (LightingMode === "Forced") {
        // Parse the hex color from ControllableParameters
        const hex = (forcedColor || "#009bde").replace("#", "");
        const r = parseInt(hex.substring(0, 2), 16) || 0;
        const g = parseInt(hex.substring(2, 4), 16) || 0;
        const b = parseInt(hex.substring(4, 6), 16) || 0;
        for (let i = 0; i < 7; i++) {
            bytes.push(r, g, b);
        }
    } else {
        // Canvas mode — sample colors from the SignalRGB canvas
        for (let x = 0; x < 7; x++) {
            const rgb = device.color(x, 0);
            bytes.push(rgb[0], rgb[1], rgb[2]);
        }
    }

    // Apply brightness scaling
    const bright = Math.max(0, Math.min(100, parseInt(brightness) || 100)) / 100;
    for (let i = 0; i < bytes.length; i++) {
        bytes[i] = Math.round(bytes[i] * bright);
    }

    const encoded = base64.Encode(bytes);
    const msg = `Creative Bridge Plugin\nSETRGB\n${controller.id}\n${encoded}`;
    udp.send(controller.ip, controller.port, msg);
}

export function Shutdown() {
    device.log("Katana V2X: Shutdown");
    _initialized = false;
}
