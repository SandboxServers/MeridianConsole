using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Dhadgar.Lab.Results;

/// <summary>
/// One failure signature of a row or a standalone violation (C12). <c>Service</c> is the answering service (http), the logging
/// service (exc) or the invariant's service (inv); <c>InvariantId</c> is set for inv signatures only.
/// </summary>
/// <param name="Kind">The signature kind: exc, http, clause, inv, hist, twin, peer, env or ver.</param>
/// <param name="Text">The signature text (C12 table).</param>
/// <param name="Service">The service the signature is scoped to, or null.</param>
/// <param name="InvariantId">The invariant id for inv signatures, or null.</param>
public sealed record FailureSignature(string Kind, string Text, string? Service, string? InvariantId);

/// <summary>An exception block from a row window: the type and its frames, outermost first.</summary>
public sealed record ExceptionEvidence(string Service, string ExceptionType, IReadOnlyList<string> Frames);

/// <summary>An HTTP response of a row. Only 500-599 responses produce a signature.</summary>
public sealed record HttpEvidence(string Service, int Status, string? ErrorCode, string Method, string? RouteTemplate,
    string Path, IReadOnlyList<string> FaultKinds);

/// <summary>A clause of a row. Only <c>NotHeld</c> and <c>Unverified</c> clauses produce a signature.</summary>
public sealed record ClauseEvidence(string Section, string RowId, string ClauseId, string State);

/// <summary>An invariant violation. Only unallowed violations produce a signature.</summary>
public sealed record ViolationEvidence(string InvariantId, string Service, string CheckId, bool Allowed);

/// <summary>A history rule violation (L51). Only required rules produce a signature.</summary>
public sealed record HistoryViolationEvidence(string Model, string RuleId, string Scenario, bool Required);

/// <summary>
/// Everything a row carries that can produce a failure signature. <paramref name="ExtraSignatures"/> holds the signatures
/// that L53 (twin), L58 (peer), L57 (env) and L60 (ver) write, already built with their kind prefix.
/// </summary>
public sealed record RowEvidence(string Section, string RowId, IReadOnlyList<ExceptionEvidence> Exceptions,
    IReadOnlyList<HttpEvidence> Http, IReadOnlyList<ClauseEvidence> Clauses, IReadOnlyList<ViolationEvidence> Violations,
    IReadOnlyList<HistoryViolationEvidence> History, IReadOnlyList<FailureSignature> ExtraSignatures);

/// <summary>
/// Builds the failure signatures of a row (C12). Normalisation is pure: the same input always yields the same text and id.
/// </summary>
public static class SignatureBuilder
{
    /// <summary>The kind prefixes C12 allows.</summary>
    public static readonly string[] Kinds = ["exc", "http", "clause", "inv", "hist", "twin", "peer", "env", "ver"];

