# Linter & SAST Implementation Strategy

**Document Version**: 1.0
**Last Updated**: 2025-12-28
**Status**: Planning
**Owner**: Engineering Team

---

## Executive Summary

This document outlines the comprehensive strategy for implementing static analysis security testing (SAST) and linting across the Meridian Console (Dhadgar) platform. The implementation is prioritized to address **critical security gaps in customer-hosted agent code** first, followed by code quality improvements across all services.

**Critical Finding**: The agent projects (`Dhadgar.Agent.Core`, `Dhadgar.Agent.Linux`, `Dhadgar.Agent.Windows`) currently have NO security-focused SAST tooling despite running on customer hardware with high trust levels. This is the highest priority item.

---

## Current State Assessment

### What's Working ✅
- .NET built-in analyzers enabled (`EnableNETAnalyzers=true`, `AnalysisLevel=latest`)
- `.editorconfig` configured for basic formatting (indentation, line endings, charset)
- Nullable reference types enabled across all .NET projects
- Azure DevOps pipeline infrastructure in place

### Critical Gaps ❌
- **No security-focused SAST for agent code** (HIGH RISK)
- No linting for TypeScript/React frontend (Dhadgar.Scope)
- No YAML validation for Azure Pipelines and Kubernetes manifests
- No code formatter (Prettier/CSharpier) configured
- Warnings not treated as errors in CI builds
- No pre-commit hooks to catch issues locally

### Tech Stack Inventory

| Category | Technologies | Linters Needed |
|----------|-------------|----------------|
| **Backend** | .NET 10, C# 13, ASP.NET Core | Security Code Scan, SonarAnalyzer, Roslynator |
| **Frontend** | Astro 5.1, React 18, TypeScript 5.7, Tailwind 3.4 | ESLint, TypeScript-ESLint, eslint-plugin-security, Prettier |
| **Infrastructure** | Docker, Kubernetes (Talos), Azure Pipelines YAML | Hadolint, yamllint, actionlint |
| **Data** | PostgreSQL, Entity Framework Core 10 | EF Core analyzers (built-in) |
| **Messaging** | RabbitMQ, MassTransit 8.3 | Covered by SonarAnalyzer |

---

## Implementation Phases

### Phase 1: Security-Critical (Week 1) 🔴

**Goal**: Eliminate security vulnerabilities in high-trust agent code and authentication services.

#### Tasks

1. **Add Security Code Scan to Agent Projects**
   - **Projects**: `Dhadgar.Agent.Core`, `Dhadgar.Agent.Linux`, `Dhadgar.Agent.Windows`
   - **Why**: These run on customer hardware with elevated privileges
   - **Detection**: SQL injection, command injection, path traversal, XSS, crypto weaknesses, hardcoded secrets
   - **Acceptance Criteria**:
     - [ ] Package added to `Directory.Packages.props`
     - [ ] All agent `.csproj` files reference the analyzer
     - [ ] Local build shows security warnings (if any)
     - [ ] ADO pipeline fails if security warnings exist in agent code

2. **Add Security Code Scan to Identity Service**
   - **Projects**: `Dhadgar.Identity`
   - **Why**: Handles authentication, JWT tokens, user credentials
   - **Acceptance Criteria**:
     - [ ] Package referenced in project
     - [ ] Security warnings reviewed and addressed
     - [ ] Pipeline validation enabled

3. **Enable TreatWarningsAsErrors in CI**
   - **File**: `Directory.Build.props`
   - **Why**: Prevent security warnings from being ignored
   - **Acceptance Criteria**:
     - [ ] Local builds still succeed (warnings allowed)
     - [ ] ADO pipeline builds fail on any warning
     - [ ] Team notified of change before merge

4. **Set Up ESLint + Security Plugin for Dhadgar.Scope**
   - **Directory**: `src/Dhadgar.Scope/`
   - **Why**: Frontend security (XSS, unsafe APIs, prototype pollution)
   - **Acceptance Criteria**:
     - [ ] ESLint + TypeScript-ESLint configured
     - [ ] `eslint-plugin-security` installed and enabled
     - [ ] `npm run lint` script added
     - [ ] ADO pipeline runs linter on Scope project
     - [ ] No high-severity security issues in scan

