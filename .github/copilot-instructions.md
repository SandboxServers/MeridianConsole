## Azure Pipelines YAML Authoring

CI/CD runs on GitHub Actions (`.github/workflows/`). The Azure Pipelines files below are manual-only and kept for reference until the GitHub workflows have run on main:
- `azure-pipelines.yml` (repo root) — former main CI/CD pipeline
- `azure-pipelines-containers.yml` (repo root) — former container build pipeline
- `parked/deploy/kubernetes/helm/helm-publish-pipeline.yml` — Helm chart publish pipeline

### Error checking
After editing any Azure Pipelines YAML file, always call `get_errors` on the modified file. The Azure Pipelines LSP is active on the three files above. Treat any LSP errors as blocking.

### YAML files that are NOT Azure Pipelines
Do NOT treat these as Azure Pipelines YAML — they use different schemas:
- `.github/workflows/*.yml` — GitHub Actions
- `deploy/compose/*.yml` and `deploy/pr-agent/docker-compose.yml` — Docker Compose / service configs
- `deploy/azure-pipelines/agent-swarm.yml` — Docker Compose (misleading folder name, not a pipeline)
- `parked/deploy/kubernetes/helm/**/*.yaml` and `parked/deploy/kubernetes/helm/**/*.yml` — Helm chart templates and values
- `.yamllint.yml`, `.hadolint.yaml`, `.coderabbit.yaml` — linter/tool configs
