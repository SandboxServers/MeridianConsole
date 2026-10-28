# L17 ruling 1: how a lease is presented on a Leased tool call

Architect ruling, 2026-10-28. Trigger: the L17 S-L6 test (`tests/Dhadgar.Lab.Tests/Uat/LabSelfTestE2ETests.cs`, uncommitted in `ml-l17`) calls `lab_build` over MCP with `lease_id` and gets `[Lab.InvalidArgument] arguments do not match the tool's schema` (`worknotes/L17.md` line 19 in `ml-l17`).

## Ruling

**`lease_id` is a reserved envelope argument for Leased tools. The caller sends it in the tool arguments; the dispatcher reads it at step 3, checks and touches the lease, then removes it before the tool binds its arguments. The MCP projection advertises it in every Leased tool's schema as a required string.** Leased argument records never declare `LeaseId`; a Leased tool reads `LabCallContext.LeaseId`. OwnLease tools are unchanged: they declare `lease_id?` in their own argument record (C4 lists it per row) and get it unchanged.

It is not a header: MCP has no per-call header, and C4 already says "every Leased tool takes a required `lease_id` string". It is not a per-record field either: that would put the same field into 13 merged records and about 30 planned ones, and each tool would then need to ignore it correctly.

The fix goes into **new fix packet L05f**, not L17. The defect is in L05's dispatcher and projection, which belong to no L17 file, and the same defect hits the runner (L15) and the observation stream (L48). L17 resumes after L05f merges.

## Facts

1. C3 step 3: "Leased: args.lease_id must equal the current lease … then Touch". C4: "Every Leased tool takes a required `lease_id` string; it is not repeated below". The OwnLease rows list `lease_id?` as an argument. L05 item 4 told the projection to pass `leaseIdFromArgs`. No section says who removes the field before binding. **That gap is in the contract I wrote. It is not a coder error.**
2. `LabTool<TArgs>.InvokeAsync` (`Tools/LabTool.cs:66-82`) deserialises the raw arguments with `LabJson.Options`, which sets `UnmappedMemberHandling.Disallow` (`Tools/LabJson.cs:39`). Any `lease_id` the record does not declare becomes `[Lab.InvalidArgument]`.
3. `LabToolDispatcher.InvokeAsync` (`Tools/LabToolDispatcher.cs:95-118`) reads `lease_id` from the arguments and passes `arguments` unchanged to `RunToolAsync` (line 142, then line 176).
4. No merged Leased argument record declares `LeaseId`. The only records that do are `LeaseRenewArgs` and `LeaseReleaseArgs` (Open) and `UatRunArgs` (OwnLease, `Tools/Uat/UatRunTool.cs:31`).
5. Nothing tested this path before. The dispatcher tests use `StubTool : ILabTool`, which ignores the arguments (`LabToolDispatcherTests.cs:222-245`). `UatRunnerTests` uses an empty registry (`UatRunnerTests.cs:188`). S-L1 calls only `lab_status`.
6. `LabJson.SchemaFor<T>` exports `additionalProperties: false` with no `lease_id`. An MCP client that follows the schema, Claude included, cannot know it must send `lease_id`, and a strict client would refuse to send it.
7. **Second defect: the lease id leaks.** `UatRunner` puts `lease_id` into the args of Leased and OwnLease steps (`Uat/UatRunner.cs:353-356`), then calls `BeginStepObservation(run.RunId, tool, args)` with those injected args (line 359). `LabObservationSink` redacts only vault values, and the lease id is not in the vault (`Leasing/LeaseBook.cs:371-377`). `events.jsonl` therefore holds the live lease id, and `events_read` and `bundle_read` are **Open** (`Policy/ToolPolicy.cs:25,85`). Any session can read the lease from another session's run and act under it. That breaks D-ML4 ("returned only to the acquirer").

## Affected tools

All 13 registered Leased tools fail today on every path that sends `lease_id`, which is MCP and the UAT runner (the CLI goes through MCP):

