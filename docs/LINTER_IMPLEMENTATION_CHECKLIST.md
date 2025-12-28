# Linter & SAST Implementation Checklist

**Quick Reference Guide for Implementing Code Quality Tools**

Use this checklist alongside the full strategy document ([LINTER_SAST_STRATEGY.md](./LINTER_SAST_STRATEGY.md)).

---

## Pipeline Architecture Overview

**Important**: This project uses a **split pipeline architecture**:

```
GitHub Actions (Fast Feedback)        Azure DevOps (Build & Deploy)
────────────────────────────          ────────────────────────────
✅ Code linting (YAML, .NET, JS)  →   🐳 Container builds
✅ Security SAST analysis         →   ☸️  Kubernetes deployments
✅ Prettier formatting            →   🌐 Azure Static Web Apps
✅ Required PR checks             →   🧪 Integration tests
```

**What goes where**:
- **GitHub Actions**: All linting/SAST validation (runs on every commit/PR)
- **Azure DevOps**: All builds, container images, and deployments (runs after merge)

**Why this split**?
- Fast feedback in GitHub (< 3 min) without waiting for heavy builds
- GitHub branch protection blocks merging bad code
- ADO focuses on what it's good at: infrastructure and deployment

---

## Phase 1: Security-Critical (Week 1) 🔴

### 1. Add Security Code Scan Package

- [ ] **Edit**: `Directory.Packages.props`
  ```xml
  <PackageVersion Include="SecurityCodeScan.VS2019" Version="5.6.7" />
  ```

### 2. Update Agent Projects (HIGH PRIORITY)

- [ ] **Edit**: `src/Agents/Dhadgar.Agent.Core/Dhadgar.Agent.Core.csproj`
- [ ] **Edit**: `src/Agents/Dhadgar.Agent.Linux/Dhadgar.Agent.Linux.csproj`
- [ ] **Edit**: `src/Agents/Dhadgar.Agent.Windows/Dhadgar.Agent.Windows.csproj`

  Add to each:
  ```xml
  <ItemGroup>
    <PackageReference Include="SecurityCodeScan.VS2019" />
  </ItemGroup>

  <PropertyGroup>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <AnalysisMode>All</AnalysisMode>
  </PropertyGroup>
  ```

### 3. Update Identity Service

- [ ] **Edit**: `src/Dhadgar.Identity/Dhadgar.Identity.csproj`
  ```xml
  <ItemGroup>
    <PackageReference Include="SecurityCodeScan.VS2019" />
  </ItemGroup>
  ```

### 4. Enable CI Warnings-as-Errors

- [ ] **Edit**: `Directory.Build.props`
  ```xml
  <!-- Add after existing <AnalysisLevel>latest</AnalysisLevel> -->
  <TreatWarningsAsErrors Condition="'$(CI)' == 'true'">true</TreatWarningsAsErrors>
  <AnalysisMode>AllEnabledByDefault</AnalysisMode>
  <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
  ```

### 5. Test Locally

- [ ] **Run**: `dotnet restore`
- [ ] **Run**: `dotnet build`
- [ ] **Check**: No security warnings in agent projects
- [ ] **Fix**: Any warnings found

### 6. Set Up Frontend Linting (Dhadgar.Scope)

