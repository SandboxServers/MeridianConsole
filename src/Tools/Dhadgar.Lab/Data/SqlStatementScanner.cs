using Dhadgar.Shared.Results;

namespace Dhadgar.Lab.Data;

/// <summary>
/// Refuses SQL that could run more than one statement (C4 <c>db_query</c>). Quoted literals, quoted identifiers and comments
/// are skipped, so <c>select ';'</c> is allowed. Backslashes are refused anywhere, and dollar signs outside literals and
/// comments, because their meaning depends on server settings or dollar quoting, which this scanner does not model.
/// </summary>
internal static class SqlStatementScanner
{
    private const int Unterminated = -1;
    private const int BackslashFound = -2;

    /// <summary>
    /// Returns <c>[Lab.InvalidArgument]</c> when the text holds more than one statement or uses syntax the scanner refuses.
    /// </summary>
    public static Result Check(string sql)
    {
        ArgumentNullException.ThrowIfNull(sql);

        bool sawSemicolon = false;
        int blockDepth = 0;
        int i = 0;
        while (i < sql.Length)
        {
            char c = sql[i];
            char next = i + 1 < sql.Length ? sql[i + 1] : '\0';

            if (blockDepth > 0)
            {
                // Postgres nests block comments, so the scanner does too.
                if (c == '/' && next == '*')
                {
                    blockDepth++;
                    i += 2;
                }
                else if (c == '*' && next == '/')
                {
                    blockDepth--;
                    i += 2;
                }
                else
                {
                    i++;
                }

                continue;
            }

            if (c == '-' && next == '-')
            {
                int newline = sql.IndexOf('\n', i);
                i = newline < 0 ? sql.Length : newline + 1;
                continue;
            }

            if (c == '/' && next == '*')
            {
                blockDepth = 1;
                i += 2;
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            if (c == '\\')
            {
                return Refuse("backslashes are not allowed in db_query SQL");
            }

            if (sawSemicolon)
            {
                return Refuse("only one statement is allowed");
            }

            if (c == ';')
            {
                sawSemicolon = true;
                i++;
                continue;
            }

            if (c == '$')
            {
                return Refuse("dollar signs are not allowed outside literals and comments");
            }

            if (c == '\'' || c == '"')
            {
                int end = FindClosingQuote(sql, i, c);
                if (end == BackslashFound)
                {
                    return Refuse("backslashes are not allowed in db_query SQL");
                }

                if (end == Unterminated)
                {
                    return Refuse("unterminated literal or identifier");
                }

                i = end + 1;
                continue;
            }

            i++;
        }

        return blockDepth > 0 ? Refuse("unterminated comment") : Result.Success();
    }

    private static int FindClosingQuote(string sql, int start, char quote)
    {
        for (int j = start + 1; j < sql.Length; j++)
        {
            if (sql[j] == '\\')
            {
                return BackslashFound;
            }

            if (sql[j] != quote)
            {
                continue;
            }

            // A doubled quote is an escaped quote inside the literal.
            if (j + 1 < sql.Length && sql[j + 1] == quote)
            {
                j++;
                continue;
            }

            return j;
        }

        return Unterminated;
    }

    private static Result Refuse(string reason) => Result.Failure($"[Lab.InvalidArgument] {reason}");
}
