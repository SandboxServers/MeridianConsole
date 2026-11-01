namespace Dhadgar.Lab.Hunt;

/// <summary>The state of one enrollment token in the model.</summary>
public enum HuntTokenState
{
    /// <summary>Minted and not yet used.</summary>
    Unused,

    /// <summary>Used by one successful enrollment.</summary>
    Used,

    /// <summary>Past its validity. No op produces it yet; kept so the model matches the product states.</summary>
    Expired,
}

/// <summary>The kinds of model op (the packet's op list, in generator order).</summary>
public enum HuntOpKind
{
    /// <summary>Mints a token (<c>nodes_token_mint</c>).</summary>
    Mint,

    /// <summary>Enrolls with an unused or used token and the stored key (<c>nodes_enroll_probe</c>).</summary>
    Enroll,

    /// <summary>Enrolls again with a new key (<c>nodes_enroll_probe</c> without <c>key_ref</c>).</summary>
    EnrollAgain,

    /// <summary>Cuts nodes-postgres (<c>fault_proxy_disable</c>).</summary>
    PgDown,

    /// <summary>Heals the cut (<c>fault_heal</c>).</summary>
    PgUp,

    /// <summary>Restarts the nodes service (<c>svc_restart</c>). Skipped while PgDown (D-ML119).</summary>
    RestartNodes,
}

/// <summary>One model op. <see cref="Token"/> indexes the token list for Enroll and EnrollAgain.</summary>
/// <param name="Kind">The op.</param>
/// <param name="Token">The token index, 0 for ops without one.</param>
public sealed record HuntOp(HuntOpKind Kind, int Token = 0)
{
    /// <summary>The validity Mint asks for, in minutes.</summary>
    public const int ValidityMinutes = 60;

    /// <inheritdoc />
    public override string ToString() => Kind switch
    {
        HuntOpKind.Mint => "Mint(" + ValidityMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")",
        HuntOpKind.Enroll => "Enroll(" + Token.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")",
        HuntOpKind.EnrollAgain => "EnrollAgain(" + Token.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")",
        _ => Kind + "()",
    };
}

/// <summary>What the model expects an enroll op to return.</summary>
public enum HuntExpectation
{
    /// <summary>No check: the op is a no-op or has no HTTP result to compare.</summary>
    None,

    /// <summary>201 with a node.</summary>
    Created,

    /// <summary>401: the token is already used.</summary>
    Unauthorized,

    /// <summary>No 2xx while Postgres is cut. A 5xx here is KI-F67 (known), never new.</summary>
    NotSuccess,
}

/// <summary>
/// The enrollment model (A2): <c>{tokens, nodes, pgDown}</c>. <see cref="Expect"/> says what the product must answer;
/// <see cref="Apply"/> moves the model to the expected state. The model is pure and holds no lab handles.
/// </summary>
public sealed class EnrollmentModel
{
    /// <summary>The most tokens one case mints.</summary>
    public const int MaxTokens = 8;

    private readonly List<HuntTokenState> _tokens = [];

    /// <summary>The token states, in mint order.</summary>
    public IReadOnlyList<HuntTokenState> Tokens => _tokens;

    /// <summary>The number of nodes the model expects.</summary>
    public int Nodes { get; private set; }

    /// <summary>True between <c>PgDown</c> and <c>PgUp</c>.</summary>
    public bool PgDown { get; private set; }

    /// <summary>The number of used tokens the model expects.</summary>
    public int UsedTokens => _tokens.Count(token => token == HuntTokenState.Used);

    /// <summary>Gets what the product must answer for <paramref name="op"/> in the current state.</summary>
    public HuntExpectation Expect(HuntOp op)
    {
        ArgumentNullException.ThrowIfNull(op);
        switch (op.Kind)
        {
            case HuntOpKind.Enroll:
            case HuntOpKind.EnrollAgain:
                if (op.Token < 0 || op.Token >= _tokens.Count)
                {
                    return HuntExpectation.None;
                }

                if (PgDown)
                {
                    return HuntExpectation.NotSuccess;
                }

                return _tokens[op.Token] == HuntTokenState.Unused ? HuntExpectation.Created : HuntExpectation.Unauthorized;
            default:
                return HuntExpectation.None;
        }
    }

    /// <summary>Moves the model to the state the expectation implies. A <see cref="HuntExpectation.None"/> op changes nothing.</summary>
    /// <param name="op">The op.</param>
    /// <param name="expectation">What <see cref="Expect"/> returned for it, before the op.</param>
    public void Apply(HuntOp op, HuntExpectation expectation)
    {
        ArgumentNullException.ThrowIfNull(op);
        switch (op.Kind)
        {
            case HuntOpKind.Mint:
                if (_tokens.Count < MaxTokens)
                {
                    _tokens.Add(HuntTokenState.Unused);
                }

                break;
            case HuntOpKind.Enroll:
            case HuntOpKind.EnrollAgain:
                if (expectation == HuntExpectation.Created)
                {
                    _tokens[op.Token] = HuntTokenState.Used;
                    Nodes++;
                }

                break;
            case HuntOpKind.PgDown:
                PgDown = true;
                break;
            case HuntOpKind.PgUp:
                PgDown = false;
                break;
            case HuntOpKind.RestartNodes:
                break;
        }
    }
}
