# L09 ruling 1: RabbitMQ port for services behind Toxiproxy

Architect ruling, 2026-10-27. Trigger: the L09 Docker check fails with `[Lab.Timeout] nodes did not answer 200 within 90 s` (worknote `worknotes/L09.md` in `ml-l09`).

## Verified facts

| # | Fact | Evidence |
| --- | --- | --- |
| 1 | Nodes cannot be given a broker port. `RabbitMqOptions` has `Host`, `Username`, `Password`, `VirtualHost` only; `Program.cs:128` calls `cfg.Host(host, virtualHost, h => ...)`. | `src/Dhadgar.Nodes/RabbitMqOptions.cs`, `src/Dhadgar.Nodes/Program.cs:125-135` |
| 2 | That overload always uses the AMQP default port. Bus address for `Host("broker.test", "/")` is `rabbitmq://broker.test/...` (port -1, so 5672). `Host("broker.test:20021", "/")` throws `ConfigurationException` at bus creation, so `host:port` in `RabbitMq__Host` is no workaround. | Scratch app on MassTransit.RabbitMQ 8.5.12 |
| 3 | The overload `Host(string host, ushort port, string virtualHost, Action<IRabbitMqHostConfigurator>)` exists and gives `rabbitmq://broker.test:20021/<vhost>/...`. | Package XML docs and the scratch app |
| 4 | The shared library has the same gap: `AddDhadgarMessaging` reads `RabbitMq:Host` only and calls `cfg.Host(host, h => ...)`. Identity, Notifications and Discord use it. An absolute URI (`rabbitmq://h:20032/`) in `RabbitMq:Host` does carry a port through that overload, but Nodes cannot take a URI (fact 2). | `src/Shared/Dhadgar.Messaging/MessagingExtensions.cs:77-96`; scratch app |
| 5 | The docs already promise `RabbitMq:Port` / `RabbitMq__Port`. The code ignores it. | `docs/CONFIGURATION-MANAGEMENT.md:211-222` |
| 6 | The lab probes `/healthz`, which runs every health check, including Nodes' `masstransit` check. With no listener on `meridian-lab-toxiproxy:5672`, the bus never connects, so `/healthz` cannot return 200. That is enough to explain the timeout. It is not proven to be the only cause (no container logs were kept). | `NodesAdapter.HealthPath`; `ServiceDefaultsExtensions.cs:492-496` (`Predicate = _ => true`); `Program.cs:104-109` |
| 7 | Three RabbitMQ proxies share one Toxiproxy container: `nodes-rabbitmq` 20021, `identity-rabbitmq` 20032, `notifications-rabbitmq` 20051. The C10 impact map attributes faults per proxy. | `work-packets.md` C10 proxy table |
| 8 | The L09 spec (item 3) and the L34 spec (item 2) both assume a `RabbitMq__Port` key exists. It never did. This is a planning defect. | `work-packets.md` L09, L34 |
| 9 | `LabIsolation_ProductFilesDoNotNameLab` scans product files for lab names. A generic `Port` setting names nothing in the lab. | `tests/Dhadgar.Lab.Tests/LabIsolationTests.cs:48` |

## Decision

**Add a generic, default-preserving `RabbitMq:Port` setting to Nodes and to `Dhadgar.Messaging` in a new fix packet, L09f. It ships as its own PR before L09 part 7.** The lab then sets `RabbitMq__Port` per proxy, as the ledger already assumes.

Rejected:

- **Toxiproxy listens on 5672, or a network alias.** One container has one 5672. Three per-service RabbitMQ proxies (fact 7) cannot all listen on it, and an alias resolves to the same container IP and port space. Using one shared proxy would break per-service fault attribution (C10 impact map, D-ML34). One Toxiproxy per service would rework C10, `FaultBook` and I5.
- **Point Nodes straight at `meridian-lab-rabbitmq:5672`.** This breaks "Nodes reaches RabbitMQ only through Toxiproxy" (D-ML6, `ServiceAdapterTests`) and kills the `nodes-rabbit-down-outbox` fault row.
- **URI in `RabbitMq__Host` for the shared library, with Nodes changed only.** Two config shapes for one concern, and it would leave fact 5's docs half true.

This is **not** a D-ML40 lab seam, so it is not owner-gated. It names no lab type, host or port, and defaults to today's behaviour (5672). It makes a documented product setting real, and any deployment with a non-default broker port needs it. Record it as **D-ML62** with finding **F52** (facts 1, 4, 5).

## L09f. Configurable RabbitMQ port (Nodes and `Dhadgar.Messaging`)