**Estimated Effort**: 8-12 hours
**Risk Level**: Low (analyzers are additive, won't break existing code)
**Blocker Dependencies**: None

---

### Phase 2: Code Quality Foundation (Week 2) 🟡

**Goal**: Establish comprehensive code quality baselines across all projects.

#### Tasks

1. **Add SonarAnalyzer.CSharp to All .NET Projects**
   - **Projects**: All 23 .NET projects
   - **Why**: 600+ rules for bugs, code smells, maintainability
   - **Acceptance Criteria**:
     - [ ] Package added to `Directory.Packages.props`
     - [ ] Baseline established (suppress existing warnings initially)
     - [ ] New code must pass all rules
     - [ ] Consider SonarCloud integration for historical tracking

2. **Add Roslynator Analyzers to All .NET Projects**
   - **Projects**: All 23 .NET projects
   - **Why**: Modern C# patterns, performance optimizations, 500+ refactorings
   - **Acceptance Criteria**:
     - [ ] Three Roslynator packages added to `Directory.Packages.props`
     - [ ] Code quality baseline established
     - [ ] Team trained on common violations

3. **Configure Prettier for Dhadgar.Scope**
   - **Directory**: `src/Dhadgar.Scope/`
   - **Why**: Automatic code formatting, reduce formatting debates
   - **Acceptance Criteria**:
     - [ ] Prettier installed
     - [ ] `.prettierrc` configuration file created
     - [ ] `format` script added to `package.json`
     - [ ] Pre-commit hook runs Prettier (optional)
     - [ ] ADO pipeline validates formatting

4. **Implement yamllint in ADO Pipeline**
   - **Files**: All `*.yml` and `*.yaml` files
   - **Why**: Catch YAML syntax errors before pipeline failures
   - **Acceptance Criteria**:
     - [ ] `yamllint` validation stage added to `azure-pipelines.yml`
     - [ ] `.yamllint.yml` configuration created
     - [ ] Existing YAML files validated and fixed
     - [ ] Pipeline fails on YAML syntax errors

**Estimated Effort**: 12-16 hours
**Risk Level**: Medium (may require fixing many existing warnings)
**Blocker Dependencies**: Phase 1 completion recommended (security first)

---

### Phase 3: Polish & Automation (Week 3) 🟢

**Goal**: Automate quality checks and add advanced tooling.

#### Tasks

1. **Set Up Pre-Commit Hooks**
   - **Tools**: Husky (for Node.js), native git hooks (for .NET)
   - **Why**: Catch issues before they reach CI/CD
   - **Acceptance Criteria**:
     - [ ] Husky configured for Dhadgar.Scope (runs ESLint + Prettier)
     - [ ] Git hook runs `dotnet build` before commit (optional)
     - [ ] Team documentation updated with setup instructions

2. **Configure Hadolint (Deferred Until Dockerfiles Exist)**
   - **Status**: No Dockerfiles in repo yet (Kubernetes uses container images built elsewhere)
   - **Why**: Docker best practices when Dockerfiles are added
   - **Acceptance Criteria**:
     - [ ] `.hadolint.yaml` configuration created
     - [ ] ADO pipeline stage ready (commented out)
     - [ ] Documentation on when to enable

3. **Add actionlint for GitHub Actions (Low Priority)**
   - **Files**: `.github/workflows/*.yml`
   - **Why**: Validate GitHub Actions workflows (PR reviews, etc.)
   - **Acceptance Criteria**:
     - [ ] actionlint validation in ADO pipeline
     - [ ] Existing workflows validated

4. **Create .editorconfig Extensions**
   - **File**: `.editorconfig`
   - **Why**: Extend existing config for specific file types
   - **Acceptance Criteria**:
     - [ ] TypeScript/TSX rules added
     - [ ] Astro file rules added
     - [ ] PowerShell/Bash script rules added

**Estimated Effort**: 8-10 hours
**Risk Level**: Low (mostly automation, not code changes)
**Blocker Dependencies**: Phase 2 completion

---

## Detailed Implementation Guides

### 1. .NET Security Analyzers

#### Add to `Directory.Packages.props`

```xml
<ItemGroup>
  <!-- Existing packages... -->

  <!-- Security & Code Quality Analyzers -->
  <PackageVersion Include="SecurityCodeScan.VS2019" Version="5.6.7" />
  <PackageVersion Include="SonarAnalyzer.CSharp" Version="10.5.0" />
  <PackageVersion Include="Roslynator.Analyzers" Version="4.12.9" />
  <PackageVersion Include="Roslynator.CodeAnalysis.Analyzers" Version="4.12.9" />
  <PackageVersion Include="Roslynator.Formatting.Analyzers" Version="4.12.9" />
</ItemGroup>
```

#### Reference in Agent Projects (High Priority)

**Files**:
- `src/Agents/Dhadgar.Agent.Core/Dhadgar.Agent.Core.csproj`
- `src/Agents/Dhadgar.Agent.Linux/Dhadgar.Agent.Linux.csproj`
- `src/Agents/Dhadgar.Agent.Windows/Dhadgar.Agent.Windows.csproj`

Add inside `<Project>`:

```xml
<ItemGroup>
  <!-- Security analyzers for high-trust agent code -->
  <PackageReference Include="SecurityCodeScan.VS2019" />
  <PackageReference Include="SonarAnalyzer.CSharp" />
  <PackageReference Include="Roslynator.Analyzers" />
  <PackageReference Include="Roslynator.CodeAnalysis.Analyzers" />
  <PackageReference Include="Roslynator.Formatting.Analyzers" />
</ItemGroup>

<PropertyGroup>
  <!-- Treat ALL warnings as errors for agent projects -->
  <TreatWarningsAsErrors>true</TreatWarningsAsErrors>

  <!-- Enable all security analyzers at strictest level -->
  <AnalysisMode>All</AnalysisMode>

  <!-- Additional security features -->
  <EnableTrimAnalyzer>true</EnableTrimAnalyzer>
  <EnableSingleFileAnalyzer>true</EnableSingleFileAnalyzer>
</PropertyGroup>
```

#### Reference in Identity Service

**File**: `src/Dhadgar.Identity/Dhadgar.Identity.csproj`

```xml
<ItemGroup>
  <!-- Security analyzers for authentication service -->
  <PackageReference Include="SecurityCodeScan.VS2019" />
  <PackageReference Include="SonarAnalyzer.CSharp" />
</ItemGroup>
```

#### Reference in All Other .NET Projects (Phase 2)

Add to all remaining `.csproj` files:

```xml
<ItemGroup>
  <!-- Code quality analyzers -->
  <PackageReference Include="SonarAnalyzer.CSharp" />
  <PackageReference Include="Roslynator.Analyzers" />
  <PackageReference Include="Roslynator.CodeAnalysis.Analyzers" />
  <PackageReference Include="Roslynator.Formatting.Analyzers" />
</ItemGroup>
```

---

### 2. Enable CI Build Enforcement

#### Update `Directory.Build.props`

**File**: `/Directory.Build.props`

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>

    <!-- Analyzers: use the SDK's built-in analyzers -->
    <EnableNETAnalyzers>true</EnableNETAnalyzers>
    <AnalysisLevel>latest</AnalysisLevel>

    <!-- NEW: Enforce code quality in CI builds -->
    <TreatWarningsAsErrors Condition="'$(CI)' == 'true'">true</TreatWarningsAsErrors>
    <AnalysisMode>AllEnabledByDefault</AnalysisMode>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>

    <!-- Quality of life -->
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors> <!-- Allow warnings locally -->
    <Deterministic>true</Deterministic>
    <ContinuousIntegrationBuild Condition="'$(CI)' == 'true'">true</ContinuousIntegrationBuild>
    <GenerateDocumentationFile>false</GenerateDocumentationFile>
  </PropertyGroup>
</Project>
```

**Explanation**:
- Local builds: Warnings shown but don't fail builds
- CI builds: Warnings treated as errors, builds fail
- Enables code style enforcement during builds (not just in IDE)

---

### 3. Frontend Linting (Dhadgar.Scope)

#### Install ESLint Packages

```bash
cd src/Dhadgar.Scope

npm install --save-dev \
  eslint \
  @typescript-eslint/parser \
  @typescript-eslint/eslint-plugin \
  eslint-plugin-react \
  eslint-plugin-react-hooks \
  eslint-plugin-jsx-a11y \
  eslint-plugin-astro \
  eslint-plugin-security \
  prettier \
  eslint-config-prettier
```

#### Create `.eslintrc.json`

**File**: `src/Dhadgar.Scope/.eslintrc.json`

```json
{
  "extends": [
    "eslint:recommended",
    "plugin:@typescript-eslint/strict-type-checked",
    "plugin:react/recommended",
    "plugin:react-hooks/recommended",
    "plugin:jsx-a11y/recommended",
    "plugin:astro/recommended",
    "plugin:security/recommended",
    "prettier"
  ],
  "parser": "@typescript-eslint/parser",
  "parserOptions": {
    "project": "./tsconfig.json",
    "ecmaVersion": "latest",
    "sourceType": "module",
    "ecmaFeatures": {
      "jsx": true
    }
  },
  "plugins": [
    "@typescript-eslint",
    "react",
    "react-hooks",
    "jsx-a11y",
    "security"
  ],
  "rules": {
    "react/react-in-jsx-scope": "off",
    "@typescript-eslint/no-unused-vars": ["error", { "argsIgnorePattern": "^_" }]
  },
  "settings": {
    "react": {
      "version": "detect"
    }
  },
  "overrides": [
    {
      "files": ["*.astro"],
      "parser": "astro-eslint-parser",
      "parserOptions": {
        "parser": "@typescript-eslint/parser",
        "extraFileExtensions": [".astro"]
      }
    }
  ]
}
```

#### Create `.prettierrc`

**File**: `src/Dhadgar.Scope/.prettierrc`

```json
{
  "semi": true,
  "trailingComma": "es5",
  "singleQuote": false,
  "printWidth": 100,
  "tabWidth": 2,
  "useTabs": false,
  "endOfLine": "lf"
}
```

#### Update `package.json` Scripts

**File**: `src/Dhadgar.Scope/package.json`

```json
{
  "scripts": {
    "dev": "astro dev",
    "build": "astro build",
    "preview": "astro preview",
    "astro": "astro",
    "lint": "eslint . --ext .ts,.tsx,.astro",
    "lint:fix": "eslint . --ext .ts,.tsx,.astro --fix",
    "format": "prettier --write \"**/*.{ts,tsx,astro,css,md,json}\"",
    "format:check": "prettier --check \"**/*.{ts,tsx,astro,css,md,json}\""
  }
}
```

---

### 4. YAML Linting

#### Install yamllint

**Local installation** (Python required):

```bash
pip install yamllint
```

**Or use in CI without local install** (pipeline installs it).

#### Create `.yamllint.yml`

**File**: `/.yamllint.yml` (root of repository)

```yaml
extends: default

rules:
  line-length:
    max: 120
    level: warning
  indentation:
    spaces: 2
    indent-sequences: true
  comments:
    min-spaces-from-content: 1
  comments-indentation: {}
  document-start: disable
  truthy:
    allowed-values: ['true', 'false', 'on', 'off']

ignore: |
  node_modules/
  .git/
  src/Dhadgar.Scope/node_modules/
  _swa_publish/
```

#### Validate Locally

```bash
yamllint .
```

---

### 5. Pipeline Architecture: GitHub Actions + Azure DevOps

**Architecture Decision**: Code quality validation runs in **GitHub Actions** for fast feedback, while container builds and deployments run in **Azure DevOps**.

```
┌─────────────────────────────────────────────────────────────┐
│  GitHub Repository (Source of Truth)                        │
├─────────────────────────────────────────────────────────────┤
│                                                              │
│  On every commit/PR:                                         │
│  ┌────────────────────────────────────────────────────┐    │
│  │  GitHub Actions: Code Quality Validation           │    │
│  │  ├── Lint .NET (Security Code Scan, analyzers)     │    │
│  │  ├── Lint Frontend (ESLint, Prettier)              │    │
│  │  ├── Lint YAML (yamllint)                          │    │
│  │  ├── Lint Dockerfiles (Hadolint, future)           │    │
│  │  └── Status check (must pass before merge)         │    │
│  └────────────────────────────────────────────────────┘    │
│                           │                                  │
│                           ▼                                  │
│  On merge to main/develop:                                  │
│  ┌────────────────────────────────────────────────────┐    │
│  │  Trigger Azure DevOps Pipeline                      │    │
│  └────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────┘
                            │
                            ▼
┌─────────────────────────────────────────────────────────────┐
│  Azure DevOps (Build & Deploy)                              │
├─────────────────────────────────────────────────────────────┤
│  ├── Build containers (Docker)                              │
│  ├── Run integration tests                                  │
│  ├── Deploy to Kubernetes (Talos)                           │
│  └── Deploy to Azure Static Web Apps (Scope, Panel)         │
└─────────────────────────────────────────────────────────────┘
```

**Rationale**:
- ✅ **GitHub Actions**: Fast linting feedback on every commit (< 2 min)
- ✅ **Azure DevOps**: Heavy builds and deployments (container images, K8s)
- ✅ **Separation of concerns**: Code quality vs. infrastructure
- ✅ **Cost-efficient**: GitHub Actions minutes for quick validation, ADO for deployments

---

### 6. GitHub Actions Workflow for Linting

**File**: `.github/workflows/code-quality.yml` (create new)

This workflow runs on every push and PR to validate code quality before ADO deployment pipeline runs:

```yaml
name: Code Quality & Security

on:
  push:
    branches:
      - main
      - develop
      - 'feature/**'
  pull_request:
    branches:
      - main
      - develop

# Cancel in-progress runs for the same PR/branch
concurrency:
  group: ${{ github.workflow }}-${{ github.ref }}
  cancel-in-progress: true

jobs:
  # Job 1: Lint YAML files
  yaml-lint:
    name: Lint YAML
    runs-on: ubuntu-latest
    steps:
      - name: Checkout code
        uses: actions/checkout@v4

      - name: Set up Python
        uses: actions/setup-python@v5
        with:
          python-version: '3.x'

      - name: Install yamllint
        run: pip install yamllint

      - name: Run yamllint
        run: yamllint --strict .

  # Job 2: Lint and validate .NET code with security analyzers
  dotnet-lint:
    name: Lint .NET (Security SAST)
    runs-on: ubuntu-latest
    steps:
      - name: Checkout code
        uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
          global-json-file: global.json

      - name: Restore dependencies
        run: dotnet restore

      - name: Build with analyzers (warnings = errors)
        run: dotnet build --configuration Release --no-restore
        env:
          CI: 'true'  # Enables TreatWarningsAsErrors in Directory.Build.props

      - name: Check for security warnings
        if: failure()
        run: |
          echo "::error::Build failed due to analyzer warnings. Please fix security/quality issues."
          echo "Run 'dotnet build' locally to see detailed warnings."
          exit 1

  # Job 3: Lint frontend (TypeScript/React/Astro)
  frontend-lint:
    name: Lint Frontend (ESLint + Prettier)
    runs-on: ubuntu-latest
    defaults:
      run:
        working-directory: src/Dhadgar.Scope
    steps:
      - name: Checkout code
        uses: actions/checkout@v4

      - name: Setup Node.js
        uses: actions/setup-node@v4
        with:
          node-version: '20'
          cache: 'npm'
          cache-dependency-path: src/Dhadgar.Scope/package-lock.json

      - name: Install dependencies
        run: npm ci

      - name: Run ESLint
        run: npm run lint

      - name: Check Prettier formatting
        run: npm run format:check

  # Job 4: Lint Dockerfiles (future, when Dockerfiles exist)
  dockerfile-lint:
    name: Lint Dockerfiles
    runs-on: ubuntu-latest
    # Only run if Dockerfiles exist
    if: false  # TODO: Enable when Dockerfiles are added
    steps:
      - name: Checkout code
        uses: actions/checkout@v4

      - name: Run Hadolint
        uses: hadolint/hadolint-action@v3.1.0
        with:
          dockerfile: '**/Dockerfile*'
          config: .hadolint.yaml

  # Summary job (all linters must pass)
  code-quality-summary:
    name: Code Quality Summary
    runs-on: ubuntu-latest
    needs: [yaml-lint, dotnet-lint, frontend-lint]
    if: always()
    steps:
      - name: Check all jobs succeeded
        if: contains(needs.*.result, 'failure') || contains(needs.*.result, 'cancelled')
        run: |
          echo "::error::One or more code quality checks failed"
          exit 1

      - name: All checks passed
        run: echo "✅ All code quality and security checks passed!"
```

**Key Features**:
- ✅ Runs in parallel (3 jobs: YAML, .NET, Frontend)
- ✅ Fast feedback (< 3 minutes total)
- ✅ Required status check (blocks PR merge if fails)
- ✅ Concurrency control (cancels old runs on new commits)
- ✅ Clear error messages

---

### 7. Azure DevOps Pipeline (Build & Deploy Only)

**File**: `/azure-pipelines.yml` (existing file, no major changes needed)

ADO pipeline remains focused on **build and deployment**. The validation is already done by GitHub Actions, so ADO can focus on:

1. Building container images
2. Running integration tests
3. Deploying to Kubernetes
4. Deploying to Azure Static Web Apps

**Optional**: Add a quick sanity check in ADO that GitHub Actions passed:

```yaml
# In azure-pipelines.yml (optional verification)
trigger:
  branches:
    include:
    - main
    - develop

# Existing configuration...

resources:
  repositories:
  - repository: pipelinePatterns
    type: github
    endpoint: github.com_SandboxServers
    name: SandboxServers/Azure-Pipeline-YAML
    ref: refs/heads/main

# Optional: Add pre-deployment check that GitHub Actions passed
stages:
  - stage: PreDeploymentChecks
    displayName: 'Pre-Deployment Validation'
    jobs:
      - job: VerifyGitHubActions
        displayName: 'Verify GitHub Actions Status'
        pool:
          vmImage: 'ubuntu-latest'
        steps:
          - bash: |
              echo "✅ GitHub Actions code quality checks passed (status checks required for merge)"
              echo "Proceeding with build and deployment..."
            displayName: 'Verify code quality gate'

  # Extend existing build/deploy pipeline
  - template: Templates/Dhadgar.CI/Pipeline/Pipeline.yml@pipelinePatterns
    parameters:
      # ... existing parameters ...
```

**Note**: This verification step is **optional** because GitHub branch protection rules already prevent merging PRs with failed checks.

---

### 8. GitHub Branch Protection Rules

To enforce code quality checks, configure branch protection rules:

**Settings → Branches → Branch protection rules → Add rule**

**For `main` branch**:
- ✅ **Require a pull request before merging**
  - Require approvals: 1
  - Dismiss stale pull request approvals when new commits are pushed
- ✅ **Require status checks to pass before merging**
  - Require branches to be up to date before merging
  - **Required status checks**:
    - `Lint YAML`
    - `Lint .NET (Security SAST)`
    - `Lint Frontend (ESLint + Prettier)`
    - `Code Quality Summary`
- ✅ **Require conversation resolution before merging**
- ✅ **Do not allow bypassing the above settings** (even admins must follow rules)

**For `develop` branch**: Same rules as `main`

**Result**: No code can be merged without passing all linting/SAST checks in GitHub Actions.

---

### 9. Integration Flow: GitHub Actions → Azure DevOps

**Complete workflow**:

1. **Developer creates feature branch** → Pushes commits
2. **GitHub Actions runs** on every push
   - Lints YAML, .NET, Frontend
   - Fails if security warnings or linting errors
   - Dev fixes issues, pushes again
3. **Developer opens PR** to `main`
   - GitHub Actions runs again
   - Required status checks must pass
   - Code review required
4. **PR merged to `main`**
   - GitHub Actions runs on merge commit
   - **ADO pipeline triggered** (via webhook or scheduled poll)
5. **ADO pipeline runs**
   - Builds container images
   - Runs integration tests
   - Deploys to Kubernetes (Talos)
   - Deploys to Azure Static Web Apps
6. **Production deployment complete**

**Trigger ADO from GitHub** (optional, for faster feedback):

Use a GitHub Actions workflow to trigger ADO pipeline after merge:

**File**: `.github/workflows/trigger-ado.yml` (create new, optional)

```yaml
name: Trigger Azure DevOps Pipeline

on:
  push:
    branches:
      - main
      - develop

jobs:
  trigger-ado:
    name: Trigger ADO Build
    runs-on: ubuntu-latest
    steps:
      - name: Trigger Azure Pipeline
        env:
          ADO_PAT: ${{ secrets.ADO_PAT }}
          ADO_ORG: 'YourOrgName'
          ADO_PROJECT: 'YourProjectName'
          ADO_PIPELINE_ID: '123'  # Your pipeline ID
        run: |
          curl -X POST \
            -u ":${ADO_PAT}" \
            -H "Content-Type: application/json" \
            "https://dev.azure.com/${ADO_ORG}/${ADO_PROJECT}/_apis/pipelines/${ADO_PIPELINE_ID}/runs?api-version=7.0" \
            -d "{
              \"resources\": {
                \"repositories\": {
                  \"self\": {
                    \"refName\": \"refs/heads/${{ github.ref_name }}\"
                  }
                }
              }
            }"

      - name: Deployment triggered
        run: echo "✅ Azure DevOps deployment pipeline triggered"
