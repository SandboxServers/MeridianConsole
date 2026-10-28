using System.Globalization;
using System.Text.Json;

// A scripted child for the L07 supervisor tests. It prints lines (plain or console-formatter JSON), sleeps, and exits.
// Options: --lines <n> --json --exit <code> --sleep-ms <n> --message <text>
// --message is an extension used by the redaction tests; the rest is the packet's shape.

int lines = 0;
bool json = false;
int exitCode = 0;
int sleepMs = 0;
string message = "line";

for (int i = 0; i < args.Length; i++)
{
    string current = args[i];
    bool hasValue = i + 1 < args.Length;
    switch (current)
    {
        case "--json":
            json = true;
            break;
        case "--lines" when hasValue && int.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out int n):
            lines = n;
            i++;
            break;
        case "--exit" when hasValue && int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int code):
            exitCode = code;
            i++;
            break;
        case "--sleep-ms" when hasValue && int.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out int ms):
            sleepMs = ms;
            i++;
            break;
        case "--message" when hasValue:
            message = args[i + 1];
            i++;
            break;
        default:
            Console.Error.WriteLine("unknown or incomplete argument: " + current);
            return 64;
    }
}

for (int line = 1; line <= lines; line++)
{
    string text = message + " " + line.ToString(CultureInfo.InvariantCulture);
    if (json)
    {
        string entry = JsonSerializer.Serialize(new
        {
            Timestamp = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            EventId = 0,
            LogLevel = "Information",
            Category = "Dhadgar.Lab.TestChild",
            Message = text,
        });
        Console.Out.WriteLine(entry);
    }
    else
    {
        Console.Out.WriteLine(text);
    }
}

Console.Out.Flush();
if (sleepMs > 0)
{
    Thread.Sleep(sleepMs);
}

return exitCode;
