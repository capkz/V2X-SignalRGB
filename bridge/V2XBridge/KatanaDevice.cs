using System.IO.Ports;
using System.Management;
using System.Security.Cryptography;
using System.Text;

namespace V2XBridge;

/// <summary>
/// Manages the serial connection to the Katana V2X (VID 0x041E, PID 0x3283),
/// performs AES-256-GCM challenge-response auth, and sends LED color commands.
/// </summary>
public sealed class KatanaDevice : IDisposable
{
    // -------------------------------------------------------------------------
    // AES-256-GCM key material (extracted from CTCDC.dll by nns.ee research)
    // Key layout: [h0, h1, KEY_DATA (28 bytes), pid_lo, pid_hi]
    // -------------------------------------------------------------------------
    private static readonly byte[] KeyData = [
        0xD3, 0x1A, 0x21, 0x27, 0x9B, 0xE3, 0x46, 0xF0,
        0x99, 0x9D, 0x6E, 0xC4, 0xC3, 0xFE, 0xBE, 0x98,
        0x90, 0x18, 0x69, 0xC1, 0x18, 0xFB, 0xB1, 0x25,
        0x6E, 0x0C, 0xE0, 0x7B,
    ];

    private const int VendorId  = 0x041E;
    private const int ProductId = 0x3283;
    private const int BaudRate  = 115200;

    private SerialPort? _port;
    private readonly ILogger<KatanaDevice> _log;

    public bool IsConnected => _port?.IsOpen == true;

    public KatanaDevice(ILogger<KatanaDevice> log) => _log = log;

    // -------------------------------------------------------------------------
    // Connection
    // -------------------------------------------------------------------------

    /// <summary>Finds the COM port for the Katana V2X and opens+authenticates it.</summary>
    public async Task ConnectAsync(CancellationToken ct)
    {
        string portName = FindComPort()
            ?? throw new InvalidOperationException(
                $"Katana V2X (VID={VendorId:X4} PID={ProductId:X4}) not found. " +
                "Ensure the device is connected and drivers are installed.");

        _log.LogInformation("Opening {Port}", portName);

        _port = new SerialPort(portName, BaudRate)
        {
            ReadTimeout  = 3000,
            WriteTimeout = 3000,
        };
        _port.Open();

        await AuthenticateAsync(ct);
        await SwitchToCommandModeAsync(ct);

        _log.LogInformation("Katana V2X ready on {Port}", portName);
    }

    // -------------------------------------------------------------------------
    // Authentication (AES-256-GCM challenge-response)
    // -------------------------------------------------------------------------