| Area file | Leased tools |
| --- | --- |
| `Mcp/Areas/ServicesArea.cs` (L09) | `lab_build`, `svc_start`, `svc_stop`, `svc_restart`, `svc_config_set` |
| `Mcp/Areas/StackArea.cs` (L08, L11) | `stack_up`, `stack_down`, `stack_reset_data` |
| `Mcp/Areas/NodesArea.cs` (L11) | `nodes_token_mint`, `nodes_enroll_probe`, `nodes_cert_time_shift` |
| `Mcp/Areas/ProbesArea.cs` (L10) | `http_request` |
| `Mcp/Areas/UatArea.cs` (L16) | `uat_attest` |

Not affected: the Open tools, including the L07 `logs_tail` and L48 `events_read`, and `uat_run` (OwnLease, which declares `LeaseId`). L48 is affected only through the leak in fact 7. Every UAT row with a Leased step fails today with `Lab.InvalidArgument`, so L15's and L16's runner paths have never run a real Leased step.

## L05f. Lease id as a gate envelope field

- **Branch / worktree / commit:** `ml/l05f-lease-envelope` / `ml-l05f` (from `campaign/meridian-lab` at its tip) / `fix(lab): lease_id is a dispatcher envelope field for Leased tools [L05f]`
- **Depends on:** none (L05, L15 and L48 are merged). **Blocks:** L17 (S-L6), L18, and every later packet that adds a Leased tool. **Security review:** yes. It changes the policy gate, and it closes a lease-id disclosure.
- **Merge:** locally into `campaign/meridian-lab` (D-ML16), before L17 resumes.

**Rules (inline):**

- R-RESULT: no new throws. A malformed argument object stays a `[Lab.InvalidArgument]` from `LabTool<TArgs>`, and the dispatcher's existing refusals keep their codes.
- R-NULL: nullable is enabled, and CI builds with `CI=true` (warnings as errors, `AnalysisMode AllEnabledByDefault`).
- R-USING: `using` on `JsonDocument` and `Utf8JsonWriter`.
- R-SECRET: the lease id is a credential. It never goes into an observation, an audit line, a log or an exception message.
- No package versions in `.csproj` (Central Package Management).
- xUnit: `[Fact]` and `Assert.*` in the style of the target file. Daemon-based tests use `[Collection("LabHome")]`. Nothing here needs Docker.

**Contract (C3 addition, applied to `work-packets.md` by this ruling):** `lease_id` is a reserved envelope argument. For a Leased tool, the dispatcher reads it at step 3, removes every top-level `lease_id` property before step 5, and passes the validated id as `LabCallContext.LeaseId`. Leased argument records never declare `LeaseId`. The MCP projection adds `lease_id: {type: string}` to the properties and the `required` list of every Leased tool's advertised schema. OwnLease argument records declare `string? LeaseId = null`, and the dispatcher passes their arguments unchanged.

**Files**

1. `src/Tools/Dhadgar.Lab/Tools/LabToolDispatcher.cs`
   - Make the field name public and shared: `public const string LeaseIdArgument = "lease_id";`. Delete the private `LeaseIdField` and use the new constant here, in `Mcp/LabMcpServer.cs` (delete its `LeaseIdField`) and in `Uat/UatRunner.cs` (delete its `LeaseIdField`).
   - In `InvokeAsync`, after the lease block (after line 118) and before the audit begin, add:

     ```csharp
     // Leased tools take lease_id as an envelope field (C3): it is consumed here and never reaches the tool's binder.
     JsonElement toolArguments = gate == LabGate.Leased ? WithoutLeaseId(arguments) : arguments;
     ```

     Then pass `toolArguments`, not `arguments`, to `RunToolAsync`.
   - Add the helper. It writes the object again without the field, drops every occurrence when keys repeat, and never throws on duplicate keys, unlike `JsonNode.Parse`:

     ```csharp
     private static JsonElement WithoutLeaseId(JsonElement arguments)
     {
         if (arguments.ValueKind != JsonValueKind.Object)
         {
             return arguments;
         }

         var buffer = new ArrayBufferWriter<byte>();
         using (var writer = new Utf8JsonWriter(buffer))
         {
             writer.WriteStartObject();
             foreach (JsonProperty property in arguments.EnumerateObject())
             {
                 if (!string.Equals(property.Name, LeaseIdArgument, StringComparison.Ordinal))
                 {
                     property.WriteTo(writer);
                 }
             }

             writer.WriteEndObject();
         }

         using JsonDocument document = JsonDocument.Parse(buffer.WrittenMemory);
         return document.RootElement.Clone();
     }
     ```

     (`using System.Buffers;`.)