- [ ] **Run** (in `src/Dhadgar.Scope/`):
  ```bash
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

- [ ] **Create**: `src/Dhadgar.Scope/.eslintrc.json` (see full strategy doc for content)
- [ ] **Create**: `src/Dhadgar.Scope/.prettierrc` (see full strategy doc for content)

- [ ] **Edit**: `src/Dhadgar.Scope/package.json` - add scripts:
  ```json
  {
    "scripts": {
      "lint": "eslint . --ext .ts,.tsx,.astro",
      "lint:fix": "eslint . --ext .ts,.tsx,.astro --fix",
      "format": "prettier --write \"**/*.{ts,tsx,astro,css,md,json}\"",
      "format:check": "prettier --check \"**/*.{ts,tsx,astro,css,md,json}\""
    }
  }
  ```

- [ ] **Run**: `npm run lint` (fix any errors)
- [ ] **Run**: `npm run format` (auto-format code)

### 7. Create GitHub Actions Workflow for Linting

- [ ] **Create**: `.github/workflows/code-quality.yml` (see full strategy doc for complete YAML)

  Key sections to include:
  - YAML linting job
  - .NET linting job (with security analyzers)
  - Frontend linting job (ESLint + Prettier)
  - Summary job

- [ ] **Test**: Commit changes to feature branch and push
- [ ] **Verify**: GitHub Actions workflow runs
- [ ] **Check**: All linting jobs pass

### 8. Configure GitHub Branch Protection

- [ ] **Navigate**: GitHub repo → Settings → Branches → Add rule
- [ ] **Branch name pattern**: `main`
- [ ] **Enable**:
  - ✅ Require pull request before merging (1 approval)
  - ✅ Require status checks to pass before merging
  - ✅ Required status checks:
    - `Lint YAML`
    - `Lint .NET (Security SAST)`
    - `Lint Frontend (ESLint + Prettier)`
    - `Code Quality Summary`
  - ✅ Require conversation resolution before merging
- [ ] **Repeat**: Same rules for `develop` branch
- [ ] **Test**: Try to merge PR without passing checks (should be blocked)

### 9. Team Communication

- [ ] **Notify team**: New analyzers enabled, expect warnings in IDE
- [ ] **Schedule**: "Security Analyzers 101" training (1 hour)
- [ ] **Document**: Update team wiki with setup instructions

---

## Phase 2: Code Quality Foundation (Week 2) 🟡

### 1. Add Code Quality Analyzers

- [ ] **Edit**: `Directory.Packages.props`
  ```xml
  <PackageVersion Include="SonarAnalyzer.CSharp" Version="10.5.0" />
  <PackageVersion Include="Roslynator.Analyzers" Version="4.12.9" />
  <PackageVersion Include="Roslynator.CodeAnalysis.Analyzers" Version="4.12.9" />
  <PackageVersion Include="Roslynator.Formatting.Analyzers" Version="4.12.9" />
  ```

### 2. Add Analyzers to All .NET Projects

- [ ] **Create script**: To bulk-update all `.csproj` files, or update manually

  Add to each `.csproj`:
  ```xml
  <ItemGroup>
    <PackageReference Include="SonarAnalyzer.CSharp" />
    <PackageReference Include="Roslynator.Analyzers" />
    <PackageReference Include="Roslynator.CodeAnalysis.Analyzers" />
    <PackageReference Include="Roslynator.Formatting.Analyzers" />
  </ItemGroup>
  ```

### 3. Baseline Existing Warnings

- [ ] **Run**: `dotnet build > build-warnings.txt`
- [ ] **Review**: All warnings in `build-warnings.txt`
- [ ] **Decide**: Which warnings to fix now vs. suppress
- [ ] **Optional**: Create `.editorconfig` suppressions for legacy code

### 4. Configure YAML Linting

- [ ] **Create**: `.yamllint.yml` (see full strategy doc for content)
- [ ] **Install locally** (optional): `pip install yamllint`
- [ ] **Run locally** (optional): `yamllint .`
- [ ] **Note**: YAML linting already added to GitHub Actions in Phase 1

### 5. Add Prettier to Frontend

- [ ] **Already installed** (from Phase 1)
- [ ] **Run**: `npm run format` to format all files
- [ ] **Commit**: Formatted code
- [ ] **Note**: Prettier check already in GitHub Actions workflow

---

## Phase 3: Polish & Automation (Week 3) 🟢

### 1. Pre-Commit Hooks (Frontend)

- [ ] **Run** (in `src/Dhadgar.Scope/`):
  ```bash
  npm install --save-dev husky lint-staged
  npx husky install
  ```

- [ ] **Create**: `.husky/pre-commit`
  ```bash
  #!/bin/sh
  . "$(dirname "$0")/_/husky.sh"

  cd src/Dhadgar.Scope
  npx lint-staged
  ```

- [ ] **Edit**: `src/Dhadgar.Scope/package.json`
  ```json
  {
    "lint-staged": {
      "*.{ts,tsx,astro}": ["eslint --fix", "prettier --write"],
      "*.{css,md,json}": ["prettier --write"]
    }
  }
  ```

### 2. Hadolint Configuration (Future)

- [ ] **Create**: `.hadolint.yaml` (ready for when Dockerfiles are added)
  ```yaml
  ignored:
    - DL3008
  trustedRegistries:
    - mcr.microsoft.com
    - docker.io
  ```

### 3. Extend .editorconfig

- [ ] **Edit**: `.editorconfig` - add rules for TypeScript, Astro, scripts

  ```ini
  [*.{ts,tsx}]
  indent_size = 2

  [*.astro]
  indent_size = 2

  [*.{ps1,sh}]
  indent_size = 2
  ```

### 4. Developer Documentation

- [ ] **Create**: `docs/LINTING_GUIDE.md` (developer setup instructions)
- [ ] **Update**: `CLAUDE.md` with linter requirements
- [ ] **Update**: Contribution guide with analyzer info

---

## Verification Steps

### After Phase 1
- [ ] Run `dotnet build` locally - should succeed with no warnings in agent code
- [ ] Run `CI=true dotnet build` - should fail if ANY warnings exist
- [ ] Run `npm run lint` in Scope - should pass
- [ ] Push to feature branch - **GitHub Actions** workflow should run and pass
- [ ] Open PR to main - GitHub Actions required checks must pass
- [ ] Merge to main - **ADO pipeline** triggers for deployment

### After Phase 2
- [ ] All .NET projects have quality analyzers
- [ ] yamllint passes on all YAML files
- [ ] Prettier formats all frontend code
- [ ] Team can fix common analyzer warnings

### After Phase 3
- [ ] Pre-commit hooks prevent bad commits
- [ ] .editorconfig covers all file types
- [ ] Developer docs complete
- [ ] Team satisfied with tooling

---

## Quick Commands Reference

### .NET
```bash
# Restore with new analyzers
dotnet restore

