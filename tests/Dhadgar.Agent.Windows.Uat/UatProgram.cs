namespace Dhadgar.Agent.Windows.Uat;

/// <summary>
/// Entry point of the Windows agent UAT driver (D-S225). Machine-changing verbs are refused unless
/// the caller passes every guard; see <see cref="UatRunner"/>.
/// </summary>
internal static class UatProgram
{
    public static async Task<int> Main(string[] args)
    {
        var parsed = UatArgs.Parse(args);
        if (!parsed.IsSuccess)
        {
            Console.Error.WriteLine(parsed.Error);
            return UatExitCodes.Refused;
        }

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
        return await UatRunner.RunAsync(parsed.Value!, cts.Token);
    }
}