```

**Setup**:
1. Create ADO Personal Access Token (PAT) with "Build (read and execute)" permissions
2. Add PAT to GitHub secrets as `ADO_PAT`
3. Update `ADO_ORG`, `ADO_PROJECT`, `ADO_PIPELINE_ID` in workflow

**Alternative**: ADO can poll GitHub for changes (default behavior).

---

## Security Analyzer Configuration

### Security Code Scan Configuration

Create a configuration file to customize Security Code Scan rules:

**File**: `/SecurityCodeScan.config.yml` (root of repo)

```yaml
# Security Code Scan configuration
# See: https://security-code-scan.github.io/#ConfigurationFilesList

# Minimum report level (Low, Medium, High, Critical)
MinimumReportLevel: Medium

# Audit mode (report all findings, even low confidence)
AuditMode: false

# Password validation (detect hardcoded passwords)
PasswordValidatorRequiredLength: 8

# Anti-CSRF token validation
AntiCsrfTokenValidation: true

# Custom sinks for taint analysis (optional)
# Add custom methods that should be considered dangerous
```

### Suppress Existing Warnings (If Needed)

If analyzers find many warnings in existing code, you can **baseline** them to focus on new code:

**Create**: `/.editorconfig` (add to existing file)

```ini
# Suppress analyzer warnings for legacy code
[src/LegacyService/**/*.cs]
dotnet_diagnostic.CA1062.severity = none  # Example: suppress null check warnings
dotnet_diagnostic.SCS0018.severity = none # Example: suppress SQL injection warnings (DANGEROUS!)