2. `src/Tools/Dhadgar.Lab/Mcp/LabMcpServer.cs`
   - Add `internal static JsonElement AdvertisedSchema(JsonElement schema, LabGate? gate)`. For any gate except `Leased`, it returns `schema` unchanged. For `Leased`, it parses the schema to a `JsonObject` and creates `properties` when it is missing. It sets `properties["lease_id"] = { "type": "string", "description": "The lease_id from lab_lease_acquire." }`, appends `"lease_id"` to `required` (creating the array when missing, never adding it twice), and returns `JsonSerializer.SerializeToElement(root, LabJson.Options)`. Leave `additionalProperties` as it is: the property is now declared.
   - In `LabMcpTool`'s constructor, set `InputSchema = AdvertisedSchema(tool.InputSchema, ToolPolicy.GateFor(tool.Name))`. Do not change `ILabTool.InputSchema`. `SpecValidator` keeps validating against the raw schema and keeps refusing `lease_id` in specs (C7).
3. `src/Tools/Dhadgar.Lab/Uat/UatRunner.cs`: record the observation with the arguments **before** the lease is injected. At line 359, change `BeginStepObservation(run.RunId, tool, args)` to `BeginStepObservation(run.RunId, tool, expanded.Value)`. `args`, which carries `WithLease`, goes only to `_dispatcher.InvokeAsync`. The observation must never see the injected args.
4. `src/Tools/Dhadgar.Lab/README.md`: in the gate section near line 91, add one paragraph: "`lease_id` is an envelope field. Leased tools list it as required in their MCP schema; the dispatcher checks it, passes it to the tool in the call context and strips it from the arguments. It never appears in a spec, an observation, an audit line or a bundle."

**Tests (each fails when its change is reverted):**

1. `tests/Dhadgar.Lab.Tests/Tools/LabToolDispatcherTests.cs`: add a nested `private sealed record EchoArgs(string Value);` and `private sealed class EchoTool : LabTool<EchoArgs>` (name taken from the constructor), returning `Result<object>.Success(new { value = args.Value, has_lease = context.LeaseId is not null })`.
   - `Leased_RealLabTool_StripsLeaseIdBeforeBinding`: gate `Leased`, acquire a lease, call `{"lease_id":"<id>","value":"v"}`. Assert not `IsError`, `/value == "v"`, `/has_lease == true`. Revert item 1 and this returns `Lab.InvalidArgument`.
   - `Leased_RealLabTool_DuplicateLeaseIdKeys_AreAllStripped`: raw JSON `{"lease_id":"<id>","lease_id":"<id>","value":"v"}` → not `IsError`.
   - `OwnLease_RealLabTool_KeepsLeaseIdForItsArgs`: `private sealed record OwnArgs(string? LeaseId = null);` with a tool returning `new { lease = args.LeaseId }`. Gate `OwnLease`, call with the id → `/lease` equals the id. This pins the rule that OwnLease is not stripped.
