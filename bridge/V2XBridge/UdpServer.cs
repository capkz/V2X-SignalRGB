using System.Net;
using System.Net.Sockets;
using System.Text;

namespace V2XBridge;

/// <summary>
/// UDP server listening on port 12346 for SignalRGB plugin messages.
/// Responds to discovery on port 12347.
/// </summary>
public sealed class UdpServer : IDisposable
{
    private const int ListenPort  = 12346;
    private const int ReplyPort   = 12347;
    private const string DeviceId   = "katana-v2x-0001";
    private const string DeviceName = "Katana V2X";
    private const string DeviceType = "KatanaV2X";

    private readonly UdpClient _udp;
    private readonly KatanaDevice _device;
    private readonly ILogger<UdpServer> _log;

    public UdpServer(KatanaDevice device, ILogger<UdpServer> log)
    {
        _device = device;
        _log    = log;
        _udp    = new UdpClient(new IPEndPoint(IPAddress.Loopback, ListenPort));
    }

    public async Task RunAsync(CancellationToken ct)
    {
        _log.LogInformation("UDP server listening on 127.0.0.1:{Port}", ListenPort);

        while (!ct.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await _udp.ReceiveAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "UDP receive error");
                continue;
            }

            try
            {
                HandlePacket(result.Buffer, result.RemoteEndPoint);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Error handling UDP packet");
            }
        }
    }

    private void HandlePacket(byte[] data, IPEndPoint from)
    {
        string msg = Encoding.UTF8.GetString(data);
        string[] lines = msg.Split('\n');

        if (lines.Length < 2) return;
        if (lines[0].Trim() != "Creative Bridge Plugin") return;

        string command = lines[1].Trim();

        switch (command)
        {
            case "DEVICES":
                HandleDiscovery(from);
                break;

            case "SETRGB":
                HandleSetRgb(lines);
                break;

            default:
                _log.LogDebug("Unknown command: {Cmd}", command);
                break;
        }
    }

    private void HandleDiscovery(IPEndPoint from)
    {
        // Response format matches the Creative plugin protocol:
        // "Creative SignalRGB Service\nDEVICES\n<type>,<name>,<uuid>\n"
        string response =
            $"Creative SignalRGB Service\nDEVICES\n{DeviceType},{DeviceName},{DeviceId}\n";

        byte[] bytes = Encoding.UTF8.GetBytes(response);
        var replyEp  = new IPEndPoint(from.Address, ReplyPort);
        _udp.Send(bytes, bytes.Length, replyEp);

        _log.LogDebug("Sent discovery response to {Ep}", replyEp);
    }

    private void HandleSetRgb(string[] lines)
    {
        // Lines: [0]=header, [1]=SETRGB, [2]=uuid, [3]=base64-encoded RGB bytes
        if (lines.Length < 4) return;

        string uuid       = lines[2].Trim();
        string b64        = lines[3].Trim();

        if (uuid != DeviceId)
        {
            _log.LogDebug("SETRGB for unknown device {Id}", uuid);
            return;
        }

        byte[] rgb;
        try
        {
            rgb = Convert.FromBase64String(b64);
        }
        catch
        {
            _log.LogWarning("Invalid base64 in SETRGB");
            return;
        }

        // rgb = [R0, G0, B0, R1, G1, B1, ..., R6, G6, B6]  (21 bytes for 7 LEDs)
        if (!_device.IsConnected)
        {
            _log.LogDebug("Device not connected, dropping SETRGB");
            return;
        }

        _device.SetColors(rgb);
    }

    public void Dispose() => _udp.Dispose();
}
