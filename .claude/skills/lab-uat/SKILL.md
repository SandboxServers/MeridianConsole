---
name: lab-uat
description: 'Run a MeridianConsole UAT spec against the meridian-lab stack through the uat-driver agent, then check the driver''s claims against the evidence bundle. Use when asked to run UAT rows, a UAT batch or a lab self-test, or when a lab run needs its report read. Triggers include "run the UAT", "run lab-uat", "UAT batch", "read the UAT report", "why did the UAT fail", "lab lease held".'
---

# Run a lab UAT

The lab runs UAT specs against the real stack. You plan the run, take the lease, hand the exact steps to the `uat-driver` agent, then read the evidence yourself. The driver runs steps and reports them raw; it never diagnoses, edits or attests.

## 1. When to use

- A campaign packet or the user asks for UAT rows to be run or re-run.
- A UAT report or bundle must be read and its claims checked.
- A lab run failed and the cause must be found from evidence.

Do not use this skill to change code. A failed row is reported with its evidence; the fix is a separate packet.

## 2. Ask before the lease

Before you take the lease, ask the user with AskUserQuestion unless they already asked for the run. Confirm the spec, the rows and the lease owner name.

Nothing in the lab changes the workstation: Windows rows run in pool members from wave 6. So no approval step exists beyond this question.

## 3. Plan first

Run `uat_plan` before anything else. It returns the plan for the spec. Do not take the lease until the plan returns without error.

```text
uat_plan  { "spec": "<spec id>" }
```

## 4. Dispatch the driver

Take the lease yourself with `lab_lease_acquire` and pass the lease owner name to the driver. Then dispatch `uat-driver` with an exact step list. The brief names each step, the arguments it takes, and the lease owner. The driver must not choose steps.

Work in batches of about 5 rows. A batch that fails stops the run; do not start the next batch on a degraded stack.

Always release the lease when the batch ends, whether it passed or failed: `lab_lease_release`. Never use a force option on a lease.

## 5. Read the result

After each batch, read the results yourself:

1. `uat_report` for the run. Take `overall`, `seed` and the bundle path from it.
2. `bundle_read` on the bundle's `ledger.md`.
3. Check every claim the driver made against `ledger.md`. A claim the ledger does not show is a finding, not a pass.

Human clauses (anything that needs someone to look at the screen) are closed only by `uat_attest`, and only after the user says what they saw. Never attest a clause on the driver's word. The verdict is `held` or `not_held`; `pass` and `fail` are refused. The user approves each `uat_attest` call in the harness (O2), so the call is shown with its exact arguments before it runs.

When the ledger is evidence for a status change, copy `ledger.md` into the campaign `worknotes/` folder under the packet's name.

## 6. Troubleshooting

| Symptom | First check | Next step |
|---|---|---|
| Lease refused | `lab_lease_status` | Ask the user who holds it. Do not take it over. |
| Stack degraded | `stack_status` | Read `lab_doctor`. Report it. Do not restart services to hide it. |
| Step error after one retry | The driver's raw line | Report the raw error with the row id. |
| Bundle missing | `uat_report` | Report the run id; do not rebuild the bundle. |

Later packets extend this skill: L24 adds faults and invariants, L26 adds results and triage, L38 adds the pool, UI and CLI tools.
