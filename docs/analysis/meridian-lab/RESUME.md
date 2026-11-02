# RESUME: meridian-lab

Ledger: [README.md](README.md) (current through the l35m and l37 rulings, addendum 1 and the 2026-11-02 owner answers). Next free: **F143, D-ML168, KG-W5-24, KG-W7-9**. Packets: [work-packets.md](work-packets.md). Plan: [next-batch plan](worknotes/next-batch-plan.md). Rulings: [l35m](worknotes/l35m-ruling-1.md), [l37 + addendum 1](worknotes/l37-ruling-1.md).

## State (2026-11-02)

- `campaign/meridian-lab` tip **d5f45ef7** (batch 2 tip fix; local, one ahead of pushed `6e3b1082`; worktree `.claude/worktrees/meridian-lab`).
- Batch 1 merged: L39 `54ff5d1b`, L27d `64d889bd`, L29b `d417390e`, L54b `1cf07e96`. Batch 2 merged: L27e `3c30c948`, L35m `4532ef7c`, L37 `6e3b1082`; each `ship` on review and security. Batch 2 tip review `ship` on pass 2.
- Owner answers (2026-11-02): D-ML37/38 yes; D-ML133 yes and D-ML120 "start and report not-ready" (one Nodes PR); D-ML107 yes (fuzz 500s PR); D-ML165 yes (Identity org_id PR); D-ML166 yes (one CLI fix PR). D-ML165/166 Accepted (owner). Product PRs are separate from the campaign, each merged only on the user's yes.

## Next actions (orchestrator)

1. Docker runs owed, each needing a **new user yes**: L35m `LabDbDockerTests` (closes KG-W7-1, D-ML161); the live `cli.toml` run (L37 ruling section 5, live half of the D-ML163 seam). Both were merge gates and the packets merged first: a red goes to the architect.
2. L35 (Docker + chromium, user's yes); architect specs L05b, L25g, L28b, L55b, then L52b; L62 after L52b; L38/L56 after L35.
3. Architect plans the four product PRs (Nodes outbox + not-ready start; fuzz 500s; Identity org_id; CLI fixes), each with its seam test.
4. L00 then wave 6 on the Windows lab (ask before each machine change); waves 8-10; campaign review; L47; one PR with `security-review` and the user's yes (D-ML131).

## Cleanup (ops; `git worktree remove` only, never prune)

Merged packet worktrees (ml-l21g, l08b, l30, l34, l36, l19n, l39, l27d, l29b, l54b, l27e, l35m, l37), `ml-l19n-red`, `nr1-nodes-ef-retry`, R7-R11 worktrees, about 90 local `ml/*` branches. Reports under each worktree are gitignored and local only.

## Watch-outs

- KG-W5-2, -19, -20 and -21 name owners that are now merged (L29b, L54b, L27e); their closure is not ruled yet (next architect pass or the campaign review).
- `AuthStackDockerTests` (KG-W7-6) rides the wave 7 Docker run (D-ML74); `MERIDIAN_LAB_S6_WIPE=1` is a separate user yes.
- Non-Docker filter: `RequiresDocker!=true&RequiresWindowsContainers!=true&RequiresNetwork!=true`. Never two full suites at once. Flake: `ModelHunterTests.Hunter_SameSeed_SameFirstOpList`.
- No `publish -r`, no `restore -r`, no `git worktree prune`. Never print the lab token or a lease id. Lab touches only `meridian-lab=1` containers.
- Vertical-slice (main checkout): #247 needs the user's yes when CI is green; its RESUME is stale.