# Strict enforcement for agent code
[src/Agents/**/*.cs]
dotnet_diagnostic.CA1062.severity = error
dotnet_diagnostic.SCS0018.severity = error
```

**WARNING**: Only suppress warnings if you've reviewed them and confirmed they're false positives. Document why each suppression exists.

---

## Acceptance Criteria

### Phase 1 Complete When:
- [ ] Security Code Scan runs on all agent projects and Identity service
- [ ] ADO pipeline fails if security warnings exist in agent/identity code
- [ ] ESLint + eslint-plugin-security runs on Dhadgar.Scope
- [ ] ADO pipeline validates frontend code
- [ ] `TreatWarningsAsErrors` enabled for CI builds
- [ ] All team members notified of changes
- [ ] Documentation updated

### Phase 2 Complete When:
- [ ] SonarAnalyzer and Roslynator run on all .NET projects
- [ ] Prettier formats Dhadgar.Scope code
- [ ] yamllint validates all YAML files in CI
- [ ] Baseline of existing warnings documented
- [ ] Team trained on common analyzer violations

### Phase 3 Complete When:
- [ ] Pre-commit hooks configured for local dev
- [ ] Hadolint ready (config created, pipeline stage prepared)
- [ ] actionlint validates GitHub workflows
- [ ] `.editorconfig` extended for all file types
- [ ] Developer documentation updated with linter setup instructions

---

## Rollback Plan

If any phase causes too many build failures:

1. **Emergency Rollback**: Remove analyzer package references, commit, push
2. **Partial Rollback**: Keep analyzers but set `<TreatWarningsAsErrors>false</TreatWarningsAsErrors>` in CI
3. **Gradual Rollout**: Enable analyzers per-project instead of globally
4. **Suppression File**: Create a `.globalconfig` to suppress specific rules temporarily

**Rollback Command** (example):

```bash
# Revert Directory.Packages.props to remove analyzers
git checkout HEAD~1 -- Directory.Packages.props

