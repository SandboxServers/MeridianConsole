# Council of Greybeards

The **Council of Greybeards** is a unique feature of Dhadgar.CodeReview that provides comprehensive, multi-domain expert review of every pull request.

## Overview

Instead of a single general code review, every PR is evaluated by **15 specialized domain expert agents**, each with deep expertise in their respective areas. This ensures no aspect of the Meridian Console platform is overlooked.

## How It Works

### 1. Agent Discovery
The service automatically discovers all agents defined in `.claude/agents/*.md` at startup.

### 2. Consultation Process
For each PR review:
1. The general LLM performs an initial code review
2. **Simultaneously**, each council member is consulted with:
   - Their specialized system prompt (from their .md file)
   - The full PR context (title, description, diffs)
   - Instructions to determine relevance and provide expert feedback

### 3. Agent Response Format
Each agent responds in one of two ways:

**Not Relevant**:
```json
{
  "relevant": false,
  "reason": "No database schema changes in this PR"
}
```

**Relevant with Feedback**:
```json
{
  "relevant": true,
  "summary": "Overall assessment from expert perspective",
  "comments": [
    {
      "path": "src/Example.cs",
      "line": 42,
      "severity": "critical|high|medium|low|info",
      "body": "Expert opinion on this specific issue"
    }
  ]
}
```

### 4. Result Merging
All council opinions are merged into a single comprehensive review:
- **Summary**: Includes general review + relevant expert opinions
- **Comments**: Combined from all agents, tagged with agent name and severity
- **Transparency**: Lists all consulted agents, even those with "no comment"

## The Council Members

### 🛡️ Security Architect
**Expertise**: Authentication, authorization, encryption, secure coding, threat modeling, OWASP top 10
**Watches For**: Hardcoded secrets, SQL injection, XSS, authentication bypasses, insecure crypto
**Model**: opus (most thorough for security-critical analysis)

### 🗄️ Database Schema Architect
**Expertise**: Schema design, EF Core migrations, relationships, normalization, indexing strategies
**Watches For**: Missing foreign keys, poor normalization, migration risks, schema evolution issues
**Model**: sonnet

### 🔧 Database Admin
**Expertise**: PostgreSQL tuning, connection pooling, query optimization, backup/recovery, performance
**Watches For**: N+1 queries, missing indexes, connection leaks, inefficient queries
**Model**: sonnet

### 📨 Messaging Engineer
**Expertise**: RabbitMQ, MassTransit, message contracts, sagas, retry policies, DLQs
**Watches For**: Message contract changes, missing retry logic, saga state management issues
**Model**: sonnet

### 🏗️ Microservices Architect
**Expertise**: Service boundaries, inter-service communication, distributed patterns, eventual consistency
**Watches For**: Service coupling, improper cross-service calls, distributed transaction anti-patterns
**Model**: opus

### 🌐 REST API Engineer
**Expertise**: RESTful design, HTTP semantics, status codes, versioning, pagination, error handling
**Watches For**: Non-RESTful endpoints, incorrect status codes, inconsistent API patterns
**Model**: sonnet

### 💻 Blazor WebDev Expert
**Expertise**: Blazor WASM/Server, MudBlazor, component patterns, responsive design, client-side state
**Watches For**: Component lifecycle issues, state management problems, UI/UX concerns
**Model**: sonnet

### 🔬 DotNet 10 Researcher
**Expertise**: Latest .NET 10 features, performance patterns, new APIs, security best practices
**Watches For**: Missed opportunities to use new features, deprecated API usage, performance anti-patterns
**Model**: sonnet

### 🧪 DotNet Test Engineer
**Expertise**: xUnit, WebApplicationFactory, mocking, integration tests, test strategies
**Watches For**: Missing test coverage, flaky tests, improper mocking, test anti-patterns
**Model**: sonnet

### 🔐 IAM Architect
**Expertise**: Identity, RBAC, JWT, OAuth/OIDC, passwordless auth, multi-tenant isolation
**Watches For**: Authorization bypasses, weak token validation, role design issues
**Model**: opus

### 🚀 Azure Pipelines Architect
**Expertise**: CI/CD, Azure Pipelines YAML, deployment automation, pipeline versioning
**Watches For**: Pipeline configuration errors, deployment risks, missing CI/CD coverage
**Model**: sonnet

### ☁️ Azure Infra Advisor
**Expertise**: Cloud vs on-prem decisions, cost optimization, infrastructure placement, TCO analysis
**Watches For**: Inappropriate cloud service usage, cost inefficiencies, infrastructure misplacement
**Model**: opus

### 🐧 Talos OS Expert
**Expertise**: Kubernetes-on-Talos, machine configs, etcd, cluster bootstrapping, immutable infrastructure
**Watches For**: Dangerous machine config changes, etcd risks, cluster upgrade issues
**Model**: opus (critical - can brick nodes)

### 📊 Observability Architect
**Expertise**: Distributed tracing, metrics, logging, OpenTelemetry, monitoring, alerting
**Watches For**: Missing instrumentation, inadequate logging, unmonitored critical paths
**Model**: sonnet

### 🛡️ Agent Service Guardian
**Expertise**: Security for customer-hosted agents, trust boundaries, process isolation, mTLS
**Watches For**: Security vulnerabilities in agent code, trust boundary violations, privilege escalation
**Model**: opus (critical - customer infrastructure security)

## Performance Considerations

### Review Time
- **Small PRs** (1-5 files): ~2-3 minutes (general review + 15 agents)
- **Medium PRs** (6-20 files): ~5-10 minutes
- **Large PRs** (chunked): Proportional to chunk count × number of relevant agents

