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

// Running interactively with no args — double-click install flow.
if (Environment.UserInteractive)
{
    Install();
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
    Console.WriteLine("Press any key to close...");
    Console.ReadKey();
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