2. `tests/Dhadgar.Lab.Tests/Mcp/LabMcpSchemaTests.cs` (new): `AdvertisedSchema_Leased_AddsRequiredLeaseId` (from `LabJson.SchemaFor<EchoLikeArgs>()`, which has no `required`, and from a schema with `required: ["value"]`. Both end with `lease_id` in properties and required, exactly once). Also `AdvertisedSchema_OpenAndOwnLease_AreUnchanged` (raw text equal) and `AdvertisedSchema_IsIdempotent` (applied twice, `lease_id` appears once in `required`).
3. `tests/Dhadgar.Lab.Tests/Daemon/LabDaemonServiceLifetimeTests.cs`: add `LeaseIdArgument_FollowsTheGate` (R-LEASE). Start the daemon as the existing fact does. For every `ILabTool` with gate `Leased`, assert that the raw `InputSchema` has no `properties.lease_id`. For every `ILabTool` with gate `OwnLease`, assert that `properties.lease_id` exists and is not in `required`. For every `McpServerTool` whose name is Leased, assert that `ProtocolTool.InputSchema.required` contains `lease_id`. Later OwnLease packets (L27 and on) are held to C4 automatically.
4. `tests/Dhadgar.Lab.Tests/Seams/McpEdgeSeamTests.cs` (S-L1, no Docker): add `Mcp_LeasedTool_TakesLeaseIdAsEnvelope`. Through a real `McpClient` against the in-process daemon:
   1. Assert that `ListToolsAsync` shows `lab_build` with `lease_id` in `JsonSchema.required`, and `uat_run` with `lease_id` in its properties but not in `required`.
   2. Call `lab_lease_acquire {owner:"s-l1", purpose:"seam"}` and read `lease_id` from the structured content.
   3. Call `lab_build {}` → error `Lab.LeaseRequired`.
   4. Call `lab_build {lease_id}` → error `Lab.InvalidArgument` with the message `checkout is required` (`BuildTool.cs:46`; it returns before any docker call).
   5. Release the lease in `finally`.

   Today step 4 returns the message `arguments do not match the tool's schema`. This is the seam test for the change.
5. `tests/Dhadgar.Lab.Tests/Uat/UatRunnerTests.cs`: extend `Harness` with optional `params ILabTool[] tools` and an optional `ILabObservationSink`. Add `RecordingSink : ILabObservationSink`, which keeps the drafts passed to `Append` and returns `Result<long>.Success(n)`, `Result.Success()` from `BeginRun` and a fixed `OpId` from `NextOp`. Check the real `OpId` constructor. Add `LeasedStep_BindsAndItsObservationNeverCarriesTheLeaseId`: a stub named `stack_up` (a real Leased name, because the runner and dispatcher use `ToolPolicy`) over `LabTool<StubArgs>` with `record StubArgs(bool? Wipe = null)`, and a one-row spec with step `{ tool = "stack_up", args = { wipe = true } }`. Assert that the step's dispatch succeeded, so the row is not graded on `Lab.InvalidArgument`. Assert that no recorded `LabObservation.Args` raw text contains `lease_id` or the run's lease id. Read the lease id with a stub that records `context.LeaseId`. Revert item 1 and the row fails on `Lab.InvalidArgument`; revert item 3 and the observation holds the id.

**Checks, in order** (Docker not needed):

```powershell
$env:CI = 'true'
dotnet build C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\ml-l05f\src\Tools\Dhadgar.Lab\Dhadgar.Lab.csproj -c Release
dotnet test C:\Users\Steve\source\projects\MeridianConsole\.claude\worktrees\ml-l05f\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "RequiresDocker!=true"
```

Every lab test must still pass. The non-Docker count was 522 passed, 4 skipped at L17's last run, so expect that plus the new facts.

**Docs owed:** item 4 (lab README). The C3 and C4 contract text is applied by this ruling.

## What changes in L17 (after L05f merges)

- Merge the `campaign/meridian-lab` tip into `ml/l17-cli-selftest`, then rerun `LabSelfTestE2ETests` (Docker up). No change to the test's `lease_id` usage: it is the correct way to call. Commit part 6 (S-L6 test and README CLI section) only when the checks pass.
- L17 changes nothing in the dispatcher, the projection or any argument record.
- This ruling does not change L17's open note that `nodes-log` is UNVERIFIED while `LogSource` has no ring. If S-L6 then fails only on that row, return `blocked` with the row's result and that note gets its own ruling.

## Ledger changes (applied in this commit)

- `README.md`: F56 (facts 1-7, short form); D-ML66 (this ruling); packet row `L05f | 3 | Lease id as a dispatcher envelope field; observation leak (D-ML66) | - | yes | Ready`; L17 depends on L05f; the wave 3 merge order gains L05f before L17.
- `work-packets.md`: the C3 envelope paragraph, a sentence in C4, the L05f block in short form (this note holds the full spec), and L05f added to L17's dependencies.

No BlockedDecision.
