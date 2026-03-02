using System.Diagnostics;
using V2XBridge;

const string ServiceName        = "V2XBridge";
const string ServiceDisplayName = "Katana V2X SignalRGB Bridge";
const string ServiceDescription = "Bridges SignalRGB to the Creative Sound Blaster Katana V2X RGB lighting.";

// --install / --uninstall are handled before the host is built so they work
// whether the exe is run interactively or from a script.
if (args.Length > 0)
{
    switch (args[0].ToLowerInvariant())
    {
        case "--install":   Install();   return;
        case "--uninstall": Uninstall(); return;
    }
}

// Running interactively (not as a service) — guide the user.
if (Environment.UserInteractive)
{
    Console.WriteLine($"""
        V2XBridge — Katana V2X SignalRGB Bridge

        Usage:
          V2XBridge.exe --install     Install and start the Windows service
          V2XBridge.exe --uninstall   Stop and remove the Windows service

        The service must be running for the SignalRGB plugin to detect your device.
        """);
    return;
}

// Running as a Windows service — normal execution path.
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = ServiceName;
});
builder.Services.AddHostedService<Worker>();
builder.Build().Run();

// ---------------------------------------------------------------------------
// Self-install / uninstall helpers
// ---------------------------------------------------------------------------

static void Install()
{
    // If not elevated, re-launch ourselves with admin rights.
    if (!IsElevated())
    {
        Elevate("--install");
        return;
    }

    string exePath = Environment.ProcessPath!;

    RunSc($"create {ServiceName} binPath= \"{exePath}\" start= auto DisplayName= \"{ServiceDisplayName}\"");
    RunSc($"description {ServiceName} \"{ServiceDescription}\"");
    RunSc($"start {ServiceName}");

    Console.WriteLine($"Service '{ServiceName}' installed and started.");
}

static void Uninstall()
{
    if (!IsElevated())
    {
        Elevate("--uninstall");
        return;
    }

    RunSc($"stop {ServiceName}");
    RunSc($"delete {ServiceName}");

    Console.WriteLine($"Service '{ServiceName}' removed.");
}

static void RunSc(string arguments)
{
    using var p = Process.Start(new ProcessStartInfo("sc.exe", arguments)
    {
        UseShellExecute = false,
    })!;
    p.WaitForExit();
}

static bool IsElevated()
{
    using var identity  = System.Security.Principal.WindowsIdentity.GetCurrent();
    var principal = new System.Security.Principal.WindowsPrincipal(identity);
    return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
}

static void Elevate(string arg)
{
    // Re-launch with UAC elevation prompt, then exit this instance.
    Process.Start(new ProcessStartInfo(Environment.ProcessPath!, arg)
    {
        Verb           = "runas",
        UseShellExecute = true,
    });
}
