# MeridianConsole

[![Phase 1 Infrastructure](https://img.shields.io/badge/Phase%201-Infrastructure%20Complete-success)](https://github.com/SandboxServers/MeridianConsole)
[![.NET 9.0](https://img.shields.io/badge/.NET-9.0-512BD4)](https://dotnet.microsoft.com/)
[![Astro](https://img.shields.io/badge/Astro-4.0-FF5D01)](https://astro.build/)
[![Docker](https://img.shields.io/badge/Docker-Compose-2496ED)](https://docs.docker.com/compose/)

**MeridianConsole** is a microservices-based platform for managing Minecraft server infrastructure. The system consists of backend .NET services exposed through an API Gateway, plus multiple frontend applications built with Astro and Blazor.

---

## 🚀 Quick Start

### Prerequisites
- [Docker](https://docs.docker.com/get-docker/) & [Docker Compose](https://docs.docker.com/compose/install/)
- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [mkcert](https://github.com/FiloSottile/mkcert) for local HTTPS

### Setup Local HTTPS
```bash
mkcert -install
mkcert -key-file certs/meridian-key.pem -cert-file certs/meridian-cert.pem localhost *.localhost
```

### Launch Full Stack
```bash
docker-compose up --build
```

### Verify Services
```bash
# Gateway health
curl https://localhost:7000/health

# Identity Service diagnostics
curl https://localhost:7001/diagnostics

# Secrets Service diagnostics  
curl https://localhost:7002/diagnostics
```

---

## 📊 Project Status

### ✅ Phase 1: Infrastructure Complete

**What's Working:**
- ✅ 11 microservices (Gateway, Identity, Secrets, Backups, Cluster, RCON, Logs, Status, Players, Worlds, WorldGen)
- ✅ Diagnostic endpoints (`/health`, `/diagnostics`, `/ready`) on all services
- ✅ Azure Workload Identity Federation setup
- ✅ Secrets Service with Azure Key Vault integration
- ✅ API Gateway with Yarp reverse proxy
- ✅ Docker Compose orchestration
- ✅ HTTPS with self-signed certificates
- ✅ Structured logging with Serilog
- ✅ Health checks and readiness probes
- ✅ 3 frontend applications (Scope, ShoppingCart, Panel)
- ✅ BetterAuth integration groundwork

**Current Focus:**
- 🚧 Stack Auth integration for authentication
- 🚧 Talos OS Kubernetes deployment
- 🚧 Frontend-backend authentication flow

---

## 🏗️ Architecture

### System Diagram
```
┌─────────────────────────────────────────────────────────────┐
│                    FRONTEND LAYER                            │
│  ┌─────────────┐  ┌──────────────┐  ┌──────────────┐       │
│  │ Dhadgar.    │  │ Dhadgar.     │  │ Dhadgar.     │       │
│  │ Scope       │  │ ShoppingCart │  │ Panel        │       │
│  │ (Astro)     │  │ (Blazor)     │  │ (Blazor)     │       │
│  └─────────────┘  └──────────────┘  └──────────────┘       │
└───────────────┬─────────────────────────────────────────────┘
                │
                ▼
┌─────────────────────────────────────────────────────────────┐
│                    API GATEWAY (Yarp)                        │
│                   :7000 (HTTPS)                              │
└───────────────┬─────────────────────────────────────────────┘
                │
    ┌───────────┼───────────┐
    ▼           ▼           ▼
┌─────────┐ ┌─────────┐ ┌─────────┐
│Identity │ │Secrets  │ │Backups  │
│:7001    │ │:7002    │ │:7003    │
└─────────┘ └─────────┘ └─────────┘
    ▼           ▼           ▼
┌─────────┐ ┌─────────┐ ┌─────────┐
│Cluster  │ │RCON     │ │Logs     │
│:7004    │ │:7005    │ │:7006    │
└─────────┘ └─────────┘ └─────────┘
    ▼           ▼           ▼
┌─────────┐ ┌─────────┐ ┌─────────┐
│Status   │ │Players  │ │Worlds   │
│:7007    │ │:7008    │ │:7009    │
└─────────┘ └─────────┘ └─────────┘
                ▼
            ┌─────────┐
            │WorldGen │
            │:7010    │
            └─────────┘
                ▼
┌─────────────────────────────────────────────────────────────┐
│              INFRASTRUCTURE LAYER                            │
│  ┌────────────────┐  ┌────────────────┐  ┌──────────────┐  │
│  │ PostgreSQL     │  │ Azure Key Vault│  │ Azure Entra  │  │
│  │ (Database)     │  │ (Secrets)      │  │ ID (Auth)    │  │
│  └────────────────┘  └────────────────┘  └──────────────┘  │
└─────────────────────────────────────────────────────────────┘
```

---

## 📁 Repository Structure

### Backend Services (C# / .NET 9.0)
Located in `Dhadgar/` directory:

| Service | Port | Purpose |
|---------|------|---------|
| **Gateway** | 7000 | API Gateway (Yarp reverse proxy) |
| **Identity** | 7001 | Authentication & authorization |
| **Secrets** | 7002 | Secrets management (Azure Key Vault) |
| **Backups** | 7003 | World backup management |
| **Cluster** | 7004 | Kubernetes cluster orchestration |
| **RCON** | 7005 | Minecraft RCON protocol |
| **Logs** | 7006 | Centralized logging |
| **Status** | 7007 | Server status monitoring |
| **Players** | 7008 | Player session management |
| **Worlds** | 7009 | World file management |
| **WorldGen** | 7010 | World generation service |

### Frontend Projects
Located in `Dhadgar/` directory:

| Project | Framework | Purpose | Deployment Target |
|---------|-----------|---------|-------------------|
| **Dhadgar.Scope** | Astro + React + TailwindCSS | Public-facing marketing/docs site | Azure Static Web Apps |
| **Dhadgar.ShoppingCart** | Blazor WebAssembly | E-commerce/subscription management | Kubernetes |
| **Dhadgar.Panel** | Blazor WebAssembly | Admin control panel | Kubernetes |

**Why Two Frameworks?**
- **Astro (Scope)**: Static site generation, SEO-friendly, fast public pages, BetterAuth integration
- **Blazor (ShoppingCart, Panel)**: Rich interactivity, real-time updates, complex UI logic

---

## 🛠️ Development

### Run Individual Service
```bash
cd Dhadgar/Services/Dhadgar.IdentityService
dotnet run
```

### Run Frontend (Scope)
```bash
cd Dhadgar/Dhadgar.Scope
npm install
npm run dev
```

### Run Frontend (Panel/ShoppingCart)
```bash
cd Dhadgar/Dhadgar.Panel
dotnet run
```

### Build All Services
```bash
dotnet build Dhadgar/Dhadgar.sln
```

### Run Tests
```bash
dotnet test Dhadgar/Dhadgar.sln
```

---

## 🔧 Configuration

### Environment Variables
Core configuration is in `.env.docker` (see `.env.docker.example`):

```env
# Azure Configuration
AZURE_TENANT_ID=your-tenant-id
AZURE_CLIENT_ID=your-client-id
AZURE_KEY_VAULT_URL=https://your-vault.vault.azure.net/

# Database
DB_HOST=postgres
DB_PORT=5432
DB_NAME=meridian
DB_USER=meridian
DB_PASSWORD=your-secure-password

# Authentication
STACK_AUTH_PROJECT_ID=your-project-id
STACK_AUTH_PUBLISHABLE_KEY=pk_...
STACK_AUTH_SECRET_KEY=sk_...

# SSL Certificates
SSL_CERT_PATH=/app/certs/meridian-cert.pem
SSL_KEY_PATH=/app/certs/meridian-key.pem
```

### Service-Specific Configuration
Each service has its own `appsettings.json`:
- Connection strings
- Logging configuration
- Service-specific settings

---

## 🧪 Diagnostic Endpoints

All services expose diagnostic endpoints:

| Endpoint | Purpose |
|----------|---------|
| `/health` | Health check (200 = healthy) |
| `/diagnostics` | Detailed service info (version, uptime, config) |
| `/ready` | Readiness probe (200 = ready for traffic) |

**Example:**
```bash
curl https://localhost:7001/diagnostics | jq
```

**Response:**
```json
{
  "service": "IdentityService",
  "version": "1.0.0",
  "uptime": "00:15:32",
  "environment": "Development",
  "timestamp": "2026-01-10T02:30:00Z",
  "azureKeyVault": {
    "configured": true,
    "url": "https://meridian-kv.vault.azure.net/"
  }
}
```

---

## 🔐 Authentication & Authorization

### Current State
- ✅ Azure Workload Identity Federation configured
- ✅ Secrets Service integrated with Azure Key Vault
- 🚧 Stack Auth integration in progress
- 🚧 Frontend authentication flow (Astro ↔ .NET)

### Planned Flow
```
User → Astro Frontend → Stack Auth → JWT Token → API Gateway → Services
```

---

## 🚢 Deployment

### Local Development
```bash
docker-compose up --build
```

### Kubernetes (Talos OS)
Helm charts and Kubernetes manifests are in `k8s/` directory:

```bash
# Deploy to Talos cluster
kubectl apply -f k8s/namespace.yaml
kubectl apply -f k8s/services/
kubectl apply -f k8s/ingress/
```

### Azure Static Web Apps (Scope)
Scope (Astro) deploys to Azure SWA via GitHub Actions:

```bash
# Build for production
cd Dhadgar/Dhadgar.Scope
npm run build

# Outputs to dist/ for SWA deployment
```

---

## 🤝 Contributing

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

---

## 📝 License

This project is proprietary software owned by Sandbox Servers LLC.

---

## 🆘 Support

- **Issues**: [GitHub Issues](https://github.com/SandboxServers/MeridianConsole/issues)
- **Discussions**: [GitHub Discussions](https://github.com/SandboxServers/MeridianConsole/discussions)
- **Email**: support@sandboxservers.com

---

## 🗺️ Roadmap

See [ROADMAP.md](docs/ROADMAP.md) for detailed development plans.