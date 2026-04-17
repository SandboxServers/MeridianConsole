## Azure Pipelines YAML Authoring

Azure Pipelines files in this repo:
- `azure-pipelines.yml` (repo root) — main CI/CD pipeline
- `azure-pipelines-containers.yml` (repo root) — container build pipeline
- `deploy/kubernetes/helm/helm-publish-pipeline.yml` — Helm chart publish pipeline

### Error checking
After editing any Azure Pipelines YAML file, always call `get_errors` on the modified file. The Azure Pipelines LSP is active on the three files above. Treat any LSP errors as blocking.

### YAML files that are NOT Azure Pipelines
Do NOT treat these as Azure Pipelines YAML — they use different schemas:
- `.github/workflows/*.yml` — GitHub Actions
- `deploy/compose/*.yml` and `deploy/pr-agent/docker-compose.yml` — Docker Compose / service configs
- `deploy/azure-pipelines/agent-swarm.yml` — Docker Compose (misleading folder name, not a pipeline)
- `deploy/kubernetes/helm/**/*.yaml` and `deploy/kubernetes/helm/**/*.yml` — Helm chart templates and values
- `.yamllint.yml`, `.hadolint.yaml`, `.coderabbit.yaml` — linter/tool configs
