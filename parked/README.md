# Parked projects

Projects in this folder are parked under decision D-03 of campaign `vertical-slice`. They are not in `Dhadgar.sln`, not in the Aspire AppHost, not routed through the Gateway, not in compose, and not in CI. Their code is kept as it was, with history followed by `git mv`. Parked code is not maintained, and it is not deleted.

## What is parked

| Item | Parked path | Original path | Kind |
| --- | --- | --- | --- |
| Billing | `parked/src/Dhadgar.Billing` | `src/Dhadgar.Billing` | Service |
| Billing tests | `parked/tests/Dhadgar.Billing.Tests` | `tests/Dhadgar.Billing.Tests` | Tests |
| Tasks | `parked/src/Dhadgar.Tasks` | `src/Dhadgar.Tasks` | Service |
| Tasks tests | `parked/tests/Dhadgar.Tasks.Tests` | `tests/Dhadgar.Tasks.Tests` | Tests |
| Files | `parked/src/Dhadgar.Files` | `src/Dhadgar.Files` | Service |
| Files tests | `parked/tests/Dhadgar.Files.Tests` | `tests/Dhadgar.Files.Tests` | Tests |
| Mods | `parked/src/Dhadgar.Mods` | `src/Dhadgar.Mods` | Service |
| Mods tests | `parked/tests/Dhadgar.Mods.Tests` | `tests/Dhadgar.Mods.Tests` | Tests |
| ShoppingCart | `parked/src/Dhadgar.ShoppingCart` | `src/Dhadgar.ShoppingCart` | Node.js site (no .NET assembly) |
| ShoppingCart tests | `parked/tests/Dhadgar.ShoppingCart.Tests` | `tests/Dhadgar.ShoppingCart.Tests` | Tests |
| Agent.Linux | `parked/src/Agents/Dhadgar.Agent.Linux` | `src/Agents/Dhadgar.Agent.Linux` | Linux agent |
| Agent.Linux tests | `parked/tests/Dhadgar.Agent.Linux.Tests` | `tests/Dhadgar.Agent.Linux.Tests` | Tests |
| Helm chart | `parked/deploy/kubernetes/helm` | `deploy/kubernetes/helm` | Kubernetes chart |
| Helm publish workflow | `parked/.github/workflows/helm-publish.yml` | `.github/workflows/helm-publish.yml` | GitHub workflow (outside `.github/workflows`, so GitHub does not run it) |

## Why

D-03 of campaign `vertical-slice` (owner decision, 2026-10-26): park Billing, Tasks, Files, Mods, Agent.Linux, ShoppingCart and the Helm chart under top-level `parked/`, out of `Dhadgar.sln` and CI, and keep their code.

## Where the rest is wired

- Solution: the twelve .NET projects were removed from `Dhadgar.sln` before they were moved.
- Aspire AppHost: the `billing`, `tasks` and `mods` resources and their references were removed from `src/Dhadgar.AppHost`.
- CI: the parked projects were removed from `.github/workflows`, `.github/dependabot.yml`, `azure-pipelines.yml` and `azure-pipelines-containers.yml`.
- Gateway routes, compose services and the Discord map are handled by VS-02 of the same campaign.

## How to unpark a project

1. Move it back to its original path with `git mv parked/<original path> <original path>`. Do this for the project and its test project.
2. Fix the `ProjectReference` paths in the moved `.csproj` files. References to shared projects (`..\Shared\...`, `..\..\Shared\...`, `..\Dhadgar.Agent.Core\...`, `..\Dhadgar.ServiceDefaults.Tests\...`) gain two `..\` segments under `parked/`, so reverse that change. References between parked projects (for example a test project to its service) are unchanged.
3. Add the projects back to the solution with `dotnet sln Dhadgar.sln add <csproj>`.
4. For a service, restore its resource and references in `src/Dhadgar.AppHost/Program.cs`, its `ProjectReference` in `src/Dhadgar.AppHost/Dhadgar.AppHost.csproj`, and its health test in `tests/Dhadgar.AppHost.Tests/AppHostIntegrationTests.cs`.
5. Restore the Gateway routes, compose entries and Discord map entries removed by VS-02.
6. Restore the CI entries listed under "Where the rest is wired", including the `helm-publish.yml` workflow and the Helm chart path.
7. Run `dotnet restore Dhadgar.sln` and `dotnet build Dhadgar.sln -c Release`, then update the lock files that change.