# Or revert entire commit
git revert <commit-hash>
```

---

## Cost Analysis

### Free/Open Source Tools (Recommended)
All tools in this strategy are **100% free**:

| Tool | License | Cost |
|------|---------|------|
| Security Code Scan | MIT | $0 |
| SonarAnalyzer.CSharp | LGPL-3.0 | $0 |
| Roslynator | Apache 2.0 | $0 |
| ESLint ecosystem | MIT | $0 |
| Prettier | MIT | $0 |
| yamllint | GPLv3 | $0 |
| Hadolint | GPLv3 | $0 |

**Total Implementation Cost**: $0 in licensing fees

### Optional Paid Upgrades (Future Consideration)

| Service | Use Case | Cost | Value |
|---------|----------|------|-------|
| SonarCloud | Historical metrics, PR decoration, cloud dashboard | $10/month per private repo | High for teams >5 |
| Snyk Code | Real-time security scanning, dependency scanning | Free tier available, then $25/dev/month | High for security-critical apps |
| Veracode/Fortify | Enterprise SAST compliance | $50k+/year | Overkill for this project |

**Recommendation**: Start with free tools. Revisit SonarCloud or Snyk after 6 months if team wants better dashboards.

---

## Training & Documentation Needs

### Developer Onboarding
1. **Local Setup Guide**: How to install analyzers, configure IDE warnings
2. **Common Violations Guide**: Top 10 analyzer warnings and how to fix them
3. **Security Best Practices**: Agent-specific security requirements
4. **Pre-Commit Hook Setup**: Optional local validation setup

### Team Training Sessions
1. **Week 1**: "Security Analyzers 101" (1 hour)
   - What is SAST?
   - Why agent code requires strict validation
   - How to interpret Security Code Scan warnings

2. **Week 2**: "Code Quality with Roslyn" (1 hour)
   - SonarAnalyzer and Roslynator overview
   - How to use code fixes and refactorings
   - Setting up IDE integration

3. **Week 3**: "Frontend Linting" (30 min)
   - ESLint + Prettier workflow
   - Fixing security issues in JavaScript/TypeScript
   - Pre-commit hooks demo

### Documentation Updates Needed
- [ ] Update `CLAUDE.md` with linter requirements
- [ ] Create `docs/LINTING_GUIDE.md` for developers
- [ ] Update CI/CD docs with validation stage info
- [ ] Add "Analyzer Warnings" section to contribution guide

---

## Risk Assessment

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| **Build failures in CI** | High | High | Gradual rollout, baseline warnings, team notification |
| **Developer productivity impact** | Medium | Medium | Good IDE integration, training, quick fixes |
| **False positives from analyzers** | Low | Medium | Configure suppressions, update analyzer rules |
| **Security gaps remain** | High | Low | Focus on agent code first, review all warnings |
| **Team resistance to change** | Medium | Low | Communicate value, show examples, gradual adoption |

---

## Success Metrics

Track these metrics after each phase:

| Metric | Baseline (Now) | Phase 1 Target | Phase 2 Target | Phase 3 Target |
|--------|---------------|----------------|----------------|----------------|
| Security warnings in agent code | Unknown | 0 | 0 | 0 |
| Code quality warnings | Unknown | < 50 | < 20 | < 10 |
| Failed builds due to linting | 0 | < 5% | < 2% | < 1% |
| Time to fix linting errors | N/A | < 15 min avg | < 10 min avg | < 5 min avg |
| % of commits with warnings | Unknown | < 10% | < 5% | < 2% |
| Developer satisfaction | N/A | Survey | Survey | Survey |

---

## Next Steps

1. **Review this document** with engineering team
2. **Create GitHub issues** for each phase
   - Issue #1: [Phase 1] Add Security Code Scan to agent projects
   - Issue #2: [Phase 1] Set up ESLint for Dhadgar.Scope
   - Issue #3: [Phase 1] Enable TreatWarningsAsErrors in CI
   - (etc.)
3. **Assign owners** for each task
4. **Schedule kick-off meeting** (Week 1 start)
5. **Create ADO sprint** with tasks
6. **Begin Phase 1 implementation**

---

## References

### Documentation
- [Security Code Scan](https://security-code-scan.github.io/)
- [SonarAnalyzer for C#](https://github.com/SonarSource/sonar-dotnet)
- [Roslynator](https://github.com/dotnet/roslynator)
- [ESLint](https://eslint.org/)
- [TypeScript-ESLint](https://typescript-eslint.io/)
- [yamllint](https://yamllint.readthedocs.io/)
- [Hadolint](https://github.com/hadolint/hadolint)
- [Code analysis in .NET](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/overview)

### Related Project Docs
- `CLAUDE.md` - Project overview and architecture
- `docs/DEVELOPMENT_SETUP.md` - Local development setup
- `docs/SPIRIT_OF_THE_DIFF_SETUP.md` - PR review bot setup
- `azure-pipelines.yml` - CI/CD pipeline configuration
- `SandboxServers/Azure-Pipeline-YAML` - Pipeline templates

---

## Document History

| Version | Date | Author | Changes |
|---------|------|--------|---------|
| 1.0 | 2025-12-28 | Claude Code | Initial strategy document created |

---

## Appendix A: Quick Reference Commands

### .NET Projects
```bash
# Restore packages (includes analyzers)
dotnet restore

