# Dhadgar.Seams.Tests

Seam tests check that two services, or a service and an agent, agree on the wire. Each test runs the
real hosts in one process and drives one real call across the boundary. A seam that disagrees fails here,
not in production.

## What a seam test is

- The Gateway (`gateway::Program`) and each backend it routes to (`nodes::Program`, and later others) are
  real `WebApplicationFactory` hosts, started in-process with `TestServer`.
- The Gateway's YARP outbound client is replaced by `SeamForwarderFactory`, which hands each request to the
  in-process handler of the cluster it targets. Requests therefore really pass through routing, auth and
  transforms on both sides.
- Agents use the real `EnrollmentService` and `ControlPlaneClient` from `Dhadgar.Agent.Core`. Their only
  route to the control plane is `SeamHost.GatewayHandler()`.
- The seam test project is the only place that wires these together. Production hosts never reference it.

## The extern-alias pattern

Gateway and Nodes both define a top-level `Program`, so they cannot both be referenced by their plain names.
The project references each with an alias:

```xml
<ProjectReference Include="..\..\src\Dhadgar.Gateway\Dhadgar.Gateway.csproj" Aliases="gateway" />
<ProjectReference Include="..\..\src\Dhadgar.Nodes\Dhadgar.Nodes.csproj" Aliases="nodes" />
```

Every file that names a type from an aliased assembly starts with the matching declaration, before any `using`:

```csharp
extern alias nodes;

using nodes::Dhadgar.Nodes.Data;
```

Factories use `WebApplicationFactory<nodes::Program>` or `WebApplicationFactory<gateway::Program>`.
Files that only use `Dhadgar.Agent.Core` or the harness do not need an alias.

## How to add a backend

1. Add a project reference with an alias (`Aliases="<service>"`) and the `extern alias` line in each file that needs it.
2. Create `Infrastructure/<Service>SeamFactory.cs` as a `WebApplicationFactory<<service>::Program>`. Follow
   `NodesSeamFactory`: environment `Testing`, dummy broker settings, EF InMemory with its own internal
   service provider, MassTransit removed and replaced by `CapturingPublishEndpoint`, hosted services removed.
   Add `SeamJwt.Apply(services)` when the backend validates JWTs.
3. Add the backend to `SeamHost`'s `Gateway` dictionary: `["<cluster-id>"] = () => <Service>.Server.CreateHandler()`.
   The cluster id must match the `ClusterId` in the Gateway's `ReverseProxy` configuration. The Gateway factory
   points that cluster at `http://<cluster-id>.seams/`.
4. Write seam tests under a folder named for the seam.

## Client-certificate stand-in

`TestServer` cannot perform a TLS handshake, so no client certificate reaches the Gateway. Instead, the
agent's `ClientCertHeaderHandler` sends the certificate as base64 DER in `X-Seam-Client-Cert`, and
`SeamClientCertificateStartupFilter` moves it onto `Connection.ClientCertificate` before any other middleware
runs and removes the header. This is a stand-in for the handshake. It exercises the Gateway's certificate
handling but not the TLS layer itself. Certificate-validation tests belong in unit tests.

## Pending seams

A seam that cannot pass yet is marked with a skip that names the packet that will remove it:

```csharp
[Fact(Skip = "Pending VS-NN: <reason>")]
```

When a packet adds the missing behaviour, it removes the skip and runs the test. Before the skip is removed
in the packet that writes the seam, the test is run once with the skip removed and the failure is recorded.
This shows the test is red for the right reason.

## Running

```bash
dotnet test tests/Dhadgar.Seams.Tests/Dhadgar.Seams.Tests.csproj -c Release
```

The expected result at VS-05 is one passed and three skipped.
