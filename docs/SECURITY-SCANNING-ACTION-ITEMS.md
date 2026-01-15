# Security Scanning Implementation - Action Items

This document outlines the steps required to enable the security scanning pipeline in Dhadgar CI/CD.

## 📋 Overview

The security scanning stage includes 6 scanning categories:
1. **SAST** (Semgrep) - C# code pattern analysis
2. **SCA** (OWASP Dependency-Check) - NuGet CVE scanning
3. **Container Scanning** (Trivy) - Docker image vulnerabilities
4. **IaC Scanning** (Checkov) - Kubernetes/Docker/YAML security
5. **Secret Scanning** (GitLeaks) - Exposed credentials detection
6. **SBOM Generation** (Syft) - Software Bill of Materials

**Total Cost**: $0/month (all free/open-source tools)

---

## ✅ Action Items

### 1. Wire Security Stage into Dhadgar CI Template

**File**: `Azure-Pipeline-YAML/azure-pipelines-dhadgar.yml` (or wherever your main pipeline is)

**Action**: Add Security stage to the pipeline by importing the template

**Example**:
```yaml
# After your Build.yml template reference
- template: Templates/Dhadgar.CI/Stages/Build.yml
  parameters:
    services: ${{ parameters.services }}
    servicesCsvNormalized: ${{ parameters.servicesCsvNormalized }}
    buildConfiguration: ${{ parameters.buildConfiguration }}
    dotnetSdkVersion: ${{ parameters.dotnetSdkVersion }}
    includePreview: ${{ parameters.includePreview }}

# Add this:
- template: Templates/Dhadgar.CI/Stages/Security.yml
  parameters:
    services: ${{ parameters.services }}
    servicesCsvNormalized: ${{ parameters.servicesCsvNormalized }}
    solutionPath: 'Dhadgar.sln'
    runSast: true
    runSca: true
    runContainerScan: true
    runIacScan: true
    runSecretScan: true
    runSbom: true
    failOnCritical: true
    failOnHigh: false
    nvdApiKey: $(NVD_API_KEY)  # See action item #2
```

**Status**: ⏳ Pending

---

### 2. Obtain Free NVD API Key (✅ WIRED IN - Just add the key)

**Why**: Speeds up OWASP Dependency-Check scans from 15+ minutes to 2-3 minutes

**Steps**:
1. Visit: https://nvd.nist.gov/developers/request-an-api-key
2. Fill out the form (use `steve@sandboxservers.com` or your email)
3. Receive API key via email (instant)
4. Add to Azure DevOps Pipeline as secret variable

**How to add secret variable** (2 options):

#### Option A: Variable Group (Recommended - Reusable)
1. Go to: Azure DevOps → Pipelines → Library → Variable Groups
2. Click "**+ Variable group**"
3. Name: `dhadgar-security-scanning`
4. Click "**+ Add**"
   - Name: `NVD_API_KEY`
   - Value: `<paste your API key here>`
   - Click the **lock icon** to mark as secret
5. Click "**Save**"
6. Add this to `azure-pipelines.yml` (under `variables:`):
   ```yaml
   variables:
     - group: dhadgar-security-scanning
   ```

#### Option B: Pipeline Variable (Quick - Single pipeline)
1. Go to: Azure DevOps → Pipelines → Select "Meridian Console" pipeline
2. Click "**Edit**" → "**Variables**" (top right)
3. Click "**+ New variable**"
   - Name: `NVD_API_KEY`
   - Value: `<paste your API key here>`
   - Check "**Keep this value secret**"
4. Click "**OK**" → "**Save**"

**Already Wired**: The pipeline is already configured to use `$(NVD_API_KEY)` - just add the secret!

**Status**: ⏳ Pending - Just needs the API key value added

---

### 3. Install Python on Build Agents (If Not Already Installed)

**Required For**: Semgrep, Checkov

**Check if installed**:
```powershell
# On your Sandbox Servers Agents
python --version
pip --version
```

**If not installed**:
- **Windows**: Download from https://www.python.org/downloads/ (add to PATH)
- **Linux**: `sudo apt-get install python3 python3-pip` or `sudo yum install python3 python3-pip`

**Alternative**: Use Microsoft-hosted agents (have Python pre-installed) for security jobs:
```yaml
# In Security.yml, change:
pool:
  name: 'Sandbox Servers Agents'

# To:
pool:
  vmImage: 'ubuntu-latest'  # or 'windows-latest'
```

