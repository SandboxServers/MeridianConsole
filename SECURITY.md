# Security Policy

## Reporting a Vulnerability

If you discover a security vulnerability in Meridian Console, please report it responsibly.

**Do NOT open a public GitHub issue for security vulnerabilities.**

### How to Report

Email security concerns to: **security@meridianconsole.com**

Include:

- Description of the vulnerability
- Steps to reproduce
- Potential impact
- Any suggested fixes (optional)

### What to Expect

- **Acknowledgment**: Within 48 hours
- **Initial Assessment**: Within 7 days
- **Resolution Timeline**: Depends on severity, typically 30-90 days

We will keep you informed of progress and credit you in the fix (unless you prefer anonymity).

---

## Security Model

Meridian Console is a **control plane** that orchestrates game servers on **customer-owned hardware**. This architecture has specific security implications.

### Trust Boundaries

```
┌─────────────────────────────────────────────────────────────────┐
│                        INTERNET                                  │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                      EDGE (Cloudflare)                          │
│  • WAF, DDoS protection, TLS termination                        │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                    CONTROL PLANE (Our Infrastructure)           │
│  ┌─────────┐  ┌──────────┐  ┌─────────┐  ┌─────────────────┐   │
│  │ Gateway │──│ Identity │──│ Servers │──│ Other Services  │   │
│  └─────────┘  └──────────┘  └─────────┘  └─────────────────┘   │
│                                                                  │
│  • JWT authentication          • Multi-tenant isolation         │
│  • Rate limiting               • Audit logging                  │
│  • Input validation            • Secrets in Azure Key Vault     │
└─────────────────────────────────────────────────────────────────┘
                              │
                              │ Outbound connections only
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                 CUSTOMER INFRASTRUCTURE                          │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │                    Customer Agent                        │    │
│  │  • Runs on customer hardware                             │    │
│  │  • Makes OUTBOUND connections to control plane           │    │
│  │  • No inbound firewall holes required                    │    │
│  │  • mTLS authentication (planned)                         │    │
│  │  • Sandboxed process execution                           │    │
│  └─────────────────────────────────────────────────────────┘    │
│                              │                                   │
│                              ▼                                   │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │                    Game Servers                          │    │
│  │  • Managed by agent                                      │    │
│  │  • Process isolation                                     │    │
│  │  • Resource limits enforced                              │    │
│  └─────────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────────┘
```

### Key Security Properties

#### 1. Outbound-Only Agent Connections

Agents connect **outbound** to the control plane. Customers don't need to open inbound firewall ports, reducing their attack surface.

#### 2. Multi-Tenant Isolation

- Each organization is isolated at the database level
- JWT tokens contain tenant claims
- All API requests are scoped to the authenticated tenant
- Cross-tenant access is architecturally prevented

#### 3. Agent Security (Critical)

Agents run on customer hardware with elevated privileges. Agent code is treated as **security-critical**:

- All agent changes require security review
- Command injection prevention
- Path traversal prevention
- Process sandboxing
- Minimum data collection

#### 4. Secrets Management

- Production secrets stored in Azure Key Vault
- Development uses .NET User Secrets (never committed)
- Automatic secret rotation (planned)
- No secrets in configuration files or environment variables in production

---

## Supported Versions

| Version | Supported          |
| ------- | ------------------ |
| main    | :white_check_mark: |
| < 1.0   | :x: (pre-release)  |

We are currently pre-1.0. Security fixes will be applied to the `main` branch.

---

## Security Best Practices for Contributors

### Code Security

1. **Never commit secrets** - Use .NET User Secrets or environment variables
2. **Validate all input** - Especially from external sources (API requests, agent commands)
3. **Use parameterized queries** - EF Core does this by default, don't bypass it
4. **Escape output** - Prevent XSS in any rendered content
5. **Check authorization** - Verify tenant access on every request

### Agent Code (Extra Scrutiny)

Agent code (`src/Agents/`) requires additional care:

```csharp
// NEVER do this - command injection
Process.Start("bash", $"-c '{userInput}'");

// NEVER do this - path traversal
var path = Path.Combine(basePath, userInput);
File.Delete(path);

// ALWAYS validate paths
if (!PathValidator.IsPathSafe(basePath, userInput))
    throw new SecurityException("Invalid path");

// ALWAYS use parameterized execution
var psi = new ProcessStartInfo {
    FileName = validatedBinaryPath,
    Arguments = string.Join(" ", validatedArgs.Select(EscapeArgument))
};
```

> **Security Review Required**: After making any changes to agent code in `src/Agents/`, request a security review from the `agent-service-guardian` specialist team. This applies to all patterns shown above (Process.Start usage, PathValidator.IsPathSafe, ProcessStartInfo, EscapeArgument, etc.). Open a review ticket or notify `agent-service-guardian` before merging.

### Dependencies

- Keep dependencies updated (`dotnet outdated`)
- Review security advisories (`dotnet list package --vulnerable`)
- Prefer well-maintained packages with security track records

---

## Security Tools

### Static Analysis

The solution uses SecurityCodeScan for SAST:

```bash
# Check for security warnings
dotnet build /p:TreatWarningsAsErrors=true
```

### Dependency Scanning

```bash
# Check for vulnerable packages
dotnet list package --vulnerable

# Check for outdated packages
dotnet outdated
```

---

## Incident Response

If a security incident occurs:

1. **Contain** - Isolate affected systems
2. **Assess** - Determine scope and impact
3. **Notify** - Inform affected customers within 72 hours
4. **Remediate** - Fix the vulnerability
5. **Review** - Post-incident analysis and improvements

---

## Contact

- **Security Issues**: security@meridianconsole.com
- **General Questions**: Use GitHub Discussions
- **Bug Reports**: Use GitHub Issues (non-security bugs only)
