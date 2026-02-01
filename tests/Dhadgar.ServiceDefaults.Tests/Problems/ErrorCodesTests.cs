using System.Reflection;
using System.Text.RegularExpressions;
using Dhadgar.ServiceDefaults.Problems;
using Xunit;

namespace Dhadgar.ServiceDefaults.Tests.Problems;

public partial class ErrorCodesTests
{
    [Fact]
    public void AllErrorCodes_ShouldBeUnique()
    {
        var errorCodes = GetAllErrorCodeValues();
        var duplicates = errorCodes
            .GroupBy(x => x.Value)
            .Where(g => g.Count() > 1)
            .Select(g => $"'{g.Key}' used by: {string.Join(", ", g.Select(x => x.Name))}")
            .ToList();

        Assert.True(duplicates.Count == 0,
            $"Duplicate error codes found:\n{string.Join("\n", duplicates)}");
    }

    [Fact]
    public void AllErrorCodes_ShouldBeSnakeCase()
    {
        var errorCodes = GetAllErrorCodeValues();
        var violations = errorCodes
            .Where(x => !SnakeCaseRegex().IsMatch(x.Value))
            .Select(x => $"'{x.Name}' = '{x.Value}'")
            .ToList();

        Assert.True(violations.Count == 0,
            $"Error codes not in snake_case format:\n{string.Join("\n", violations)}");
    }

    [GeneratedRegex(@"^[a-z][a-z0-9]*(_[a-z0-9]+)*$")]
    private static partial Regex SnakeCaseRegex();

    private static List<(string Name, string Value)> GetAllErrorCodeValues()
    {
        var results = new List<(string Name, string Value)>();
        var errorCodesType = typeof(ErrorCodes);

        foreach (var nestedType in errorCodesType.GetNestedTypes(BindingFlags.Public | BindingFlags.Static))
        {
            foreach (var field in nestedType.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy))
            {
                if (field.IsLiteral && !field.IsInitOnly && field.FieldType == typeof(string))
                {
                    var value = (string?)field.GetValue(null);
                    if (value != null)
                    {
                        results.Add(($"{nestedType.Name}.{field.Name}", value));
                    }
                }
            }
        }

        return results;
    }
}
