using Dhadgar.Agent.Windows.Uat.Stack;
using Xunit;

namespace Dhadgar.Agent.Windows.Uat.Tests;

public class DockerArgsTests
{
    [Fact]
    public void PostgresArguments_PasswordHasNoValue_AndBindsLoopbackOnly()
    {
        var args = DockerStack.PostgresRunArguments("meridian-uat-pg-x", "x", 54321);

        AssertEnvWithoutValue(args, "POSTGRES_PASSWORD");
        AssertLoopbackOnly(args);
        Assert.Contains("meridian-uat=x", args);
    }

    [Fact]
    public void RabbitMqArguments_PasswordHasNoValue_AndBindsLoopbackOnly()
    {
        var args = DockerStack.RabbitMqRunArguments("meridian-uat-mq-x", "x", 5672);

        AssertEnvWithoutValue(args, "RABBITMQ_DEFAULT_PASS");
        AssertLoopbackOnly(args);
        Assert.Contains("meridian-uat=x", args);
    }

    [Fact]
    public void RabbitMqProbe_RunsAsTheBrokerUser_SoRootNeverCreatesTheCookie()
    {
        var args = DockerStack.RabbitMqProbeArguments("meridian-uat-mq-x");

        Assert.Equal(
            new[] { "exec", "--user", "rabbitmq", "meridian-uat-mq-x", "rabbitmq-diagnostics", "-q", "ping" },
            args);
    }

    private static void AssertEnvWithoutValue(IReadOnlyList<string> args, string name)
    {
        Assert.Contains(args, a => a == name);
        Assert.DoesNotContain(args, a => a.StartsWith(name + "=", StringComparison.Ordinal));
        var index = args.ToList().IndexOf(name);
        Assert.Equal("-e", args[index - 1]);
    }

    private static void AssertLoopbackOnly(IReadOnlyList<string> args)
    {
        var publishes = new List<string>();
        for (var i = 0; i < args.Count - 1; i++)
        {
            if (args[i] == "-p")
            {
                publishes.Add(args[i + 1]);
            }
        }

        Assert.NotEmpty(publishes);
        Assert.All(publishes, p => Assert.StartsWith("127.0.0.1:", p, StringComparison.Ordinal));
    }
}