**Status**: ⏳ Pending - Verify Python availability

---

### 4. Configure GitLeaks Allowlist (Optional)

**Why**: Prevent false positives from test data, mock credentials, example configs

**Action**: Create `.gitleaks.toml` in repository root

**Example**:
```toml
title = "Dhadgar GitLeaks Configuration"

[allowlist]
description = "Allowlist for false positives"

# Ignore test files
paths = [
  '''tests/.*''',
  '''.*\.Test\..*''',
]

# Ignore specific patterns
regexes = [
  '''example\.com''',
  '''password.*=.*123456''',  # Test passwords
  '''localhost''',
]

# Ignore specific commits (if needed)
commits = [
  # "abc123def456",  # Add commit SHAs to ignore
]
```

**Status**: ⏳ Optional (can add later when you see false positives)

---

### 5. Enable SARIF Viewing in Azure DevOps (Optional Enhancement)

**Why**: View security findings directly in Azure DevOps UI (instead of just artifacts)

**Action**: Install "SARIF SAST Scans Tab" extension

**Steps**:
1. Visit: https://marketplace.visualstudio.com/items?itemName=sariftools.scans
2. Click "Get it free" → Select your Azure DevOps organization
3. Install extension

**After installation**, add to pipeline:
```yaml
# After each SARIF-generating scan
- task: PublishBuildArtifacts@1
  inputs:
    PathtoPublish: '$(Build.ArtifactStagingDirectory)/semgrep.sarif'
    ArtifactName: 'CodeAnalysisLogs'
    publishLocation: 'Container'

# Results will appear in "Scans" tab on pipeline runs
```

**Status**: ⏳ Optional (nice-to-have UI enhancement)

---

### 6. Test Security Pipeline on Small Changeset

**Action**: Run the security pipeline on a test branch first

**Steps**:
1. Create a test branch: `git checkout -b test/security-scanning`
2. Push the Security.yml template changes
3. Trigger a pipeline run with limited services to test:
   ```yaml
   servicesCsv: 'Gateway'  # Test with just Gateway first
   ```
4. Review artifacts and logs
5. Fix any agent-specific issues (missing tools, permissions, etc.)
6. Expand to full service list once working

**Status**: ⏳ Pending - Test before production use

---

### 7. Configure Failure Behavior (Policy Decision)

**Current Settings** (in Security.yml):
- `failOnCritical: true` - Fail build on CRITICAL severity findings
- `failOnHigh: false` - Continue on HIGH severity findings

**Decision Points**:

| Scenario | Recommendation | Rationale |
|----------|----------------|-----------|
| **Production builds** | `failOnCritical: true` | Block deployment of critical CVEs |
| **PR builds** | `failOnHigh: true` | Catch issues early, before merge |
| **Experimental branches** | `failOnCritical: false` | Allow testing without blocking |

**Action**: Decide your policy and adjust parameters when calling Security.yml

**Example** (different settings per branch):
```yaml
# In main pipeline
- ${{ if eq(variables['Build.SourceBranch'], 'refs/heads/main') }}:
  - template: Templates/Dhadgar.CI/Stages/Security.yml
    parameters:
      failOnCritical: true
      failOnHigh: false

- ${{ if startsWith(variables['Build.SourceBranch'], 'refs/pull/') }}:
  - template: Templates/Dhadgar.CI/Stages/Security.yml
    parameters:
      failOnCritical: true
      failOnHigh: true  # Stricter on PRs
```

**Status**: ⏳ Policy decision needed

---

### 8. Document Security Findings Workflow

**Action**: Create a process for handling security findings

**Recommended Workflow**:

1. **Critical findings** (CVSS 9.0-10.0):
   - Immediate action required
   - Block deployment
   - Create incident ticket
   - Patch within 24-48 hours

2. **High findings** (CVSS 7.0-8.9):
   - Review within 1 week
   - Create backlog ticket
   - Patch in next sprint

3. **Medium/Low findings**:
   - Review quarterly
   - Accept risk or fix opportunistically

**Create a Confluence/Wiki page documenting this workflow**

**Status**: ⏳ Pending - Document process

---

### 9. Schedule Regular Security Scans (Optional)

**Action**: Run security scans on a schedule (not just on commits)