### GPU Usage
- Each agent uses the same DeepSeek Coder 33B model
- Model loads once per agent (15 total loads per PR)
- With `OLLAMA_KEEP_ALIVE=0`, VRAM is freed between agents
- Total VRAM usage: ~22GB peak per agent consultation

### Context Window Handling
Both the **general review** and **each council member** automatically handle large PRs:
- **Context limit**: 16,384 tokens (12,288 for prompt + 4,096 for response)
- **Automatic chunking**: When a PR exceeds limits, files are split into multiple LLM calls
- **Intelligent merging**: Chunk results are merged into a cohesive review
- **Agent-specific**: Each council member chunks independently based on their prompt size

**Example**: A 50-file PR might be chunked as:
- General reviewer: 3 chunks (all files reviewed)
- Security Architect: 1 chunk (only security-relevant files)
- Database Schema Architect: 2 chunks (migration files reviewed in detail)
- REST API Engineer: 1 chunk (controller files)
- Other agents: Mark as "not relevant" without reviewing

This ensures **every agent can review every PR**, no matter how large.

### Optimization
The Council consultations run **sequentially** (not in parallel) to:
- Avoid GPU memory contention
- Provide predictable, reliable inference
- Allow for detailed logging and debugging per agent

For faster reviews with less thoroughness, you could disable specific agents or reduce the council size.

## Customization

### Adding New Agents
1. Create a new `.md` file in `.claude/agents/`
2. Follow the frontmatter format:
   ```markdown
   ---
   name: my-new-agent
   description: Brief description of agent's expertise
   model: sonnet|opus|haiku
   ---

   [System prompt describing agent's role and expertise]
   ```
3. Rebuild the Docker image to bundle the new agent
4. The agent will be automatically discovered on next service restart

**Note**: Agent definitions are bundled into the Docker image at build time from `.claude/agents/`. When running in Docker, they're available at `/app/agents/`. For local development with `dotnet run`, agents are loaded directly from `.claude/agents/` in the repository.

### Disabling Agents
**Docker deployment**: Remove the agent's `.md` file from `.claude/agents/` and rebuild the Docker image.
**Local development**: Move or rename the agent's `.md` file outside `.claude/agents/`.

### Adjusting Agent Behavior
Edit the agent's `.md` file to refine its:
- System prompt (expertise, focus areas)
- Response format guidance
- Priority levels for different issue types

## Example Output

Here's what a council-reviewed PR looks like:

```markdown
## General Code Review

This PR adds JWT authentication to the Identity service. The implementation looks solid
with proper token validation and expiration handling. Found 2 minor issues with error messages.

## Council of Greybeards

Expert opinions from specialized domain agents:

### 🧙 Security Architect

🚨 **CRITICAL FINDINGS** - This PR implements authentication and I've identified serious security concerns:

1. JWT signing key is hardcoded (line 42 in JwtService.cs)
2. No token rotation mechanism implemented
3. Missing HTTPS enforcement middleware

**3 specific concern(s) raised** (see inline comments below)

### 🧙 IAM Architect

The JWT implementation follows solid patterns but recommend:
- Implementing refresh tokens for better UX
- Adding support for passwordless authentication (WebAuthn) in future iterations

**1 specific concern(s) raised** (see inline comments below)

### 🧙 DotNet 10 Researcher

Consider using .NET 10's new `JsonWebTokenHandler` instead of `JwtSecurityTokenHandler`
for better performance and built-in validation features.

### Consulted (No Comments)

- **Database Schema Architect**: No schema changes in this PR
- **Database Admin**: No database performance concerns
- **Messaging Engineer**: No messaging-related changes
- **Microservices Architect**: Service boundaries remain appropriate
- **REST API Engineer**: API changes follow established patterns
- **Blazor WebDev Expert**: No frontend changes
- **DotNet Test Engineer**: Test coverage is adequate
- **Azure Pipelines Architect**: No CI/CD changes
- **Azure Infra Advisor**: No infrastructure changes
- **Talos OS Expert**: No Kubernetes configuration changes
- **Observability Architect**: Logging and tracing are sufficient
- **Agent Service Guardian**: No changes to agent code
```

**Inline Comments** (visible on PR):
```
🚨 [Security Architect] The JWT signing key should never be hardcoded.
Store it in Azure Key Vault or user-secrets for development.

⚠️ [Security Architect] Add token rotation to prevent indefinite token validity.

💡 [IAM Architect] Consider implementing refresh tokens using the sliding expiration pattern.

ℹ️ [DotNet 10 Researcher] Replace `JwtSecurityTokenHandler` with `JsonWebTokenHandler`
for 2-3x better validation performance in .NET 10.
```

## Benefits

1. **Comprehensive Coverage**: No aspect of the platform is overlooked
2. **Domain Expertise**: Each area reviewed by a specialized expert
3. **Early Detection**: Security, performance, and architectural issues caught early
4. **Learning Tool**: Developers see expert-level feedback on their code
5. **Consistent Standards**: Agents enforce platform-wide best practices
6. **Transparency**: All consulted agents listed, even those with no comments

## Limitations

- **Review Time**: Longer than single-agent review (tradeoff for thoroughness)
- **False Positives**: Agents may occasionally flag non-issues (refinement needed)
- **Context Limits**: Very large PRs may need chunking per agent
- **Model Variance**: Different LLM responses may vary slightly between runs

## Future Enhancements

- **Parallel Consultations**: Run multiple agents simultaneously (requires more VRAM)
- **Selective Consultation**: Only consult relevant agents based on PR file patterns
- **Agent Weighting**: Prioritize opinions from agents with higher relevance scores
- **Historical Learning**: Agents learn from accepted/rejected suggestions over time
- **Custom Rulesets**: Allow teams to define custom rules per agent
