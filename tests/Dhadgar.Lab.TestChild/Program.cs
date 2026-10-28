using System.Globalization;
using System.Text.Json;

// A scripted child for the L07 supervisor tests. It prints lines (plain or console-formatter JSON), sleeps, and exits.
// Options: --lines <n> --json --exit <code> --sleep-ms <n> --message <text> --pem --pem-unterminated
// --message, --pem and --pem-unterminated are extensions used by the redaction tests; the rest is the packet's shape.

int lines = 0;
bool json = false;
int exitCode = 0;
int sleepMs = 0;
string message = "line";
bool pem = false;
bool pemUnterminated = false;

for (int i = 0; i < args.Length; i++)
{
    string current = args[i];
    bool hasValue = i + 1 < args.Length;
    switch (current)
    {
        case "--json":
            json = true;
            break;
        case "--pem":
            pem = true;
            break;
        case "--pem-unterminated":
            pemUnterminated = true;
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

if (pem)
{
    Console.Out.WriteLine("-----BEGIN PRIVATE KEY-----");
    Console.Out.WriteLine("MIIEvQIBADANBgkqhkiG9w0BAQEFAASC");
    Console.Out.WriteLine("-----END PRIVATE KEY-----");
    Console.Out.WriteLine("after-pem");
}

// A key block that never ends (a parse error logged as a BEGIN line), then a new log record and a crash (N3).
if (pemUnterminated)
{
    Console.Out.WriteLine("-----BEGIN PRIVATE KEY-----");
    Console.Out.WriteLine("MIIEvQIBADANBgkqhkiG9w0BAQEFAASC");
    Console.Out.WriteLine("info: X[0]");
    Console.Out.WriteLine("Unhandled exception. System.Exception: boom");
}

Console.Out.Flush();
if (sleepMs > 0)
{
    Thread.Sleep(sleepMs);
}

return exitCode;