**Example** (add to pipeline):
```yaml
schedules:
- cron: "0 2 * * 0"  # Every Sunday at 2 AM
  displayName: Weekly security scan
  branches:
    include:
    - main
  always: true  # Run even if no code changes
```

**Status**: ⏳ Optional (recommended for compliance)

---

### 10. Enable GitHub Advanced Security (Optional Paid Upgrade)

**Cost**: $49/user/month

**What you get**:
- CodeQL (deeper SAST than Semgrep)
- Dependabot (auto-PRs for vulnerable dependencies)
- Secret scanning with push protection
- Native GitHub PR integration

**Decision**: Evaluate after using free tools for 1-2 months

**Status**: ⏳ Future consideration

---

## 📊 Quick Start Checklist

**Minimum Viable Security Scanning** (can be done in 30 minutes):

- [ ] Wire Security.yml into main pipeline (Action #1)
- [ ] Verify Python installed on build agents (Action #3)
- [ ] Test with single service (Action #6)
- [ ] Review first scan results
- [ ] Adjust failure thresholds (Action #7)

**Enhanced Setup** (adds ~1 hour):

- [ ] Get NVD API key for faster SCA scans (Action #2)
- [ ] Create GitLeaks allowlist for false positives (Action #4)
- [ ] Install SARIF viewer extension (Action #5)
- [ ] Document security workflow (Action #8)

**Long-term Maturity**:

- [ ] Schedule regular scans (Action #9)
- [ ] Evaluate paid tools after 2 months (Action #10)

---

## 🔍 Expected First Run Results

Based on your codebase, expect to see:

### SAST (Semgrep)
- **Likely findings**: 5-15 issues
- **Common patterns**: SQL injection risks, hardcoded secrets patterns, weak crypto usage
- **Action**: Review and fix or mark as false positives

### SCA (OWASP Dependency-Check)
- **Likely findings**: 10-30 NuGet package CVEs
- **Most common**: Transitive dependencies with known CVEs
- **Action**: Update packages or accept risk for non-exploitable paths

### Container Scanning (Trivy)
- **Likely findings**: 20-50 vulnerabilities per image
- **Most common**: Base image OS package CVEs (Debian/Alpine)
- **Action**: Update base image or wait for upstream patches

### IaC (Checkov)
- **Likely findings**: 5-20 Dockerfile/K8s misconfigurations
- **Common patterns**: Running as root, no health checks, missing resource limits
- **Action**: Harden configurations

### Secrets (GitLeaks)
- **Likely findings**: 0-5 (hopefully 0!)
- **If found**: Rotate immediately, add to allowlist if false positive
- **Action**: Never commit real secrets

### SBOM (Syft)
- **Expected**: Clean SBOM generation for all services
- **Use case**: Compliance, supply chain security, vulnerability tracking

---

## 🚨 Troubleshooting

### "Python command not found"
**Solution**: Install Python 3.8+ on build agents or switch to Microsoft-hosted agents

### "Docker load failed"
**Solution**: Ensure Package stage completed successfully and container artifacts exist

### "OWASP scan timeout"
**Solution**: Add NVD API key (Action #2) or increase timeout to 60 minutes

### "Too many false positives"
**Solution**: Create allowlist configs for each tool (GitLeaks: `.gitleaks.toml`, Semgrep: `.semgrepignore`)

### "Scan jobs not running"
**Solution**: Check `dependsOn: Build` and ensure Build stage succeeded

---

## 📚 Additional Resources

- **Security Scanning Tools Reference**: `docs/SECURITY-SCANNING-TOOLS-2026.md`
- **Trivy Documentation**: https://aquasecurity.github.io/trivy/
- **Semgrep Rules**: https://semgrep.dev/explore
- **OWASP Dependency-Check**: https://owasp.org/www-project-dependency-check/
- **GitLeaks Configs**: https://github.com/gitleaks/gitleaks
- **Checkov Policies**: https://www.checkov.io/

---

## 🎯 Success Metrics

After implementation, track:

1. **Scan Coverage**: % of builds that include security scans
2. **Mean Time to Remediate (MTTR)**: Days from finding → fix
3. **False Positive Rate**: % of findings marked as false positives
4. **Vulnerability Backlog**: Open security findings by severity
5. **Compliance**: SBOM generation rate for releases

**Dashboard Idea**: Create Azure DevOps dashboard widget showing security scan trends over time
