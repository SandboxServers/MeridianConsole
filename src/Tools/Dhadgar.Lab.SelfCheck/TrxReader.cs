using System.Xml;
using System.Xml.Linq;
using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.SelfCheck;

/// <summary>One <c>UnitTestResult</c> of a TRX file: the test's name and its <c>outcome</c> attribute.</summary>
/// <param name="TestName">The <c>testName</c> attribute, for example <c>Dhadgar.Lab.Tests.Faults.FaultBookTests.X</c>.</param>
/// <param name="Outcome">The <c>outcome</c> attribute as written: <c>Passed</c>, <c>Failed</c>, <c>NotExecuted</c>, ...</param>
public sealed record TrxTestResult(string TestName, string Outcome);

/// <summary>
/// Reads the <c>UnitTestResult</c> elements of a TRX log (written by <c>dotnet test --logger trx</c>). DTDs are refused, so
/// a hostile log cannot expand entities. A malformed log is a failure, never an empty list.
/// </summary>
public static class TrxReader
{
    /// <summary>Parses TRX text.</summary>
    /// <param name="trx">The TRX XML.</param>
    public static Result<IReadOnlyList<TrxTestResult>> Read(string trx)
    {
        ArgumentNullException.ThrowIfNull(trx);

        XDocument document;
        try
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            using var stringReader = new StringReader(trx);
            using var xmlReader = XmlReader.Create(stringReader, settings);
            document = XDocument.Load(xmlReader);
        }
        catch (XmlException ex)
        {
            return Result<IReadOnlyList<TrxTestResult>>.Failure($"[Lab.InvalidArgument] trx is not valid XML: {ex.Message}");
        }

        var results = new List<TrxTestResult>();
        foreach (XElement element in document.Descendants().Where(e => e.Name.LocalName == "UnitTestResult"))
        {
            string? name = (string?)element.Attribute("testName");
            string? outcome = (string?)element.Attribute("outcome");
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(outcome))
            {
                return Result<IReadOnlyList<TrxTestResult>>.Failure("[Lab.InvalidArgument] a UnitTestResult lacks testName or outcome");
            }

            results.Add(new TrxTestResult(name, outcome));
        }

        return Result<IReadOnlyList<TrxTestResult>>.Success(results);
    }
}