# Build locally (warnings OK)
dotnet build

# Build in CI mode (warnings = errors)
CI=true dotnet build

# Clean solution
dotnet clean
```

### Frontend (Dhadgar.Scope)
```bash
cd src/Dhadgar.Scope

# Install dependencies
npm ci

# Lint code
npm run lint

# Fix lint errors
npm run lint:fix

# Format code
npm run format
```

### YAML
```bash
# Lint all YAML
yamllint .

# Lint specific file
yamllint azure-pipelines.yml
```

---

## Rollback Commands

### If build breaks:
```bash
# Revert Directory.Packages.props
git checkout HEAD~1 -- Directory.Packages.props

# Revert all .csproj changes
git checkout HEAD~1 -- "**/*.csproj"

# Or full commit revert
git revert <commit-hash>
```

### If pipeline breaks:
```bash
# Revert azure-pipelines.yml
git checkout HEAD~1 -- azure-pipelines.yml
```

---

## Issue Templates (Copy to GitHub/ADO)

### Phase 1 Issues

**Issue #1: Add Security Code Scan to Agent Projects**
```
**Goal**: Enable security analysis on high-trust agent code

**Tasks**:
- [ ] Add SecurityCodeScan.VS2019 to Directory.Packages.props
- [ ] Update Dhadgar.Agent.Core.csproj
- [ ] Update Dhadgar.Agent.Linux.csproj
- [ ] Update Dhadgar.Agent.Windows.csproj
- [ ] Test builds locally
- [ ] Fix any security warnings
- [ ] Commit and push

**Acceptance Criteria**:
- Security analyzer runs on all agent projects
- No security warnings in agent code
- Local build succeeds

**Estimated Effort**: 2 hours
```

**Issue #2: Set Up ESLint for Dhadgar.Scope**
```
**Goal**: Enable security linting for frontend code

**Tasks**:
- [ ] Install ESLint packages
- [ ] Create .eslintrc.json
- [ ] Create .prettierrc
- [ ] Add npm scripts to package.json
- [ ] Run linter and fix errors
- [ ] Format all code
- [ ] Commit and push

**Acceptance Criteria**:
- ESLint + security plugin configured
- npm run lint passes
- Code formatted with Prettier

**Estimated Effort**: 3 hours
```

**Issue #3: Enable CI Warnings-as-Errors**
```
**Goal**: Enforce code quality in CI/CD pipeline

**Tasks**:
- [ ] Update Directory.Build.props
- [ ] Test locally (warnings should be OK)
- [ ] Test in CI (set CI=true)
- [ ] Add validation stage to azure-pipelines.yml
- [ ] Notify team
- [ ] Merge to main

**Acceptance Criteria**:
- Local builds allow warnings
- CI builds fail on warnings
- ADO pipeline runs validation stage

**Estimated Effort**: 2 hours
```

---

## Success Criteria

### Phase 1 Complete ✅
- Security analyzers run on agent projects and Identity service
- ESLint + security plugin run on Dhadgar.Scope
- ADO pipeline validates code quality
- CI builds fail on warnings
- Zero security warnings in agent code

### Phase 2 Complete ✅
- All .NET projects have quality analyzers
- YAML linting enabled
- Prettier formats frontend code
- Baseline of warnings documented

### Phase 3 Complete ✅
- Pre-commit hooks prevent bad commits
- .editorconfig comprehensive
- Developer docs complete
- Team trained

---

**Use this checklist to track progress. Check off items as you complete them!**
