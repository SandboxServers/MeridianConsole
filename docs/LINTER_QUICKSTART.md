# Linter & SAST Quickstart Guide

**Get started with code quality validation in 5 minutes**

This guide helps you quickly enable linting and security analysis for the Meridian Console project.

---

## TL;DR - What You Need to Know

### 🔀 Split Pipeline Architecture

```
┌─────────────────────────┐         ┌──────────────────────────┐
│   GitHub Actions        │         │   Azure DevOps           │
│   (Linting & SAST)      │    ─→   │   (Build & Deploy)       │
│                         │         │                          │
│  • YAML linting         │         │  • Container builds      │
│  • .NET security scan   │         │  • K8s deployments       │
│  • Frontend linting     │         │  • Integration tests     │
│  • Blocks PR merge      │         │  • Azure SWA deploys     │
└─────────────────────────┘         └──────────────────────────┘
     Fast (< 3 min)                      Heavy lifting
```

**Why?**
- **GitHub Actions**: Fast feedback on every commit/PR (must pass before merge)
- **Azure DevOps**: Build containers and deploy after merge

---

## 📁 What's Been Created

### New Files
1. **`.github/workflows/code-quality.yml`** - GitHub Actions workflow (ready to use!)
2. **`docs/LINTER_SAST_STRATEGY.md`** - Full implementation strategy
3. **`docs/LINTER_IMPLEMENTATION_CHECKLIST.md`** - Step-by-step checklist
4. **`docs/LINTER_QUICKSTART.md`** - This file

### Configuration Files to Create
- `.yamllint.yml` - YAML linting rules
- `src/Dhadgar.Scope/.eslintrc.json` - ESLint configuration
- `src/Dhadgar.Scope/.prettierrc` - Prettier configuration

---

## 🚀 Quick Start (Phase 1 in 30 Minutes)

### Step 1: Enable .NET Security Analyzers (10 min)

**Add to `Directory.Packages.props`:**
```xml
<PackageVersion Include="SecurityCodeScan.VS2019" Version="5.6.7" />
```

**Update agent projects** (`Dhadgar.Agent.Core`, `Dhadgar.Agent.Linux`, `Dhadgar.Agent.Windows`):
```xml
<ItemGroup>
  <PackageReference Include="SecurityCodeScan.VS2019" />
</ItemGroup>

<PropertyGroup>
  <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  <AnalysisMode>All</AnalysisMode>
</PropertyGroup>
```

**Update `Directory.Build.props`:**
```xml
<!-- Add after existing <AnalysisLevel>latest</AnalysisLevel> -->
<TreatWarningsAsErrors Condition="'$(CI)' == 'true'">true</TreatWarningsAsErrors>
<AnalysisMode>AllEnabledByDefault</AnalysisMode>
<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
```

**Test:**
```bash
dotnet restore
dotnet build  # Should pass locally
CI=true dotnet build  # Should fail if any warnings
```

---

### Step 2: Set Up Frontend Linting (10 min)

**Install packages:**
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

**Create `.eslintrc.json`** (minimal config):
```json
{
  "extends": [
    "eslint:recommended",
    "plugin:@typescript-eslint/recommended",
    "plugin:react/recommended",
    "plugin:react-hooks/recommended",
    "plugin:security/recommended"
  ],
  "parser": "@typescript-eslint/parser",
  "parserOptions": {
    "ecmaVersion": "latest",
    "sourceType": "module"
  },
  "rules": {
    "react/react-in-jsx-scope": "off"
  },
  "settings": {
    "react": {
      "version": "detect"
    }
  }
}
```

**Create `.prettierrc`:**
```json
{
  "semi": true,
  "singleQuote": false,
  "printWidth": 100,
  "tabWidth": 2
}
```

**Update `package.json` scripts:**
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

**Test:**
```bash
npm run lint
npm run format
```

---

### Step 3: Create YAML Linting Config (5 min)

**Create `.yamllint.yml`** in repo root:
```yaml
extends: default

rules:
  line-length:
    max: 120
    level: warning
  indentation:
    spaces: 2
  document-start: disable

ignore: |
  node_modules/
  .git/
  src/Dhadgar.Scope/node_modules/
```

**Test locally** (optional):
```bash
pip install yamllint
yamllint .
```

---

