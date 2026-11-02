# RESUME: meridian-lab

Ledger: [README.md](README.md) (current through the docker-run-15 ruling). Next free: **F148, D-ML175, KG-W5-24, KG-W7-11**. Packets: [work-packets.md](work-packets.md). Plan: [next-batch plan](worknotes/next-batch-plan.md). Rulings: [l35m](worknotes/l35m-ruling-1.md), [l37 + addendum 1](worknotes/l37-ruling-1.md), [docker run 15](worknotes/docker-run-15-ruling.md).

## State (2026-11-02)

- `campaign/meridian-lab` tip **ce7e37c9** (local; pushed `2efc332f`; worktree `.claude/worktrees/meridian-lab`). Merges: `0bab7454` L35mf, `ce7e37c9` L37f1-L37f3.
- Batches 1 and 2 merged (see README). Run 15: `LabDbDockerTests` 3/4 (F143); live `cli.toml` could not start (F144); F145-F147 found in source.
- L35mf merged (`33d7188c`, merge `0bab7454`; review 1 `ship`; no security review needed). L37f1-L37f3 merged (`ml/l37-live` `8f22f0e2`, merge `ce7e37c9`; packet review and security-advisor `ship`), in D-ML174 order. Batch 3 tip review: `ship` on pass 1.
- Owner answers (2026-11-02): D-ML37/38 yes; D-ML133 yes and D-ML120 "start and report not-ready" (one Nodes PR); D-ML107 yes; D-ML165 yes; D-ML166 yes. Product PRs are separate from the campaign, each merged only on the user's yes.

## Next actions (orchestrator)

1. **Next step:** Docker runs of the [run 15 ruling](worknotes/docker-run-15-ruling.md) section 7, serially, **each after a new user yes**: run 1 `LabDbDockerTests` (closes KG-W7-1, half of KG-W7-9); run 2 `CliSpecE2ETests` (closes KG-W7-10, the L37 gate, live half of D-ML163); run 3 S-L6 `LabSelfTestE2ETests` (rest of KG-W7-9); run 4 optional `LinuxPoolDockerTests`. `MERIDIAN_LAB_S6_WIPE=1` is a separate yes. Every red goes to the architect with the raw report.
2. L35 (Docker + chromium, user's yes); architect specs L05b, L25g, L28b, L55b, then L52b; L62 after L52b; L38/L56 after L35.
3. Architect plans the four product PRs (Nodes outbox + not-ready start; fuzz 500s; Identity org_id; CLI fixes), each with its seam test.
4. L00 then wave 6 on the Windows lab (ask before each machine change); waves 8-10; campaign review; L47; one PR with `security-review` and the user's yes (D-ML131).

## Cleanup (ops; `git worktree remove` only, never prune)

Merged packet worktrees (ml-l21g, l08b, l30, l34, l36, l19n, l39, l27d, l29b, l54b, l27e, l35m, l37, **ml-l35m-fix, ml-l37-live**), `ml-l19n-red`, `nr1-nodes-ef-retry`, R7-R11 worktrees, about 90 local `ml/*` branches. Reports under each worktree are gitignored and local only.

## Watch-outs

- S-L6 was predicted red until L35mf (F143); the tip now has L35mf, but only run 3 can say it is green.
- `a9d36dd5` (L37f2) carries a `Co-Authored-By: Claude Haiku 5.5` trailer; consider it when squashing for the final campaign PR (D-ML131).
- `Dhadgar.Lab.Tests.Probe.SinkTests`: 2 failures seen once under full-suite load ("0.0.0.0 is an unspecified address"); 21/21 alone. Not ruled; re-run alone before reporting, and route a repeat to the architect.
- KG-W5-2, -19, -20 and -21 name owners that are now merged (L29b, L54b, L27e); their closure is not ruled yet.
- `AuthStackDockerTests` (KG-W7-6) rides the wave 7 Docker run (D-ML74); it can pass only on a tip with L35mf.
- Non-Docker filter: `RequiresDocker!=true&RequiresWindowsContainers!=true&RequiresNetwork!=true`. Never two full suites at once. Flake: `ModelHunterTests.Hunter_SameSeed_SameFirstOpList`.
- No `publish -r`, no `restore -r`, no `git worktree prune`. Never print the lab token or a lease id. Lab touches only `meridian-lab=1` containers.
- Vertical-slice (main checkout): #247 needs the user's yes when CI is green; its RESUME is stale.
