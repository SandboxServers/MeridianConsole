# P06 ruling check 2 (range 356eeb1..8947508, reviewed head 8947508)

## Verdict

ship

## Fix notes

None.

## Not fixed

- Previous N1 is applied. src/Dhadgar.Gateway/README.md:541 now ends "so on every other route `Global` is the only limiter that runs, and on `PerAgent` routes `PerAgent` is." The text matches the ruled replacement exactly, and `on those routes` no longer appears in the line.
- The range 356eeb1..8947508 changes only that one line in src/Dhadgar.Gateway/README.md (1 insertion, 1 deletion). No code, tests or other files changed, so the earlier build and test results (0 warnings, 144 Gateway tests passed) still stand.
- security-review label needed: Gateway auth path, rate limiting and client-certificate forwarding (carried over from the previous report).
- Owner item, not blocking P06: D-S220 goes to P15 and the closing report (carried over).