- **Branch / worktree / commit:** `ml/l09f-rabbitmq-port` / `ml-l09f` (from `main`) / `fix(messaging): honour RabbitMq:Port in Nodes and AddDhadgarMessaging`
- **Depends on:** none. **Blocks:** L09 part 7, L34, L36.
- **Security review:** no. This is broker connection config. It is not an agent project, `IControlPlaneClient`, `CommandEnvelope`, a process manager, enrollment, certificates or auth. `packet-reviewer` only.
- **Rules (inline):** no package versions in `.csproj` and no new package references (Nodes.Tests already gets MassTransit transitively); nullable on, warnings as errors; config validation fails fast at startup the way the existing code does (`ValidateDataAnnotations().ValidateOnStart()` in Nodes, `InvalidOperationException` naming the key at bus creation in Messaging). This is the accepted pattern for startup config, not a `Result<T>` path. `await using` on every `ServiceProvider` in tests (MassTransit services are `IAsyncDisposable` only). xUnit `[Fact]`/`[Theory]` as in the target test projects. No lab names anywhere in product files (D-ML2, `LabIsolationTests`).

**Files**

1. `src/Dhadgar.Nodes/RabbitMqOptions.cs`: add

   ```csharp
   [Range(1, 65535)]
   public int Port { get; set; } = 5672;
   ```

2. `src/Dhadgar.Nodes/RabbitMqHostConfiguration.cs` (new, `public static class RabbitMqHostConfiguration`). This is the one place Nodes sets the host, so a test can cover what `Program.cs` runs:

   ```csharp
   public static void Configure(IRabbitMqBusFactoryConfigurator cfg, RabbitMqOptions options)
   {
       ArgumentNullException.ThrowIfNull(cfg);
       ArgumentNullException.ThrowIfNull(options);
       cfg.Host(options.Host, (ushort)options.Port, options.VirtualHost, h =>
       {
           h.Username(options.Username);
           h.Password(options.Password);
       });
   }
   ```