    private const int FrameCount = 3;
    private const int SignatureIdLength = 16;
    private const string NoValue = "-";

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);
    private static readonly Regex CompilerClass = new(@"^<>(c(__DisplayClass.*)?)?$", RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex CompilerMethod = new(@"^<([^>]+)>[a-z]__[0-9]+", RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex StateMachine = new(@"^<([^>]+)>d__[0-9]+$", RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex BareCompilerType = new(@"^[a-z]__[0-9]+$", RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex Arity = new(@"`[0-9]+(\[[^\]]*\])?", RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex Offset = new(@"\+0x[0-9a-fA-F]+", RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex GuidPattern = new(
        @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}",
        RegexOptions.CultureInvariant,
        RegexTimeout);
    private static readonly Regex DigitRun = new("[0-9]+", RegexOptions.CultureInvariant, RegexTimeout);

    /// <summary>
    /// Every failure signature of the row, in C12 order: exc, http (5xx), clause (NotHeld or Unverified), inv (unallowed),
    /// hist (required), then the extra texts.
    /// </summary>
    public static IReadOnlyList<FailureSignature> BuildAll(RowEvidence row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var signatures = new List<FailureSignature>();

        foreach (ExceptionEvidence exception in row.Exceptions)
        {
            signatures.Add(Exception(exception));
        }

        foreach (HttpEvidence http in row.Http.Where(h => h.Status is >= 500 and <= 599))
        {
            signatures.Add(Http(http));
        }

        foreach (ClauseEvidence clause in row.Clauses.Where(c => c.State is "NotHeld" or "Unverified"))
        {
            signatures.Add(Clause(clause.Section, clause.RowId, clause.ClauseId, clause.State));
        }

        foreach (ViolationEvidence violation in row.Violations.Where(v => !v.Allowed))
        {
            signatures.Add(Invariant(violation.InvariantId, violation.Service, violation.CheckId));
        }

        foreach (HistoryViolationEvidence history in row.History.Where(h => h.Required))
        {
            signatures.Add(History(history.Model, history.RuleId, history.Scenario));
        }

        signatures.AddRange(row.ExtraSignatures);

        return signatures;
    }

    /// <summary>
    /// The bucketing signature: the first exc, else the first 5xx http, else the first failing clause, else the first inv,
    /// else the first hist. Null when the row has no failure signature.
    /// </summary>
    public static FailureSignature? Primary(RowEvidence row)
    {
        IReadOnlyList<FailureSignature> all = BuildAll(row);
        string[] order = ["exc", "http", "clause", "inv", "hist", "twin"];
        foreach (string kind in order)
        {
            FailureSignature? first = all.FirstOrDefault(s => s.Kind == kind);
            if (first is not null)
            {
                return first;
            }
        }

        return null;
    }

    /// <summary>The signature id: the first 16 lowercase hex characters of SHA-256 over the UTF-8 text.</summary>
    public static string SignatureId(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexStringLower(hash)[..SignatureIdLength];
    }

    /// <summary>Builds an <c>exc</c> signature: <c>exc:&lt;type&gt;|&lt;frame1&gt;|&lt;frame2&gt;|&lt;frame3&gt;</c>.</summary>
    public static FailureSignature Exception(ExceptionEvidence exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var frames = new List<string>();
        foreach (string raw in exception.Frames.Take(FrameCount))
        {
            frames.Add(NormalizeFrame(raw));
        }

        while (frames.Count < FrameCount)
        {
            frames.Add(NoValue);
        }

        string text = "exc:" + exception.ExceptionType.Trim() + "|" + string.Join('|', frames);
        return new FailureSignature("exc", text, exception.Service, null);
    }

    /// <summary>
    /// Builds an <c>http</c> signature: <c>http:&lt;status&gt;|&lt;errorCode or -&gt;|&lt;METHOD&gt;|&lt;route&gt;|&lt;faults or none&gt;</c>.
    /// The route is the endpoint template when known, else the path with GUIDs as <c>{id}</c> and digit runs as <c>{n}</c>.
    /// </summary>
    public static FailureSignature Http(HttpEvidence http)
    {
        ArgumentNullException.ThrowIfNull(http);
        string route = http.RouteTemplate is { Length: > 0 } template ? template : RouteOf(http.Path);
        string faults = http.FaultKinds.Count == 0
            ? "none"
            : string.Join('+', http.FaultKinds.OrderBy(k => k, StringComparer.Ordinal));
        string text = "http:" + http.Status + "|" + (string.IsNullOrEmpty(http.ErrorCode) ? NoValue : http.ErrorCode)
            + "|" + http.Method.ToUpperInvariant() + "|" + route + "|" + faults;
        return new FailureSignature("http", text, http.Service, null);
    }

    /// <summary>Builds a <c>clause</c> signature: <c>clause:&lt;section&gt;/&lt;row&gt;/&lt;clause id&gt;|&lt;state&gt;</c>.</summary>
    public static FailureSignature Clause(string section, string rowId, string clauseId, string state) =>
        new("clause", "clause:" + section + "/" + rowId + "/" + clauseId + "|" + state, null, null);

    /// <summary>Builds an <c>inv</c> signature: <c>inv:&lt;invariant id&gt;|&lt;service&gt;|&lt;check id&gt;</c>.</summary>
    public static FailureSignature Invariant(string invariantId, string service, string checkId) =>
        new("inv", "inv:" + invariantId + "|" + service + "|" + checkId, service, invariantId);

    /// <summary>Builds a <c>hist</c> signature: <c>hist:&lt;model&gt;|&lt;rule id&gt;|&lt;scenario&gt;</c>.</summary>
    public static FailureSignature History(string model, string ruleId, string scenario) =>
        new("hist", "hist:" + model + "|" + ruleId + "|" + scenario, null, null);

    /// <summary>Builds a <c>twin</c> signature (C12): <c>twin:&lt;experiment id&gt;|&lt;pointer&gt;</c>, one per pointer that did not hold.</summary>
    public static FailureSignature Twin(string experimentId, string jsonPointer) =>
        new("twin", "twin:" + experimentId + "|" + jsonPointer, null, null);

    /// <summary>
    /// Normalises one stack frame to <c>Namespace.Type.Method</c>: line numbers, offsets, parameters, generic arity and the
    /// compiler-generated names (display classes, lambdas and state machines) are removed, so a compiler-generated frame
    /// becomes its enclosing method.
    /// </summary>
    public static string NormalizeFrame(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        string s = raw.Trim();
        if (s.StartsWith("at ", StringComparison.Ordinal))
        {
            s = s[3..];
        }

        int paren = s.IndexOf('(', StringComparison.Ordinal);
        if (paren >= 0)
        {
            s = s[..paren];
        }

        int inAt = s.IndexOf(" in ", StringComparison.Ordinal);
        if (inAt >= 0)
        {
            s = s[..inAt];
        }

        s = Offset.Replace(s, string.Empty);
        s = Arity.Replace(s, string.Empty).Trim();

        var segments = new List<string>();
        bool skipMoveNext = false;
        foreach (string segment in s.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (skipMoveNext && segment == "MoveNext")
            {
                skipMoveNext = false;
                continue;
            }

            skipMoveNext = false;
            Match stateMachine = StateMachine.Match(segment);
            if (stateMachine.Success)
            {
                segments.Add(stateMachine.Groups[1].Value);
                skipMoveNext = true;
                continue;
            }

            Match lambda = CompilerMethod.Match(segment);
            if (lambda.Success)
            {
                segments.Add(lambda.Groups[1].Value);
                continue;
            }

            if (CompilerClass.IsMatch(segment) || BareCompilerType.IsMatch(segment))
            {
                continue;
            }

            segments.Add(segment);
        }

        return string.Join('.', segments);
    }

    /// <summary>The route of a request path: GUIDs become <c>{id}</c>, digit runs <c>{n}</c>, the query string is dropped.</summary>
    public static string RouteOf(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        string route = path;
        int query = route.IndexOf('?', StringComparison.Ordinal);
        if (query >= 0)
        {
            route = route[..query];
        }

        route = GuidPattern.Replace(route, "{id}");
        return DigitRun.Replace(route, "{n}");
    }
}