### Step 4: Enable GitHub Actions (5 min)

**The workflow already exists!** (`.github/workflows/code-quality.yml`)

**Test it:**
```bash
git checkout -b feature/enable-linters
git add .
git commit -m "feat: enable code quality analyzers and linting"
git push origin feature/enable-linters
```

Go to GitHub → Actions tab → Watch "Code Quality & Security" workflow run

---

### Step 5: Configure Branch Protection (5 min)

**GitHub → Settings → Branches → Add rule**

**Branch name pattern:** `main`

**Enable:**
- ✅ Require pull request before merging (1 approval)
- ✅ Require status checks to pass before merging
  - Select: `Lint YAML`, `Lint .NET (Security SAST)`, `Lint Frontend (ESLint + Prettier)`, `Code Quality Summary`
- ✅ Require conversation resolution before merging

**Repeat for `develop` branch**

---

## ✅ Verification

After completing the steps above, you should see:

1. **Local development**:
   - `dotnet build` shows warnings (doesn't fail)
   - `npm run lint` in Scope catches TypeScript/React issues
   - Your IDE shows analyzer warnings inline

2. **GitHub Actions**:
   - Every commit triggers the "Code Quality & Security" workflow
   - Workflow runs 3 jobs in parallel (YAML, .NET, Frontend)
   - Status checks appear on PRs (must pass to merge)

3. **Branch protection**:
   - Cannot merge PR if any linter fails
   - Cannot bypass checks (even as admin)

4. **Azure DevOps**:
   - Continues to handle builds and deployments
   - Only runs after code passes GitHub validation

---

## 🐛 Troubleshooting

### "Workflow not running on push"
- Check `.github/workflows/code-quality.yml` exists
- Verify branch name matches trigger (`main`, `develop`, or `feature/*`)
- Check GitHub Actions is enabled: Settings → Actions → Allow all actions

### "Build fails with analyzer warnings"
- Run `dotnet build` locally to see warnings
- Fix security/quality issues in code
- Or suppress specific warnings in `.editorconfig` (not recommended for security issues)

### "ESLint not found"
- Navigate to `src/Dhadgar.Scope/`
- Run `npm install`
- Verify `node_modules/.bin/eslint` exists

### "Status checks not appearing on PR"
- Workflow must run at least once on the PR branch
- Push a commit to trigger the workflow
- Check Actions tab for workflow status

---

## 📊 What Gets Checked?

### .NET Security Analysis
- ✅ SQL injection vulnerabilities
- ✅ Cross-site scripting (XSS)
- ✅ Cross-site request forgery (CSRF)
- ✅ XML external entity (XXE) attacks
- ✅ Hardcoded secrets/credentials
- ✅ Weak cryptography
- ✅ Path traversal vulnerabilities
- ✅ Command injection

### Frontend Security
- ✅ Unsafe DOM manipulation
- ✅ Prototype pollution
- ✅ Regex denial of service (ReDoS)
- ✅ Unsafe eval usage
- ✅ XSS vulnerabilities in React

### Code Quality
- ✅ TypeScript type safety
- ✅ React hooks rules
- ✅ Accessibility (a11y) issues
- ✅ Code formatting consistency
- ✅ YAML syntax errors

---

## 📈 What's Next?

### Phase 2: Enhanced Quality (Optional)
- Add SonarAnalyzer for comprehensive code quality
- Add Roslynator for C# best practices
- Baseline existing warnings
- Consider SonarCloud integration

### Phase 3: Automation (Optional)
- Pre-commit hooks (Husky for Node.js)
- Auto-formatting on save in IDE
- Hadolint for Dockerfiles (when added)

**See full details in `LINTER_SAST_STRATEGY.md`**

---

## 🆘 Need Help?

1. **Quick reference**: Check `LINTER_IMPLEMENTATION_CHECKLIST.md`
2. **Full strategy**: Read `LINTER_SAST_STRATEGY.md`
3. **Team questions**: Bring to daily standup or schedule training session

---

## 📝 Summary

**Time investment**: 30 minutes to enable Phase 1
**Benefit**: Catch security vulnerabilities and code quality issues before they reach production
**Risk**: Low (analyzers won't break existing code, just flag issues)

**Critical for**: Agent code security (runs on customer hardware!)

---

**Ready to start? Follow Step 1 above!**
