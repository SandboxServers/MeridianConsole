# Campaign ledgers

Each folder here is one campaign: a multi-PR effort run by `claude --agent orchestrator`. See [docs/agents/ai-sdlc.md](../agents/ai-sdlc.md) for the workflow and `.claude/skills/campaign-packet/SKILL.md` for the process.

| File | Content |
| --- | --- |
| `<campaign>/README.md` | Findings, decisions, packet status table |
| `<campaign>/work-packets.md` | The contract, then packets grouped into waves |
| `<campaign>/RESUME.md` | The 40-line resume card |
| `<campaign>/worknotes/` | Context-stop notes and ruling notes |
| `<campaign>/uat/` | Recorded manual-check runs, when the campaign has any (the evidence behind "confirmed working on Windows") |

## Campaigns

| Campaign | Ledger | Status | Closed |
| --- | --- | --- | --- |
| `sprint2-agent-nodes` | [README](sprint2-agent-nodes/README.md), [packets](sprint2-agent-nodes/work-packets.md), [UAT records](sprint2-agent-nodes/uat/) | Done when the campaign PR merges (D-S215) | 2026-10-27 |