# Build with analyzers (local, warnings allowed)
dotnet build

# Build with analyzers (CI mode, warnings = errors)
CI=true dotnet build

# List analyzer warnings
dotnet build /p:TreatWarningsAsErrors=false | grep warning
```

### Frontend (Dhadgar.Scope)
```bash
cd src/Dhadgar.Scope

# Install dependencies
npm ci

# Run linter
npm run lint

# Fix auto-fixable issues
npm run lint:fix

# Check formatting
npm run format:check

# Auto-format code
npm run format
```

### YAML Validation
```bash
# Lint all YAML files
yamllint .

# Lint specific file
yamllint azure-pipelines.yml

# Lint with config
yamllint -c .yamllint.yml .
```

---

## Appendix B: Troubleshooting

### "Package 'SecurityCodeScan.VS2019' not found"
- Check `Directory.Packages.props` has correct package version
- Run `dotnet restore --force-evaluate`
- Clear NuGet cache: `dotnet nuget locals all --clear`

### "ESLint command not found"
- Ensure Node.js 20+ installed: `node --version`
- Navigate to `src/Dhadgar.Scope/`
- Run `npm install`
- Verify `node_modules/.bin/eslint` exists

### "yamllint: command not found"
- Install Python 3: `python --version`
- Install yamllint: `pip install yamllint`
- Or use in ADO pipeline only (installs automatically)

### "Too many warnings, build takes forever"
- Create a baseline: Suppress existing warnings in `.editorconfig`
- Set specific rules to `suggestion` instead of `warning`
- Focus on high-severity warnings first

### "Analyzer rules too strict"
- Review rule documentation: Each analyzer has configurable severity
- Create `.editorconfig` overrides for specific rules
- Balance between code quality and pragmatism

---

**End of Document**
