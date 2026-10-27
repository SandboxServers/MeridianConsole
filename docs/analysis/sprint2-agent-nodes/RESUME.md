# RESUME: sprint2-agent-nodes

Ledger: [README.md](README.md). Packets and contract: [work-packets.md](work-packets.md).

## State (2026-10-27)

- Base `main` at `2d8e4d0` (main merged into w1 at `0f95bc4`). One local branch `campaign/sprint2-agent-nodes-w1` (worktree `.claude/worktrees/sprint2-agent-nodes-w1`), tip **`d759943`** (P39 and P40 merged; P15 on `s2/p15-closeout`, `c032c67`, in review). Merged this stretch: P30f, P31 (PSScriptAnalyzer), P32 to P38. **Draft PR #235** stays draft until P15 and the user's yes; ops pushes w1 after each merge.
- **UAT, all three modes pass:**
  - Process `20261027-182612-65dd` and Service `20261027-183248-3fd7`, both at `6530253`.
  - Msi `20261027-203859-0f1b` at `c0a641e`: overall pass, cleanup clean (18 undone), U1 and U9 pass, 0 secret findings, footprint empty, token masked in `msi-install.log` (P37 works).
- Process and Service count at `c0a641e` only if `src/` changes since `6530253` touch just `Installer/` and `Dhadgar.Agent.Windows.InstallerActions/` (P36-ruling § 3). Ops is checking that diff, and CI on #235 at `c0a641e`.
- Rulings D-S241 to D-S245 are integrated (P32 to P37). Owner actions: **O1** (WiX `WindowsBuild` rule, D-S242); **O2** (WiX `Hidden="yes"` secrets rule, D-S245, CLAUDE.md:80 and security-advisor.md:21).

## Next actions (orchestrator)

1. Finish the P15 review loop and merge `s2/p15-closeout` into w1.
2. Run campaign security pass 2 (`security-advisor`, scope campaign, range `main..<w1 tip>`; no UAT rerun, P39-ruling § 5).
3. Refresh the PR #235 description (final SHA, CI, UAT, P30f to P38).
4. At approval, list for the owner: D-S237 (3), D-S238, D-S239 R1-R8, N5, O1, O2, the P36/P37 residuals (token is an unzeroable managed string; table checks not in CI), and the P39 residuals (P39-ruling § 6: FU-P39-1, R-P39-1 to R-P39-4).
5. Mark ready and merge only on green CI and the user's explicit yes (`security-review` label present). Then remove packet worktrees; never prune.

## Gates

- Each UAT run needs `overall = pass`, `cleanup.status = clean`, `launchedBy = agent`, `agentRun.treeClean = true` and an empty footprint. Only the runner passes `-AcceptMachineChanges` or `-CleanupOnly`.
- Follow-ups for P15: D-S228 watch issue, Helm/ingress, retire the HTTP heartbeat endpoint, notifications outbox, WiX ICE61/ICE69, P23 advisories, P28f N3/N4, ~250 PSScriptAnalyzer warnings. (full list: PROJECT-STATE, 'Open after Sprint 2')

## Watch-outs

- Never run `msiexec` or the UAT wrapper outside the runner. Never run a bare `publish -r` (D-S241). Build with TreatWarningsAsErrors; run the `CI=true` solution build too. MakeSfxCA fails on paths of 260 characters or more.
- An elevated session is admin for every tool call (D-S239 R1): no coding or merging in it. PR #227 (meridian-lab plan) is not merged unless the user says so.
