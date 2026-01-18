# Free Security Scanning Tools for Azure Pipelines (2026)

**Last Updated**: January 2026
**Scope**: Free/open-source security scanning tools for .NET 10, containers, dependencies, and IaC

This document catalogs free security scanning tools available for Azure Pipelines in 2026, focusing on tools with no-cost tiers, open-source options, and GitHub integration capabilities.

---

## Quick Reference Table

| Tool | Type | Cost | Azure DevOps | GitHub Actions | PR Comments | Best For |
|------|------|------|--------------|----------------|-------------|----------|
| **Trivy** | Container, SAST, IaC | Free | ✅ Official extension | ✅ | ✅ | Container scanning, multi-purpose |
| **CodeQL** | SAST (.NET/C#) | Paid ($49/user/mo) | ✅ via GHAS | ✅ Native | ✅ | Enterprise .NET code analysis |
| **Semgrep** | SAST | Free tier + SaaS | ✅ Extension | ✅ | ✅ | Fast CI/CD SAST, custom rules |
| **OWASP Dependency-Check** | SCA (NuGet) | Free | ✅ Extension | ✅ | Partial | NuGet vulnerability scanning |
| **Snyk** | Container, SCA, SAST | Free tier limited | ✅ Extension | ✅ | ✅ | Multi-purpose (limited free) |
| **Grype** | Container | Free | ⚠️ Manual setup | ✅ | ✅ | Container vulnerability scanning |
| **Syft** | SBOM | Free | ⚠️ Manual setup | ✅ | N/A | SBOM generation |
| **Checkov** | IaC | Free | ✅ via MSDO | ✅ | ✅ | Terraform, K8s, Dockerfile |
| **Terrascan** | IaC | Free | ✅ via MSDO | ✅ | ✅ | IaC policy enforcement |
| **GitLeaks** | Secrets | Free | ✅ Extension | ✅ | ✅ | Secret detection |
| **Microsoft Security DevOps** | Bundled | Free | ✅ Native | ✅ | Partial | Microsoft-curated toolset |
| **SonarCloud** | SAST, Quality | Free for OSS | ✅ Extension | ✅ | ✅ | Code quality + security |

**Legend:**
- ✅ = Officially supported with extensions/actions
- ⚠️ = Requires manual CLI installation
- ❌ = Not supported / Not applicable

---

## 1. Container Image Scanning

### 1.1 Trivy (Aqua Security) - **RECOMMENDED**

**Status**: Free, open-source (Apache 2.0)
**Last Updated**: Actively maintained (2026)

#### Capabilities
- Container image vulnerability scanning (CRITICAL/HIGH severity filtering)
- Filesystem and Git repository scanning
- IaC misconfiguration detection (Dockerfile, K8s, Terraform)
- Secret scanning
- SBOM support (CycloneDX, SPDX)
- License scanning

#### Azure DevOps Integration
- **Official Extension**: [Aqua Trivy Azure DevOps Extension](https://marketplace.visualstudio.com/items?itemName=AquaSecurityOfficial.trivy-official)
- **Manual Installation**: `curl -sfL https://raw.githubusercontent.com/aquasecurity/trivy/main/contrib/install.sh | sh -s -- -b /usr/local/bin`
- **Included in**: Microsoft Security DevOps extension

#### Example Pipeline Task
```yaml
- task: trivy@1
  inputs:
    version: 'latest'
    docker: false
    image: '$(imageName):$(imageTag)'
    exitCode: '1'
    severity: 'CRITICAL,HIGH'
```

#### Output Formats
- Table, JSON, SARIF, JUnit-XML
- Publish SARIF to GitHub Security tab
- Publish JUnit-XML as test results in Azure DevOps

#### Pros
- Fast and accurate (low false positives)
- Multi-purpose (containers, IaC, secrets, SBOM)
- No external SaaS dependencies
- Azure Linux 3 support (v0.81.0+)

#### Cons
- No built-in PR comment integration (requires custom scripting)

#### Resources
- [GitHub Repository](https://github.com/aquasecurity/trivy)
- [Azure DevOps Extension](https://marketplace.visualstudio.com/items?itemName=AquaSecurityOfficial.trivy-official)
- [Container Security Scanning with Trivy and Azure DevOps](https://lgulliver.github.io/container-security-scanning-with-trivy-in-azure-devops/)
- [Build, Scan and Push containers with Azure DevOps](https://lgulliver.github.io/build-scan-and-push-containers/)

---

### 1.2 Grype (Anchore)

**Status**: Free, open-source (Apache 2.0)
**Last Updated**: Actively maintained (2026)

#### Capabilities
- Container image vulnerability scanning
- Filesystem and directory scanning
- SBOM-based vulnerability analysis (pairs with Syft)
- Accurate version matching (minimizes false positives)

#### Azure DevOps Integration
- **Legacy Extension**: [Anchore Container Scan Task](https://marketplace.visualstudio.com/items?itemName=AnchoreInc.anchore-scan-task) (⚠️ Deprecated - use Grype CLI directly)
- **Manual Installation**: Install via CLI in pipeline
- **GitHub Actions**: Native support

#### Example Pipeline Task
```yaml
- script: |
    curl -sSfL https://raw.githubusercontent.com/anchore/grype/main/install.sh | sh -s -- -b /usr/local/bin
    grype $(imageName):$(imageTag) --fail-on high -o json > grype-results.json
  displayName: 'Scan container with Grype'
```

#### Output Formats
- JSON, Table, CycloneDX, SARIF

#### Pros
- Free and fast
- Pairs well with Syft for SBOM-first workflows
- Scriptable for CI/CD
- Azure Linux 3 support (v0.81.0+)

#### Cons
- No official Azure DevOps extension (manual setup required)
- Limited PR comment integration without custom scripting

#### Resources
- [GitHub Repository](https://github.com/anchore/grype)
- [Open Source Container Security](https://anchore.com/opensource/)
- [Guide to SBOM with Syft and Grype](https://www.jit.io/resources/appsec-tools/a-guide-to-generating-sbom-with-syft-and-grype)

---

### 1.3 Snyk Container

**Status**: Free tier (limited), paid plans available
**Free Tier Limits**: 100 tests/month for open-source projects

#### Capabilities
- Container image vulnerability scanning
- Base image recommendations
- Fix pull requests
- License compliance

#### Azure DevOps Integration
- **Official Extension**: [Snyk Security Scan](https://marketplace.visualstudio.com/items?itemName=Snyk.snyk-security-scan)
- **Requires**: Node.js and npm on build agent (available on Microsoft-hosted agents)

#### Example Pipeline Task
```yaml
- task: SnykSecurityScan@1
  inputs:
    serviceConnectionEndpoint: 'SnykAuth'
    testType: 'container'
    dockerImageName: '$(imageName):$(imageTag)'
    failOnIssues: true
    monitorWhen: 'always'
```

#### Pros
- Unified platform (container + code + dependencies)
- Good PR integration
- Automatic fix suggestions

#### Cons
- Free tier heavily limited (100 tests/month)
- Requires external SaaS account (Snyk.io)
- Paid tiers expensive ($25+/user/month)

#### Resources
- [Azure Pipelines Integration Docs](https://docs.snyk.io/developer-tools/snyk-ci-cd-integrations/azure-pipelines-integration)
- [GitHub Repository](https://github.com/snyk/snyk-azure-pipelines-task)
- [Container Image Pipeline Example](https://docs.snyk.io/developer-tools/snyk-ci-cd-integrations/azure-pipelines-integration/example-of-a-snyk-task-for-a-container-image-pipeline)

---

## 2. .NET / C# Code Scanning (SAST)

### 2.1 CodeQL (GitHub Advanced Security for Azure DevOps)

**Status**: Paid - $49/active committer/month
**Free Tier**: None (viewing alerts in Microsoft Defender for Cloud free tier available)

#### Capabilities
- Semantic code analysis for C#, Java, JavaScript, Python, Go, Ruby, Swift, C++
- Deep dataflow and taint analysis
- 500+ security queries
- Custom query support (QL language)
- SARIF output for GitHub Security tab

#### Azure DevOps Integration
- **Native Task**: `AdvancedSecurity-Codeql-Init@1` and `AdvancedSecurity-Codeql-Analyze@1`
- **Requires**: GitHub Advanced Security for Azure DevOps license
- **GitHub Actions**: Native support (free for public repositories)

#### Example Pipeline Task
```yaml
- task: AdvancedSecurity-Codeql-Init@1
  inputs:
    languages: 'csharp'
    buildtype: 'none'  # Use 'none' for C#, Java, C++ (no build required)

- task: AdvancedSecurity-Codeql-Analyze@1
```

#### C# Build Modes
- `none`: Database created directly from codebase (no build) - **RECOMMENDED for C#**
- `manual`: Custom build steps for compiled languages

#### Pros
- Industry-leading SAST for .NET
- Deep semantic analysis (low false positives)
- Native GitHub integration
- Secret scanning included

#### Cons
- **Not free** ($49/user/month for Azure DevOps)
- Requires GitHub Advanced Security license
- Alternative: Use GitHub Actions with public repos (free)

#### Resources
- [Set up code scanning for GitHub Advanced Security](https://learn.microsoft.com/en-us/azure/devops/repos/security/github-advanced-security-code-scanning?view=azure-devops)
- [AdvancedSecurity-Codeql-Analyze@1 Task](https://learn.microsoft.com/en-us/azure/devops/pipelines/tasks/reference/advanced-security-codeql-analyze-v1?view=azure-pipelines)
- [Billing for GitHub Advanced Security](https://learn.microsoft.com/en-us/azure/devops/repos/security/github-advanced-security-billing?view=azure-devops)

---

### 2.2 Semgrep - **RECOMMENDED for FREE SAST**

**Status**: Free tier (OSS rules), paid plans for proprietary rules
**Free Tier**: Unlimited scans with community rules

#### Capabilities
- Fast, lightweight SAST for 30+ languages (C#, Java, JavaScript, Python, Go, etc.)
- Pattern-based static analysis
- Custom rule authoring (YAML)
- Low false-positive rate
- IDE, CLI, and CI/CD support

#### Azure DevOps Integration
- **Official Extension**: [Semgrep](https://marketplace.visualstudio.com/items?itemName=Semgrep.semgrep)
- **Managed Scans**: Add Azure DevOps repos in bulk without CI changes
- **Self-Hosted Runners**: Supported with `SEMGREP_APP_TOKEN` variable

#### Example Pipeline Task
```yaml
- task: UseDotNet@2
  inputs:
    packageType: 'sdk'
    version: '10.x'

- script: |
    pip install semgrep
    semgrep scan --config=auto --sarif -o semgrep.sarif
  displayName: 'Semgrep SAST scan'

- task: PublishBuildArtifacts@1
  inputs:
    pathToPublish: 'semgrep.sarif'
    artifactName: 'CodeAnalysisLogs'
```

#### PR Comments
- ✅ Inline comments for individual findings
- ✅ Grouped findings for multiple issues
- ✅ Supply Chain license violations
- ✅ Finding hyperlinks (commit URL, branch URL, line of code URL)

#### Pros
- **Free and fast** (great for CI/CD)
- Developer-friendly (custom rules in YAML)
- Strong Azure DevOps integration (managed scans, PR comments)
- No external SaaS dependency for CLI usage

#### Cons
- Limited to pattern-based analysis (not dataflow/taint tracking like CodeQL)
- Advanced features require paid tier

#### Resources
- [Azure DevOps Documentation](https://semgrep.dev/docs/deployment/managed-scanning/azure)
- [Bringing Semgrep to Azure DevOps](https://semgrep.dev/blog/2024/bringing-more-semgrep-capabilities-to-bitbucket-and-azure-devops/)
- [Self-Hosted Ubuntu Runners](https://semgrep.dev/docs/kb/semgrep-ci/azure-self-hosted-ubuntu)

---

### 2.3 SonarCloud - **RECOMMENDED for CODE QUALITY + SECURITY**

**Status**: Free for open-source projects, paid for private repos
**Free Tier**: Unlimited analysis for public repositories

#### Capabilities
- SAST + code quality linting
- Security hotspots and vulnerability detection
- Code smells, bugs, technical debt tracking
- 30+ language support (C#, Java, JavaScript, Python, etc.)
- Pull request decoration with inline comments

#### Azure DevOps Integration
- **Official Extension**: [SonarCloud](https://marketplace.visualstudio.com/items?itemName=SonarSource.sonarcloud)
- **Deep Integration**: GitHub, GitLab, Azure DevOps, Bitbucket

#### Example Pipeline Task
```yaml
- task: SonarCloudPrepare@1
  inputs:
    SonarCloud: 'SonarCloud-Connection'
    organization: 'your-org'
    scannerMode: 'MSBuild'
    projectKey: 'your-project-key'
    projectName: 'Dhadgar'

- task: DotNetCoreCLI@2
  inputs:
    command: 'build'
    projects: '**/*.csproj'

- task: SonarCloudAnalyze@1

- task: SonarCloudPublish@1
  inputs:
    pollingTimeoutSec: '300'
```

#### Pros
- **Free for OSS projects**
- Unified code quality + security analysis
- Excellent PR integration (inline comments, quality gates)
- Supports .NET 10 and all major languages

#### Cons
- Requires SonarCloud SaaS account
- Paid for private repositories (starts ~$10/month)
- Slower than lightweight SAST tools (Semgrep)

#### Resources
- [SonarCloud](https://www.sonarcloud.io/)
- [Azure DevOps Integration](https://docs.sonarsource.com/sonarcloud/getting-started/azure-devops/)

---

### 2.4 Microsoft Security DevOps (MSDO) Extension - **BUNDLED OPTION**

**Status**: Free (Microsoft-provided)

#### Capabilities
- Bundles multiple open-source security tools
- Included tools: Bandit (Python), BinSkim (.NET), Checkov (IaC), ESLint (JS), Template Analyzer (ARM), Terrascan (IaC), Trivy
- SARIF output for unified results

#### Azure DevOps Integration
- **Official Extension**: [Microsoft Security DevOps](https://marketplace.visualstudio.com/items?itemName=ms-securitydevops.microsoft-security-devops-azdevops)
- **Requires**: Project Collection Administrator privileges to install

#### Example Pipeline Task
```yaml
- task: MicrosoftSecurityDevOps@1
  inputs:
    categories: 'code,artifacts,IaC,containers'
```

#### Pros
- **Free and Microsoft-supported**
- Bundles multiple tools (no separate installs)
- SARIF output for GitHub Security tab
- Good for standardized security baseline

#### Cons
- Less flexible than individual tool configurations
- Limited customization per tool
- Not all tools are best-in-class

#### Resources
- [Configure Microsoft Security DevOps](https://learn.microsoft.com/en-us/azure/defender-for-cloud/azure-devops-extension)
- [Visual Studio Marketplace](https://marketplace.visualstudio.com/items?itemName=ms-securitydevops.microsoft-security-devops-azdevops)

---

## 3. NuGet / Dependency Scanning (SCA)

### 3.1 OWASP Dependency-Check - **RECOMMENDED for FREE SCA**

**Status**: Free, open-source
**Last Updated**: Requires .NET 8 runtime (2026)

#### Capabilities
- NuGet package vulnerability scanning (NuSpec, packages.config)
- Checks against National Vulnerability Database (NVD)
- Supports Maven, Gradle, npm, Python, Ruby, etc.
- SARIF, JSON, HTML, XML output

#### Azure DevOps Integration
- **Official Extension**: [OWASP Dependency Check](https://marketplace.visualstudio.com/items?itemName=dependency-check.dependencycheck)
- **Requires**: .NET 8 runtime on build agent

#### Example Pipeline Task
```yaml
- task: UseDotNet@2
  inputs:
    packageType: 'sdk'
    version: '8.x'

- task: dependency-check-build-task@6
  inputs:
    projectName: 'Dhadgar'
    scanPath: '**/*.csproj'
    format: 'ALL'
    failOnCVSS: '7'
    nvdApiKey: '$(NVD_API_KEY)'  # RECOMMENDED: Speeds up scans (15+ min → 2-3 min)
```

#### Performance Optimization
- **NVD API Key**: Free API key from [National Vulnerability Database](https://nvd.nist.gov/developers/request-an-api-key)
- Reduces scan time from 15+ minutes to 2-3 minutes

#### Pros
- **100% free and open-source**
- No external SaaS dependencies
- Supports .NET projects (NuSpec, packages.config)
- Works on Windows and Linux agents

#### Cons
- Slow without NVD API key
- Limited PR comment integration
- Requires .NET 8 runtime

#### Resources
- [OWASP Dependency-Check](https://owasp.org/www-project-dependency-check/)
- [Azure DevOps Extension](https://marketplace.visualstudio.com/items?itemName=dependency-check.dependencycheck)
- [GitHub Repository](https://github.com/dependency-check/azuredevops)
- [Secure .NET by scanning vulnerable NuGet dependencies](https://ilovedotnet.org/blogs/owasp-secure-your-dotnet-app-by-scanning-for-vulnerable-nuget-dependency-in-ci-pipelines/)

---

### 3.2 Snyk Open Source

**Status**: Free tier (limited), paid plans available
**Free Tier Limits**: 200 tests/month for open-source projects

#### Capabilities
- NuGet, npm, Maven, pip, etc. vulnerability scanning
- Automated fix pull requests
- License compliance checks
- Developer-friendly reporting

#### Azure DevOps Integration
- **Official Extension**: [Snyk Security Scan](https://marketplace.visualstudio.com/items?itemName=Snyk.snyk-security-scan)

#### Example Pipeline Task
```yaml
- task: SnykSecurityScan@1
  inputs:
    serviceConnectionEndpoint: 'SnykAuth'
    testType: 'app'
    monitorWhen: 'always'
    failOnIssues: true
```

#### Pros
- Good PR integration with fix suggestions
- Unified platform (container + code + dependencies)

#### Cons
- Free tier limited (200 tests/month)
- Requires external SaaS account

#### Resources
- [Snyk Azure Pipelines Integration](https://docs.snyk.io/developer-tools/snyk-ci-cd-integrations/azure-pipelines-integration)

---

### 3.3 Trivy (Filesystem Mode)

**Status**: Free, open-source
**Note**: Trivy can also scan filesystems for dependency vulnerabilities

#### Example Pipeline Task
```yaml
- task: trivy@1
  inputs:
    path: '$(Build.SourcesDirectory)'
    scanType: 'fs'
    severity: 'CRITICAL,HIGH'
```

#### Pros
- Same tool for containers + dependencies (consolidation)
- Fast and accurate

#### Cons
- Less detailed than OWASP Dependency-Check for NuGet-specific analysis

---

## 4. Secrets Scanning

### 4.1 GitLeaks - **RECOMMENDED**

**Status**: Free, open-source
**Last Updated**: Actively maintained (2024+)

#### Capabilities
- Pattern-based secret detection (passwords, API keys, tokens, SSH keys)
- Pre-commit hooks, CI/CD integration
- Customizable rules (TOML configuration)
- Multiple scan modes: all commits, prevalidation (PR only), changes (incremental), smart (auto-detect)

#### Azure DevOps Integration
- **Community Extension**: [Gitleaks](https://marketplace.visualstudio.com/items?itemName=Foxholenl.Gitleaks)
- **Alternative**: Manual CLI installation

#### Example Pipeline Task
```yaml
- task: Gitleaks@2
  inputs:
    scanmode: 'prevalidation'  # Scan only PR commits
    reportformat: 'sarif'
    uploadresults: true
    failOnAlert: true
```

#### Features
- Default SARIF output (displayed in pipeline summaries)
- Results uploaded as artifacts to Azure DevOps
- Integration with Application Insights for monitoring

#### Pros
- **Free and open-source**
- Fast and accurate pattern matching
- No external SaaS dependencies
- Pre-commit hook support

#### Cons
- Pattern-based (may have false positives for complex secrets)
- Limited PR comment integration without custom scripting

#### Resources
- [Monitor git secrets on Azure DevOps with Gitleaks](https://techcommunity.microsoft.com/blog/azuredevcommunityblog/monitor-git-secrets-on-azure-devops-with-gitleaks/3998673)
- [Automatically Detect and Prevent Secrets](https://www.thelazyadministrator.com/2024/12/09/automatically-detect-and-prevent-secrets-leaked-into-code-within-azure-devops/)
- [GitHub Repository](https://github.com/JoostVoskuil/azure-devops-gitleaks)

---

### 4.2 GitHub Advanced Security Secret Scanning

**Status**: Paid ($49/user/month) - Included with GHAS for Azure DevOps

#### Capabilities
- Partner patterns (validated secrets from 200+ providers)
- Custom patterns (regex-based)
- Push protection (block commits with secrets)
- Alert notifications and remediation workflows

#### Pros
- Most accurate (partner-validated patterns)
- Push protection (prevents commits)

#### Cons
- **Not free** (requires GHAS license)

---

### 4.3 Trivy (Secrets Mode)

**Status**: Free, open-source

#### Example Pipeline Task
```yaml
- task: trivy@1
  inputs:
    scanType: 'config'
    path: '$(Build.SourcesDirectory)'
```

#### Pros
- Consolidates secrets scanning with container/IaC scans

#### Cons
- Less mature than GitLeaks for secrets-only scanning

---

## 5. Infrastructure as Code (IaC) Scanning

### 5.1 Checkov - **RECOMMENDED**

**Status**: Free, open-source (Apache 2.0)
**Maintained by**: Bridgecrew (Palo Alto Networks)

#### Capabilities
- 750+ pre-defined checks for misconfigurations
- Supports: Terraform, CloudFormation, Kubernetes YAML/JSON, Helm, Kustomize, Dockerfile, Bicep, ARM Templates, OpenTofu
- Graph-based scanning (detects dependency issues)
- CIS, PCI-DSS, SOC 2 compliance frameworks
- License scanning for open-source packages

#### Azure DevOps Integration
- **Included in**: Microsoft Security DevOps extension
- **Manual Installation**: `pip install checkov`

#### Example Pipeline Task
```yaml
- script: |
    pip install checkov
    checkov -d $(Build.SourcesDirectory) --framework kubernetes terraform dockerfile --output junitxml --soft-fail
  displayName: 'Checkov IaC scan'

- task: PublishTestResults@2
  inputs:
    testResultsFormat: 'JUnit'
    testResultsFiles: '**/results_junitxml.xml'
    testRunTitle: 'Checkov IaC Results'
```

#### Pros
- **Free and open-source**
- Comprehensive rule coverage (750+ checks)
- Multi-framework support (K8s, Terraform, Docker, Helm, etc.)
- Graph-based analysis (catches deeper issues)
- Integrates with GitHub, GitLab, Azure DevOps

#### Cons
- Can be noisy (many rules enabled by default)
- Requires tuning/configuration for false positives

#### Resources
- [Checkov Documentation](https://www.checkov.io/)
- [GitHub Repository](https://github.com/bridgecrewio/checkov)
- [Ensuring IaC Security with Checkov](https://medium.com/@williamwarley/ensuring-iac-security-with-checkov-a-practical-integration-guide-for-azure-devops-gitlab-and-cc8bcfa3d3e9)
- [Azure DevOps Terraform Pipeline with Checkov](https://sahayagodson.medium.com/azure-devops-terraform-pipeline-with-checkov-e5faf225e001)

---

### 5.2 Terrascan

**Status**: Free, open-source
**Maintained by**: Tenable

#### Capabilities
- IaC policy-as-code engine
- Supports: Terraform, CloudFormation, Kubernetes, Helm, Kustomize, Docker, ARM Templates
- 500+ policies for security and compliance
- Custom policy support (Rego language)

#### Azure DevOps Integration
- **Included in**: Microsoft Security DevOps extension
- **Manual Installation**: Script-based installation

#### Example Pipeline Task
```yaml
- script: |
    curl -L https://github.com/tenable/terrascan/releases/download/v1.18.0/terrascan_1.18.0_Linux_x86_64.tar.gz -o terrascan.tar.gz
    tar -xzf terrascan.tar.gz
    ./terrascan scan -i k8s -d $(Build.SourcesDirectory)/deploy/kubernetes -o sarif > terrascan.sarif
  displayName: 'Terrascan IaC scan'
```

#### Kubernetes YAML Scanning
- Use `-i k8s` flag to scan Kubernetes manifests
- Auto-detects file types and applies policies

#### Pros
- **Free and open-source**
- Policy-as-code (custom Rego rules)
- Multiple output formats (YAML, JSON, SARIF, JUnit-XML)

#### Cons
- No official Azure DevOps marketplace extension (manual setup)
- Steeper learning curve for custom policies

#### Resources
- [Terrascan Documentation](https://runterrascan.io/docs/)
- [Using Terrascan with Azure DevOps](https://lgulliver.github.io/terrascan-in-azure-devops/)
- [GitHub Repository - Azure DevOps Template](https://github.com/MMerzinger/terrascan-azure-devops-pipeline)

---

### 5.3 Trivy (IaC Mode)

**Status**: Free, open-source

#### Capabilities
- Dockerfile, K8s, Terraform, CloudFormation misconfiguration detection
- Same tool as container scanning (consolidation)

#### Example Pipeline Task
```yaml
- task: trivy@1
  inputs:
    scanType: 'config'
    path: '$(Build.SourcesDirectory)/deploy'
```

#### Pros
- Consolidates IaC scanning with container scans

#### Cons
- Fewer checks than Checkov/Terrascan

---

## 6. SBOM Generation

### 6.1 Syft - **RECOMMENDED**

**Status**: Free, open-source (Apache 2.0)
**Maintained by**: Anchore

#### Capabilities
- SBOM generation from container images, filesystems, and source code
- Output formats: SPDX, CycloneDX, JSON, Table
- Deep package analysis (OS packages, language-specific packages, binaries)
- Pairs with Grype for SBOM-based vulnerability scanning

#### Azure DevOps Integration
- **Manual Installation**: CLI-based installation

#### Example Pipeline Task
```yaml
- script: |
    curl -sSfL https://raw.githubusercontent.com/anchore/syft/main/install.sh | sh -s -- -b /usr/local/bin
    syft $(imageName):$(imageTag) -o spdx-json > sbom.spdx.json
  displayName: 'Generate SBOM with Syft'

- task: PublishBuildArtifacts@1
  inputs:
    pathToPublish: 'sbom.spdx.json'
    artifactName: 'SBOM'
```

#### Pros
- **Free and fast**
- Pairs with Grype for SBOM-first security workflows
- Multiple output formats (SPDX, CycloneDX)

#### Cons
- No official Azure DevOps extension (manual setup)

#### Resources
- [GitHub Repository](https://github.com/anchore/syft)
- [How to Generate SBOM with Azure DevOps Pipeline](https://medium.com/@clouddefenseai/how-to-generate-sbom-with-azure-devops-pipeline-f0ecc52bc686)
- [SBOM Generation Tools Guide](https://www.cybeats.com/blog/top-7-sbom-generation-tools-and-how-to-choose)

---

### 6.2 Microsoft SBOM Tool

**Status**: Free, open-source
**Maintained by**: Microsoft

#### Capabilities
- SBOM generation for .NET, npm, Maven, Python, etc.
- SPDX format
- Azure DevOps integration

#### Example Pipeline Task
```yaml
- task: ManifestGeneratorTask@0
  inputs:
    BuildDropPath: '$(Build.ArtifactStagingDirectory)'
```

#### Pros
- Microsoft-supported
- Native Azure DevOps integration

#### Cons
- Limited to SPDX format
- Less flexible than Syft

---

## 7. Recommended Tool Combinations

### 7.1 Minimal Free Stack (No SaaS Dependencies)
- **Container Scanning**: Trivy
- **SAST (.NET)**: Semgrep (free tier with community rules)
- **SCA (NuGet)**: OWASP Dependency-Check
- **Secrets**: GitLeaks
- **IaC**: Checkov
- **SBOM**: Syft

**Total Cost**: $0
**External Dependencies**: None (all run in CI/CD)

---

### 7.2 Comprehensive Free Stack (SaaS-Assisted)
- **Container Scanning**: Trivy
- **SAST (.NET)**: SonarCloud (free for OSS) or Semgrep (free tier)
- **SCA (NuGet)**: Snyk Open Source (free tier, 200 tests/month)
- **Secrets**: GitLeaks
- **IaC**: Checkov
- **SBOM**: Syft
- **Code Quality**: SonarCloud

**Total Cost**: $0 (with limits)
**External Dependencies**: SonarCloud, Snyk accounts

---

### 7.3 Enterprise Stack (Paid)
- **Container Scanning**: Trivy or Snyk Container
- **SAST (.NET)**: CodeQL (GitHub Advanced Security for Azure DevOps)
- **SCA (NuGet)**: Snyk Open Source or GitHub Dependency Scanning
- **Secrets**: GitHub Advanced Security Secret Scanning
- **IaC**: Checkov or Snyk IaC
- **SBOM**: Syft
- **Observability**: Microsoft Defender for Cloud (unified alerts)

**Total Cost**: $49/user/month (GHAS for Azure DevOps)
**External Dependencies**: GitHub Advanced Security license

---

## 8. GitHub Integration for PR Comments

Most tools support GitHub Actions with PR comment capabilities. To integrate Azure DevOps with GitHub for PR comments:

### 8.1 Bridge Pattern (Azure Pipelines → GitHub Actions)
1. Azure Pipelines triggers GitHub Actions workflow via webhook
2. GitHub Actions runs security scans (CodeQL, Trivy, Semgrep, etc.)
3. GitHub Actions posts PR comments using native integrations

### 8.2 Custom PR Comment Script (Azure DevOps Native)
Use Azure DevOps REST API to post comments on PRs:

```yaml
- script: |
    # Parse SARIF results
    COMMENT_BODY=$(python3 scripts/parse-sarif.py trivy.sarif)

    # Post comment to Azure DevOps PR
    az repos pr comment create \
      --pr $(System.PullRequest.PullRequestId) \
      --repository $(Build.Repository.Name) \
      --comment "$COMMENT_BODY" \
      --org $(System.CollectionUri) \
      --project $(System.TeamProject)
  displayName: 'Post scan results as PR comment'
  env:
    AZURE_DEVOPS_EXT_PAT: $(System.AccessToken)
```

### 8.3 Tools with Native Azure DevOps PR Comments
- ✅ **Semgrep**: Native PR comment support (inline + grouped findings)
- ✅ **SonarCloud**: Pull request decoration with inline comments
- ✅ **Snyk**: PR comments with fix suggestions
- ✅ **Checkov**: PR comments (via Bridgecrew platform)

---

## 9. Azure Pipelines Free Tier Limits

**Free Tier (Per Organization)**:
- 1 parallel job (Microsoft-hosted agent)
- 1,800 minutes/month (30 hours)
- 1 free self-hosted agent (unlimited minutes)

**Open Source Projects**:
- Free starter plans (unlimited minutes with Microsoft-hosted agents)

**Note**: Security scans can be time-consuming. Consider self-hosted agents for heavy scanning workloads.

---

## 10. Implementation Priorities for Dhadgar/Meridian Console

### Phase 1: Immediate (Free, No SaaS)
1. **Container Scanning**: Add Trivy to `azure-pipelines-containers.yml`
2. **Secrets Scanning**: Add GitLeaks to all repos
3. **IaC Scanning**: Add Checkov for Kubernetes manifests and Dockerfiles

### Phase 2: Code Analysis (Free/Limited SaaS)
4. **SAST**: Add Semgrep (free tier) for .NET 10 code analysis
5. **SCA**: Add OWASP Dependency-Check for NuGet packages
6. **SBOM**: Generate SBOMs with Syft for container images

### Phase 3: Quality Gates (Optional SaaS)
7. **Code Quality**: Add SonarCloud (free for open-source, paid for private repos)
8. **Unified Dashboard**: Export SARIF results to GitHub Security tab (if using GitHub)

### Phase 4: Enterprise (Paid)
9. **Deep SAST**: Upgrade to CodeQL with GitHub Advanced Security for Azure DevOps ($49/user/month)
10. **Unified Alerts**: Enable Microsoft Defender for Cloud for centralized security insights

---

## 11. Sample Azure Pipeline Integration

```yaml
# azure-pipelines-security.yml
trigger:
  branches:
    include:
      - main
      - develop

pool:
  vmImage: 'ubuntu-latest'

stages:
  - stage: SecurityScans
    displayName: 'Security Scanning'
    jobs:
      - job: ContainerScan
        displayName: 'Container Image Scan'
        steps:
          - task: Docker@2
            inputs:
              command: 'build'
              repository: 'dhadgar/gateway'
              tags: '$(Build.BuildId)'

          - task: trivy@1
            displayName: 'Trivy Container Scan'
            inputs:
              image: 'dhadgar/gateway:$(Build.BuildId)'
              exitCode: '1'
              severity: 'CRITICAL,HIGH'

      - job: CodeScan
        displayName: '.NET Code Scan (SAST)'
        steps:
          - task: UseDotNet@2
            inputs:
              packageType: 'sdk'
              version: '10.x'

          - script: |
              pip install semgrep
              semgrep scan --config=auto --sarif -o semgrep.sarif
            displayName: 'Semgrep SAST'

          - task: PublishBuildArtifacts@1
            inputs:
              pathToPublish: 'semgrep.sarif'
              artifactName: 'SAST-Results'

      - job: DependencyScan
        displayName: 'NuGet Dependency Scan (SCA)'
        steps:
          - task: UseDotNet@2
            inputs:
              packageType: 'sdk'
              version: '8.x'

          - task: dependency-check-build-task@6
            displayName: 'OWASP Dependency-Check'
            inputs:
              projectName: 'Dhadgar'
              scanPath: '**/*.csproj'
              format: 'SARIF'
              failOnCVSS: '7'
              nvdApiKey: '$(NVD_API_KEY)'

      - job: SecretsScan
        displayName: 'Secrets Scan'
        steps:
          - task: Gitleaks@2
            displayName: 'GitLeaks Scan'
            inputs:
              scanmode: 'all'
              reportformat: 'sarif'
              failOnAlert: true

      - job: IaCScan
        displayName: 'IaC Scan (Kubernetes, Dockerfile)'
        steps:
          - script: |
              pip install checkov
              checkov -d deploy/ --framework kubernetes terraform dockerfile --output sarif -o checkov.sarif
            displayName: 'Checkov IaC Scan'

          - task: PublishBuildArtifacts@1
            inputs:
              pathToPublish: 'checkov.sarif'
              artifactName: 'IaC-Results'

      - job: SBOMGeneration
        displayName: 'SBOM Generation'
        steps:
          - script: |
              curl -sSfL https://raw.githubusercontent.com/anchore/syft/main/install.sh | sh -s -- -b /usr/local/bin
              syft dir:. -o spdx-json > sbom.spdx.json
            displayName: 'Generate SBOM'

          - task: PublishBuildArtifacts@1
            inputs:
              pathToPublish: 'sbom.spdx.json'
              artifactName: 'SBOM'
```

---

## 12. Key Takeaways

1. **Best Free Container Scanner**: **Trivy** (multi-purpose, fast, accurate)
2. **Best Free SAST for .NET**: **Semgrep** (fast, customizable) or **SonarCloud** (if OSS project)
3. **Best Free SCA for NuGet**: **OWASP Dependency-Check** (fully offline, NVD integration)
4. **Best Free Secrets Scanner**: **GitLeaks** (pattern-based, CI/CD ready)
5. **Best Free IaC Scanner**: **Checkov** (750+ rules, graph-based analysis)
6. **Best Free SBOM Tool**: **Syft** (pairs with Grype, multiple formats)

**Enterprise Alternative**: GitHub Advanced Security for Azure DevOps ($49/user/month) includes CodeQL, Dependabot, and secret scanning with native PR integration.

**Microsoft-Curated Bundle**: Microsoft Security DevOps extension (free) bundles Trivy, Checkov, Terrascan, and BinSkim for standardized baseline.

---

## Sources

### Container Scanning
- [Container Security Scanning with Trivy and Azure DevOps](https://lgulliver.github.io/container-security-scanning-with-trivy-in-azure-devops/)
- [GitHub - aquasecurity/trivy-azure-pipelines-task](https://github.com/aquasecurity/trivy-azure-pipelines-task)
- [Aqua Trivy Azure DevOps Extension](https://marketplace.visualstudio.com/items?itemName=AquaSecurityOfficial.trivy-official)
- [GitHub - anchore/grype](https://github.com/anchore/grype)
- [Open Source Container Security with Syft & Grype](https://anchore.com/opensource/)
- [Grype Support for Azure Linux 3](https://anchore.com/blog/grype-support-for-azure-linux-3-released/)

### .NET / C# SAST
- [Set up code scanning for GitHub Advanced Security for Azure DevOps](https://learn.microsoft.com/en-us/azure/devops/repos/security/github-advanced-security-code-scanning?view=azure-devops)
- [AdvancedSecurity-Codeql-Analyze@1 Task](https://learn.microsoft.com/en-us/azure/devops/pipelines/tasks/reference/advanced-security-codeql-analyze-v1?view=azure-pipelines)
- [Azure DevOps | Semgrep](https://semgrep.dev/docs/deployment/managed-scanning/azure)
- [Bringing more Semgrep capabilities to BitBucket and Azure DevOps](https://semgrep.dev/blog/2024/bringing-more-semgrep-capabilities-to-bitbucket-and-azure-devops/)
- [Top 10 SAST Tools in 2025](https://www.ox.security/blog/static-application-security-sast-tools/)
- [Top 13 Enterprise SAST Tools for 2026](https://cycode.com/blog/top-13-enterprise-sast-tools-for-2026/)

### NuGet / SCA
- [OWASP Dependency Check - Visual Studio Marketplace](https://marketplace.visualstudio.com/items?itemName=dependency-check.dependencycheck)
- [OWASP Dependency-Check | OWASP Foundation](https://owasp.org/www-project-dependency-check/)
- [Fortifying your .NET Projects by Integrating Vulnerability Detection](https://itnext.io/fortifying-your-net-projects-integrating-vulnerability-detection-in-azure-devops-67fa1d60b7b4)
- [OWASP - Secure your dotnet app](https://ilovedotnet.org/blogs/owasp-secure-your-dotnet-app-by-scanning-for-vulnerable-nuget-dependency-in-ci-pipelines/)
- [Snyk Azure Pipelines integration](https://docs.snyk.io/developer-tools/snyk-ci-cd-integrations/azure-pipelines-integration)

### Secrets Scanning
- [Monitor git secrets on Azure DevOps with Gitleaks](https://techcommunity.microsoft.com/blog/azuredevcommunityblog/monitor-git-secrets-on-azure-devops-with-gitleaks/3998673)
- [Automatically Detect and Prevent Secrets Leaked into Code](https://www.thelazyadministrator.com/2024/12/09/automatically-detect-and-prevent-secrets-leaked-into-code-within-azure-devops/)
- [Gitleaks - Visual Studio Marketplace](https://marketplace.visualstudio.com/items?itemName=Foxholenl.Gitleaks)
- [GitHub - JoostVoskuil/azure-devops-gitleaks](https://github.com/JoostVoskuil/azure-devops-gitleaks)

### IaC Scanning
- [Ensuring IaC Security with Checkov](https://medium.com/@williamwarley/ensuring-iac-security-with-checkov-a-practical-integration-guide-for-azure-devops-gitlab-and-cc8bcfa3d3e9)
- [GitHub - bridgecrewio/checkov](https://github.com/bridgecrewio/checkov)
- [What is Checkov? Features, Use Cases & Examples](https://spacelift.io/blog/what-is-checkov)
- [Using Terrascan with Azure DevOps](https://lgulliver.github.io/terrascan-in-azure-devops/)
- [What is Terrascan? Features, Use Cases & Custom Policies](https://spacelift.io/blog/what-is-terrascan)
- [Top 7 Terraform Scanning Tools You Should Know in 2026](https://spacelift.io/blog/terraform-scanning-tools)

### SBOM
- [GitHub - anchore/syft](https://github.com/anchore/syft)
- [How to Generate SBOM with Azure DevOps Pipeline](https://medium.com/@clouddefenseai/how-to-generate-sbom-with-azure-devops-pipeline-f0ecc52bc686)
- [A Guide to Generating SBOM with Syft and Grype](https://www.jit.io/resources/appsec-tools/a-guide-to-generating-sbom-with-syft-and-grype)
- [SBOM Generation Tools & Guide](https://anchore.com/sbom/how-to-generate-an-sbom-with-free-open-source-tools/)

### Microsoft Security DevOps
- [Configure the Microsoft Security DevOps Azure DevOps extension](https://learn.microsoft.com/en-us/azure/defender-for-cloud/azure-devops-extension)
- [Microsoft Security DevOps - Visual Studio Marketplace](https://marketplace.visualstudio.com/items?itemName=ms-securitydevops.microsoft-security-devops-azdevops)

### GitHub Advanced Security
- [Billing for GitHub Advanced Security for Azure DevOps](https://learn.microsoft.com/en-us/azure/devops/repos/security/github-advanced-security-billing?view=azure-devops)
- [GitHub Advanced Security for Azure DevOps](https://azure.microsoft.com/en-us/products/devops/github-advanced-security)
- [Announcing general availability of GitHub Advanced Security for Azure DevOps](https://github.blog/news-insights/product-news/announcing-general-availability-of-github-advanced-security-for-azure-devops/)

### GitHub Actions & PR Comments
- [Using GitHub Actions for automated security scans](https://graphite.com/guides/using-github-actions-for-automated-security-scans)
- [Create a security scan GitHub workflow - .NET](https://learn.microsoft.com/en-us/dotnet/devops/dotnet-secure-github-action)
- [Best GitHub Security Tools for Secure Repositories](https://www.aikido.dev/blog/top-github-security-tools)
- [Enhance Code Security with GitHub Actions: Automatically Commenting PRs](https://dev.to/suzuki0430/enhance-code-security-with-github-actions-automatically-commenting-prs-with-docker-scans-48ap)

---

**Document Version**: 1.0
**Last Updated**: January 15, 2026
**Maintained By**: Meridian Console Security Team