    private async Task AuthenticateAsync(CancellationToken ct)
    {
        // Drain any buffered data
        _port!.DiscardInBuffer();

        // Send "whoareyou\r\n" to request a challenge (or check if already authed)
        WriteLine("whoareyou");
        await Task.Delay(50, ct);

        string response = ReadLine();

        if (response.StartsWith("Unknown command", StringComparison.Ordinal) ||
            response.StartsWith("unlock_OK",       StringComparison.Ordinal))
        {
            _log.LogInformation("Device already authenticated");
            return;
        }

        if (!response.StartsWith("whoareyou", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unexpected auth response: {response}");
        }

        // Parse challenge: "whoareyou" [h0 h1] [p0 p1] [32-byte nonce]
        byte[] challengeBytes = Encoding.Latin1.GetBytes(response.TrimEnd('\r', '\n'));
        if (challengeBytes.Length < 45)
            throw new InvalidOperationException("Challenge packet too short");

        byte h0    = challengeBytes[9];
        byte h1    = challengeBytes[10];
        byte pid0  = challengeBytes[11]; // PID lo
        byte pid1  = challengeBytes[12]; // PID hi
        byte[] nonce = challengeBytes[13..45]; // 32 bytes

        // Build 32-byte AES key
        byte[] key = BuildKey(h0, h1, pid0, pid1);

        // Generate 16-byte random IV; first 12 bytes used as GCM nonce
        byte[] iv = RandomNumberGenerator.GetBytes(16);
        byte[] gcmNonce = iv[..12];

        // Encrypt the device nonce with AES-256-GCM
        byte[] ciphertext = new byte[nonce.Length];
        byte[] tag        = new byte[16];
        using var aesGcm  = new AesGcm(key, 16);
        aesGcm.Encrypt(gcmNonce, nonce, ciphertext, tag);

        // Wire: "unlock" + iv(16) + ciphertext(32) + tag(16) + "\r\n"  = 72 bytes
        byte[] prefix   = "unlock"u8.ToArray();
        byte[] payload  = [.. prefix, .. iv, .. ciphertext, .. tag];
        string response2 = SendBinaryAndReadLine(payload);

        if (!response2.StartsWith("unlock_OK", StringComparison.Ordinal))
            throw new InvalidOperationException($"Auth failed: {response2}");

        _log.LogInformation("Auth OK");
    }

    private static byte[] BuildKey(byte h0, byte h1, byte pid0, byte pid1)
    {
        byte[] key = new byte[32];
        key[0]  = h0;
        key[1]  = h1;
        KeyData.CopyTo(key, 2);
        key[30] = pid0;
        key[31] = pid1;
        return key;
    }

    private async Task SwitchToCommandModeAsync(CancellationToken ct)
    {
        WriteLine("SW_MODE1");
        await Task.Delay(100, ct);
        _port!.DiscardInBuffer();

        // Confirm with binary ping
        byte[] ping = [0x5A, 0x03, 0x00];
        _port.Write(ping, 0, ping.Length);
        await Task.Delay(50, ct);
        _port.DiscardInBuffer();

        // One-time LED setup: power on, 7 color slots, static mode
        // These are sent once at connect time — not per frame.
        SendCommand([0x5A, 0x3A, 0x02, 0x25, 0x01]);       // lighting on
        SendCommand([0x5A, 0x3A, 0x03, 0x37, 0x00, 0x07]); // color count = 7
        SendCommand([0x5A, 0x3A, 0x03, 0x29, 0x00, 0x03]); // mode = 0x03 (static/solo)
    }

    // -------------------------------------------------------------------------
    // LED commands
    // -------------------------------------------------------------------------

    /// <summary>
    /// Sends per-LED colors to the device.
    /// colors: flat array of (R, G, B) bytes for each of 7 LEDs (21 bytes total).
    /// </summary>
    public void SetColors(ReadOnlySpan<byte> rgbColors)
    {
        if (_port is null || !_port.IsOpen) return;

        // Packet: 5A 3A 20 2B 00 01 01 [ABGR_0..ABGR_6]
        // Length byte 0x20 = 32: counts everything after it (4 sub-header + 7×4 color bytes).
        byte[] packet = new byte[35];
        packet[0] = 0x5A;
        packet[1] = 0x3A;
        packet[2] = 0x20; // payload length = 32 bytes
        packet[3] = 0x2B;
        packet[4] = 0x00;
        packet[5] = 0x01;
        packet[6] = 0x01;

        for (int i = 0; i < 7; i++)
        {
            int src  = i * 3; // R,G,B in source
            int dest = 7 + i * 4;
            byte r = (i * 3 + 0 < rgbColors.Length) ? rgbColors[src + 0] : (byte)0;
            byte g = (i * 3 + 1 < rgbColors.Length) ? rgbColors[src + 1] : (byte)0;
            byte b = (i * 3 + 2 < rgbColors.Length) ? rgbColors[src + 2] : (byte)0;
            packet[dest + 0] = 0xFF; // alpha
            packet[dest + 1] = b;
            packet[dest + 2] = g;
            packet[dest + 3] = r;
        }

        SendCommand(packet);
    }

    private void SendCommand(ReadOnlySpan<byte> data)
    {
        if (_port is null || !_port.IsOpen) return;
        _port.Write(data.ToArray(), 0, data.Length);
    }

    // -------------------------------------------------------------------------
    // Serial helpers
    // -------------------------------------------------------------------------

    private void WriteLine(string text)
    {
        byte[] data = Encoding.ASCII.GetBytes(text + "\r\n");
        _port!.Write(data, 0, data.Length);
    }

    private string ReadLine()
    {
        var sb = new StringBuilder();
        while (true)
        {
            int b = _port!.ReadByte();
            if (b == '\n') break;
            if (b == '\r') continue;
            sb.Append((char)b);
        }
        return sb.ToString();
    }

    private string SendBinaryAndReadLine(byte[] payload)
    {
        byte[] wire = [.. payload, (byte)'\r', (byte)'\n'];
        _port!.Write(wire, 0, wire.Length);
        return ReadLine();
    }

    // -------------------------------------------------------------------------
    // COM port discovery via WMI
    // -------------------------------------------------------------------------

    private static string? FindComPort()
    {
        // Query Win32_PnPEntity for USB serial devices matching our VID/PID
        string vidStr = $"VID_{VendorId:X4}";
        string pidStr = $"PID_{ProductId:X4}";

        using var searcher = new ManagementObjectSearcher(
            "SELECT Name, DeviceID FROM Win32_PnPEntity WHERE Name LIKE '%(COM%'");

        foreach (ManagementObject obj in searcher.Get())
        {
            string? deviceId = obj["DeviceID"]?.ToString();
            string? name     = obj["Name"]?.ToString();

            if (deviceId is null || name is null) continue;

            if (deviceId.Contains(vidStr, StringComparison.OrdinalIgnoreCase) &&
                deviceId.Contains(pidStr, StringComparison.OrdinalIgnoreCase))
            {
                // Extract "COMxx" from name like "USB Serial Device (COM3)"
                int start = name.IndexOf("(COM", StringComparison.Ordinal);
                if (start < 0) continue;
                int end = name.IndexOf(')', start);
                if (end < 0) continue;
                return name[(start + 1)..end];
            }
        }

        return null;
    }

    public void Dispose()
    {
        _port?.Close();
        _port?.Dispose();
    }
}
