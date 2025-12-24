using System.CommandLine;
using Dhadgar.Cli;

var root = new RootCommand("Dhadgar CLI (Meridian Console) — troubleshooting and ops tooling");

root.SetHandler(() =>
{
    Console.WriteLine(Hello.Message);
    Console.WriteLine("Use --help to see available commands.");
});

var urlOpt = new Option<string>(
    name: "--url",
    getDefaultValue: () => "http://localhost:5000/healthz",
    description: "Health endpoint URL");

var ping = new Command("ping", "Ping a service health endpoint");
ping.AddOption(urlOpt);

ping.SetHandler(async (string url) =>
{
    using var http = new HttpClient();
    var res = await http.GetAsync(url);
    var body = await res.Content.ReadAsStringAsync();

    Console.WriteLine($"{(int)res.StatusCode} {res.ReasonPhrase}");
    if (!string.IsNullOrWhiteSpace(body))
        Console.WriteLine(body);
}, urlOpt);

root.AddCommand(ping);

return await root.InvokeAsync(args);