3. `src/Dhadgar.Nodes/Program.cs:127-132`: replace the inline `cfg.Host(...)` with `RabbitMqHostConfiguration.Configure(cfg, rabbitOptions);`. Change nothing else.
4. `src/Shared/Dhadgar.Messaging/MessagingExtensions.cs`, inside `UsingRabbitMq`, after the credential checks:

   ```csharp
   ushort? port = null;
   var portValue = config["RabbitMq:Port"];
   if (!string.IsNullOrWhiteSpace(portValue))
   {
       if (!ushort.TryParse(portValue, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed == 0)
       {
           throw new InvalidOperationException("RabbitMq:Port must be an integer from 1 to 65535.");
       }
       if (Uri.IsWellFormedUriString(host, UriKind.Absolute))
       {
           throw new InvalidOperationException("RabbitMq:Port cannot be combined with a URI in RabbitMq:Host; put the port in the URI.");
       }
       port = parsed;
   }

   void ConfigureHost(IRabbitMqHostConfigurator h) { /* the existing body: credentials, heartbeat, timeout, channel max, confirms */ }

   if (port is { } p) { cfg.Host(host, p, "/", ConfigureHost); } else { cfg.Host(host, ConfigureHost); }
   ```

   Keep vhost `/` (today's behaviour). Do not add a vhost key here. L34 must not rely on one (see the ledger changes below).
5. Tests:
   - `tests/Dhadgar.Nodes.Tests/RabbitMqHostConfigurationTests.cs` (new). Build a `ServiceCollection` with `AddMassTransit(x => x.UsingRabbitMq((ctx, cfg) => RabbitMqHostConfiguration.Configure(cfg, options)))`, then `await using` the provider and resolve `IBusControl` (created, never started, so no broker is contacted). `Configure_UsesConfiguredPort`: options `Host="broker.test"`, `Port=20021`, `VirtualHost="/"` → `bus.Address.Host == "broker.test"` and `bus.Address.Port == 20021`. `Configure_DefaultPort_Is5672`: default options → `bus.Address.Port` is -1 (MassTransit leaves out the default port). **Fails when reverted:** with the 3-argument `Host` the port is -1, not 20021.
   - `tests/Dhadgar.Nodes.Tests/RabbitMqOptionsValidationTests.cs` (extend): `RabbitMq:Port` binds (`"20021"` → 20021); missing → 5672; `0` and `70000` fail `ValidateDataAnnotations`. **Fails when reverted:** no `Port` property, so it does not bind or compile.
   - `tests/Dhadgar.Messaging.Tests/MessagingExtensionsHostPortTests.cs` (new, same provider pattern as `MessagingExtensionsCredentialValidationTests`): `RabbitMq:Port=20021` → `bus.Address.Port == 20021`; no port → -1; `"0"`, `"abc"`, `"70000"` → `InvalidOperationException` containing `RabbitMq:Port`; `RabbitMq:Host="rabbitmq://broker.test:5673/"` plus `RabbitMq:Port=20021` → `InvalidOperationException` containing `RabbitMq:Port`. **Fails when reverted:** the port is ignored, so the address port is -1.

**Checks (in order; `<W>` = the `ml-l09f` worktree)**

```powershell
dotnet build <W>\src\Shared\Dhadgar.Messaging\Dhadgar.Messaging.csproj -c Release
dotnet build <W>\src\Dhadgar.Nodes\Dhadgar.Nodes.csproj -c Release
dotnet build <W>\src\Dhadgar.Identity\Dhadgar.Identity.csproj -c Release
dotnet build <W>\src\Dhadgar.Notifications\Dhadgar.Notifications.csproj -c Release
dotnet build <W>\src\Dhadgar.Discord\Dhadgar.Discord.csproj -c Release
dotnet test <W>\tests\Dhadgar.Messaging.Tests\Dhadgar.Messaging.Tests.csproj -c Release
dotnet test <W>\tests\Dhadgar.Nodes.Tests\Dhadgar.Nodes.Tests.csproj -c Release
dotnet test <W>\tests\Dhadgar.Lab.Tests\Dhadgar.Lab.Tests.csproj -c Release --filter "FullyQualifiedName~LabIsolation"
git -C <W> status --porcelain   # no packages.lock.json change
```

**Docs owed:** `docs/libraries/messaging.md`, configuration section: add `RabbitMq:Port` (optional, default 5672, cannot be combined with a URI host). `src/Dhadgar.Nodes/README.md`: add `RabbitMq:Port` wherever it lists RabbitMQ settings. `docs/CONFIGURATION-MANAGEMENT.md` is already correct, so leave it. Check `docs/meta/doc-update-map.md` for any other entry.

## What changes in L09 (part 7, `ml-l09`, after L09f merges and L09 rebases onto `main`)

These files are already in L09's list. No product file changes in L09.

1. `src/Tools/Dhadgar.Lab/Services/NodesAdapter.cs`: add `["RabbitMq__Port"] = NodesRabbitMqProxyPort.ToString(CultureInfo.InvariantCulture)`.
2. `tests/Dhadgar.Lab.Tests/Services/ServiceAdapterTests.cs`: `Nodes_ReachesRabbitMqOnlyThroughProxy` asserts `RabbitMq__Host == "meridian-lab-toxiproxy"` and `RabbitMq__Port == "20021"`. **Fails when reverted:** with no port key Nodes dials 5672 and bypasses the proxy.
3. `src/Tools/Dhadgar.Lab/Services/LabServices.cs`, readiness timeout: the `[Lab.Timeout]` message adds the last HTTP status code. If the body parses as the health JSON, it also adds the names of the non-`Healthy` entries (names only, never descriptions, exceptions or env: R-LOG). The next failure is then diagnosable without keeping containers.
4. Drop the worknote deviation "Nodes `RabbitMq__Port=20021` not set".
5. Acceptance: `ServicesDockerTests` green (Nodes and Gateway ready; `ca-certificate` 200 on 15000 and 25000). This is the end-to-end proof that Nodes reaches RabbitMQ through `nodes-rabbitmq`. If it still times out, the improved message names the failing check. The coder reports it as `blocked` with that output and does not widen the file list.

## Ledger changes (orchestrator applies in the campaign worktree)

- README: add F52 (facts 1, 4, 5) and D-ML62 (this decision). Add packet row `L09f | 2 | Configurable RabbitMQ port (Nodes, Messaging) | - | no | Ready`. L09's dependencies gain L09f.
- work-packets: add the L09f block above. In L09 item 3, replace "`RabbitMq__Port=20021` (or the key Nodes reads)" with "`RabbitMq__Port=20021` (key added by L09f)".
- L34 item 2: keep `RabbitMq__Port=20032` (real after L09f). Change "the vhost" to "vhost `/` (`AddDhadgarMessaging` has no vhost key)".
- L36 item 2: Notifications gets `RabbitMq__Host=meridian-lab-toxiproxy` and `RabbitMq__Port=20051`. **Gap:** Discord calls `AddDhadgarMessaging` but C10 lists no `discord-rabbitmq` proxy. Add `discord-rabbitmq | 20062 | meridian-lab-rabbitmq:5672 | L36` to the C10 proxy table and Discord's dependency cell, and set `RabbitMq__Port=20062` in `DiscordLabAdapter`.
- Seams: S-L2 (L11, Docker variant through the lab Gateway) and S-L7 (L19, Nodes `/readyz`) need Nodes ready in the lab. Both depend on L09, and so now on L09f. Their specs do not change.

## Status

No BlockedDecision. L09 stays `blocked` until L09f merges. Then a fresh `packet-coder` continues L09 part 7 from this note and `worknotes/L09.md`.
