using MacroKeyboard.Plugin.HomeAssistant;
using System.Text;

// Ensure stdout/stderr are flushed immediately — the backend captures them via
// redirected pipes, and buffered output would never appear in the backend log.
Console.OutputEncoding = Encoding.UTF8;
var stdout = new StreamWriter(Console.OpenStandardOutput(), Encoding.UTF8) { AutoFlush = true };
var stderr = new StreamWriter(Console.OpenStandardError(),  Encoding.UTF8) { AutoFlush = true };
Console.SetOut(stdout);
Console.SetError(stderr);

// Parse Stream Deck standard CLI args:
//   -port PORT -pluginUUID UUID -registerEvent registerPlugin -info JSON
int port = 28196;
string pluginUuid = string.Empty;

for (int i = 0; i < args.Length - 1; i++)
{
    switch (args[i])
    {
        case "-port":      port = int.Parse(args[i + 1]);     break;
        case "-pluginUUID": pluginUuid = args[i + 1];         break;
    }
}

if (string.IsNullOrEmpty(pluginUuid))
{
    Console.Error.WriteLine(
        "Usage: MacroKeyboard.Plugin.HomeAssistant.exe " +
        "-port PORT -pluginUUID ID -registerEvent registerPlugin -info JSON");
    return 1;
}

Console.WriteLine($"[HA Plugin] Starting — port={port}, uuid={pluginUuid}");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

var sd = new SdConnection(port, pluginUuid);
await using var controller = new PluginController(sd);

try
{
    await sd.ConnectAsync(cts.Token);
    Console.WriteLine("[HA Plugin] Connected to backend");

    // Request stored global settings immediately so the plugin can connect to HA
    // without waiting for the user to open the Property Inspector.
    await sd.GetGlobalSettingsAsync(cts.Token);

    await sd.RunAsync(cts.Token);
}
catch (OperationCanceledException) { }
catch (Exception ex)
{
    Console.Error.WriteLine($"[HA Plugin] Fatal: {ex.Message}");
    return 1;
}

Console.WriteLine("[HA Plugin] Shutdown complete");
return 0;
