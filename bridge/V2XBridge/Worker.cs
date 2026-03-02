namespace V2XBridge;

/// <summary>
/// Top-level background service: connects to the Katana V2X and runs the UDP server.
/// Retries device connection on failure with exponential back-off.
/// </summary>
public sealed class Worker : BackgroundService
{
    private readonly ILogger<Worker> _log;
    private readonly ILoggerFactory _logFactory;

    public Worker(ILogger<Worker> log, ILoggerFactory logFactory)
    {
        _log        = log;
        _logFactory = logFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _log.LogInformation("V2XBridge starting");

        int attempt = 0;
        while (!ct.IsCancellationRequested)
        {
            attempt++;
            using var device = new KatanaDevice(_logFactory.CreateLogger<KatanaDevice>());
            try
            {
                await device.ConnectAsync(ct);
            }
            catch (Exception ex)
            {
                int delay = Math.Min(30, (int)Math.Pow(2, attempt - 1));
                _log.LogWarning(ex, "Device connection failed (attempt {N}), retrying in {D}s", attempt, delay);
                await Task.Delay(TimeSpan.FromSeconds(delay), ct);
                continue;
            }

            attempt = 0; // reset back-off on successful connect

            using var udp = new UdpServer(device, _logFactory.CreateLogger<UdpServer>());
            try
            {
                await udp.RunAsync(ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _log.LogWarning(ex, "UDP server error, reconnecting...");
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
        }

        _log.LogInformation("V2XBridge stopped");
    }
}
