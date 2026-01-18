# Identity Service - Complete Implementation Plan

## Document Status
**Version:** 2.0 - Final
**Date:** 2025-12-28
**Status:** ✅ Ready for Implementation
**Agent Review:** Complete (security-architect, iam-architect, microservices-architect, dotnet-10-researcher)

---

## Table of Contents

1. [Architecture Overview](#architecture-overview)
2. [Technology Stack](#technology-stack)
3. [Database Schema](#database-schema)
4. [Authentication Flow](#authentication-flow)
5. [Authorization Model](#authorization-model)
6. [Implementation Phases](#implementation-phases)
7. [Code Specifications](#code-specifications)
8. [Security Requirements](#security-requirements)
9. [Testing Strategy](#testing-strategy)
10. [Deployment Considerations](#deployment-considerations)

---

## Architecture Overview

### Hybrid Identity System

The Meridian Console uses a **hybrid identity architecture** combining:

1. **Better Auth (Node.js/Astro)** - User-facing authentication
   - Social OAuth providers (Discord, Twitch, Google, Microsoft, GitHub, Facebook, Apple)
   - Passkey/WebAuthn support
   - Hardware token (FIDO2) support
   - Session management

2. **ASP.NET Core Identity (.NET 10)** - Backend identity and authorization
   - Gaming OAuth providers (Steam, Epic Games, Battle.net, Xbox Live)
   - Organization and permission management
   - Claims-based authorization
   - JWT token issuance
   - OpenIddict OIDC provider for Azure federation

### System Diagram

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                          MERIDIAN CONSOLE IDENTITY                           │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  ┌──────────────┐              ┌────────────────────────────────────────┐   │
│  │ Astro Frontend│◄────────────►│    Better Auth (Node.js)               │   │
│  │  (SSR/Static)│              │  • Discord, Twitch, Google OAuth       │   │
│  └──────┬───────┘              │  • Passkeys (WebAuthn)                 │   │
│         │                      │  • Issues 60s exchange token           │   │
│         │ Exchange Token       └────────────────┬───────────────────────┘   │
│         │ (ES256, single-use)                   │                           │
│         │                                       │ Webhooks                  │
│         ▼                                       ▼                           │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │                       YARP Gateway (Port 443)                        │   │
│  │  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐              │   │
│  │  │ JWT Validate │→ │Strip Headers │→ │ Inject Claims│→ Route       │   │
│  │  │ (JWKS cache) │  │ (security)   │  │ X-User-Id... │              │   │
│  │  └──────────────┘  └──────────────┘  └──────────────┘              │   │
│  └──────────────────────────────┬──────────────────────────────────────┘   │
│                                 │                                           │
│  ┌──────────────────────────────┴───────────────────────────────────────┐  │
│  │            Dhadgar.Identity Service (ASP.NET Core/.NET 10)            │  │
│  │                                                                        │  │
│  │  ┌──────────────────────────────────────────────────────────────┐    │  │
│  │  │                Token Exchange Endpoint                        │    │  │
│  │  │  POST /api/auth/exchange                                      │    │  │
│  │  │  1. Validate exchange token (ES256, issuer, audience)         │    │  │
│  │  │  2. Check Redis for single-use (SETNX pattern)                │    │  │
│  │  │  3. Upsert user in PostgreSQL (with transaction)              │    │  │
│  │  │  4. Load org memberships + calculate permissions              │    │  │
│  │  │  5. Issue JWT (ES256, 15min access + 7day refresh)            │    │  │
│  │  └──────────────────────────────────────────────────────────────┘    │  │
│  │                                                                        │  │
│  │  ┌──────────────────────────────────────────────────────────────┐    │  │
│  │  │           Gaming OAuth Handlers (ASP.NET Core)                │    │  │
│  │  │  • Steam (OpenID 2.0 via AspNet.Security.OpenId.Steam)        │    │  │
│  │  │  • Epic Games (Custom OAuth handler)                          │    │  │
│  │  │  • Battle.net (AspNet.Security.OAuth.BattleNet)               │    │  │
│  │  │  • Xbox Live (Microsoft Account with xboxlive.signin scope)   │    │  │
│  │  └──────────────────────────────────────────────────────────────┘    │  │
│  │                                                                        │  │
│  │  ┌──────────────────────────────────────────────────────────────┐    │  │
│  │  │         OpenIddict OIDC Provider (Self-Hosted)                │    │  │
│  │  │  • /connect/authorize (authorization endpoint)                │    │  │
│  │  │  • /connect/token (token endpoint)                            │    │  │
│  │  │  • /connect/userinfo (user info endpoint)                     │    │  │
│  │  │  • /.well-known/jwks.json (public key distribution)           │    │  │
│  │  │  • Enables Azure federated credentials                        │    │  │
│  │  └──────────────────────────────────────────────────────────────┘    │  │
│  │                                                                        │  │
│  │  ┌──────────────────────────────────────────────────────────────┐    │  │
│  │  │              Organization & Claims Management                 │    │  │
│  │  │  • Multi-tenant (organization-scoped permissions)             │    │  │
│  │  │  • Hybrid roles + claims (role implies claims + overrides)    │    │  │
│  │  │  • Organization switching (re-issue JWT with new org context) │    │  │
│  │  └──────────────────────────────────────────────────────────────┘    │  │
│  │                                                                        │  │
│  │  ┌──────────────────────────────────────────────────────────────┐    │  │
│  │  │                 Webhook Receiver (Better Auth)                │    │  │
│  │  │  POST /webhooks/better-auth                                   │    │  │
│  │  │  • user.deleted → soft delete in .NET                         │    │  │
│  │  │  • user.updated → sync email changes                          │    │  │
│  │  │  • passkey.registered → update flag                           │    │  │
│  │  └──────────────────────────────────────────────────────────────┘    │  │
│  │                                                                        │  │
│  └────────────────────────────────┬───────────────────────────────────────┘
│                                   │                                         │
│  ┌────────────────────────────────┴───────────────────────────────────┐   │
│  │                   Data & Messaging Layer                            │   │
│  │  ┌──────────────┐  ┌──────────────┐  ┌──────────────────────────┐  │   │
│  │  │ PostgreSQL   │  │    Redis     │  │  RabbitMQ (MassTransit)   │  │   │
│  │  │ • User       │  │ • Exchange   │  │  • UserAuthenticated      │  │   │
│  │  │ • Org        │  │   token JTI  │  │  • OrgMembershipChanged   │  │   │
│  │  │ • LinkedAcct │  │ • Refresh    │  │  • UserDeactivated        │  │   │
│  │  │ • Claims     │  │   tokens     │  │                           │  │   │
│  │  └──────────────┘  └──────────────┘  └──────────────────────────┘  │   │
│  └────────────────────────────────────────────────────────────────────┘   │
│                                                                             │
│  ┌─────────────────────────────────────────────────────────────────────┐  │
│  │              Other Microservices (Servers, Nodes, Tasks...)          │  │
│  │  • Trust Gateway-injected headers (X-User-Id, X-Org-Id, X-Perms)    │  │
│  │  • Service-to-Service: Client Credentials flow via OpenIddict       │  │
│  │  • Subscribe to identity events via MassTransit                     │  │
│  └─────────────────────────────────────────────────────────────────────┘  │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘

Legend:
  ──────► Synchronous HTTP/REST
  ◄─────► Bidirectional HTTP
  ───────► Async messaging (MassTransit/RabbitMQ)
```

---

## Technology Stack

### Better Auth (Node.js Runtime)

| Component | Technology | Version |
|-----------|-----------|---------|
| Runtime | Node.js or Bun | 20+ |
| Framework | Better Auth | Latest |
| Frontend | Astro | 4.x |
| OAuth Providers | Discord, Twitch, Google, Microsoft, GitHub, Facebook, Apple | - |
| Passkey Support | @better-auth/passkey plugin | - |
| Database | PostgreSQL (Better Auth tables) | 16+ |

### ASP.NET Core Identity (.NET Runtime)

| Component | Technology | Version |
|-----------|-----------|---------|
| Runtime | .NET | 10.0 |
| Framework | ASP.NET Core | 10.0 |
| ORM | Entity Framework Core | 10.0 |
| Database | PostgreSQL | 16+ |
| OAuth Providers | Steam, Epic Games, Battle.net, Xbox Live | - |
| OIDC Provider | OpenIddict | 6.0.0 |
| JWT Library | Microsoft.IdentityModel.JsonWebTokens | 8.15.0 |
| Redis Client | StackExchange.Redis | 2.8.16 |
| Messaging | MassTransit | 8.3.6 |

### Required NuGet Packages

```xml
<!-- Directory.Packages.props additions -->
<ItemGroup>
  <!-- Identity & Authentication -->
  <PackageVersion Include="Microsoft.AspNetCore.Identity.EntityFrameworkCore" Version="10.0.0" />
  <PackageVersion Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="10.0.0" />
  <PackageVersion Include="Microsoft.IdentityModel.JsonWebTokens" Version="8.15.0" />

  <!-- OIDC Provider -->
  <PackageVersion Include="OpenIddict.AspNetCore" Version="6.0.0" />
  <PackageVersion Include="OpenIddict.EntityFrameworkCore" Version="6.0.0" />

  <!-- OAuth Providers (aspnet-contrib) -->
  <PackageVersion Include="AspNet.Security.OpenId.Steam" Version="10.0.0" />
  <PackageVersion Include="AspNet.Security.OAuth.BattleNet" Version="10.0.0" />
  <!-- Custom Epic Games OAuth handler (see implementation section) -->

  <!-- Data & Caching -->
  <PackageVersion Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="10.0.0" />
  <PackageVersion Include="StackExchange.Redis" Version="2.8.16" />

  <!-- Messaging -->
  <PackageVersion Include="MassTransit.RabbitMQ" Version="8.3.6" />

  <!-- Observability -->
  <PackageVersion Include="OpenTelemetry.Extensions.Hosting" Version="1.10.0" />
  <PackageVersion Include="OpenTelemetry.Instrumentation.AspNetCore" Version="1.10.1" />
  <PackageVersion Include="OpenTelemetry.Instrumentation.Http" Version="1.10.1" />
  <PackageVersion Include="OpenTelemetry.Instrumentation.EntityFrameworkCore" Version="1.0.0-beta.13" />
  <PackageVersion Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" Version="1.10.0" />

  <!-- Testing -->
  <PackageVersion Include="Microsoft.Extensions.TimeProvider.Testing" Version="10.1.0" />
</ItemGroup>
```

---

## Database Schema

### Entity Relationship Diagram

```
┌─────────────────┐         ┌─────────────────────┐         ┌──────────────────┐
│      User       │         │  UserOrganization   │         │  Organization    │
├─────────────────┤         ├─────────────────────┤         ├──────────────────┤
│ Id (PK)         │◄───────┤│ Id (PK)             │├───────►│ Id (PK)          │
│ ExternalAuthId  │         │ UserId (FK)         │         │ Name             │
│ Email           │         │ OrganizationId (FK) │         │ Slug             │
│ EmailVerified   │         │ Role                │         │ OwnerId (FK)     │
│ PreferredOrgId  │         │ IsActive            │         │ Settings (JSON)  │
│ HasPasskeys     │         │ JoinedAt            │         │ CreatedAt        │
│ LastAuthAt      │         │ LeftAt              │         │ Version          │
│ CreatedAt       │         │ InvitedByUserId     │         └──────────────────┘
│ Version         │         └─────────────────────┘
└────────┬────────┘                   │
         │                            │
         │                            ▼
         │                  ┌─────────────────────────┐
         │                  │ UserOrganizationClaim   │
         │                  ├─────────────────────────┤
         │                  │ Id (PK)                 │
         │                  │ UserOrganizationId (FK) │
         │                  │ ClaimType (Grant/Deny)  │
         │                  │ ClaimValue              │
         │                  │ ResourceType            │
         │                  │ ResourceId              │
         │                  │ GrantedAt               │
         │                  │ GrantedByUserId (FK)    │
         │                  │ ExpiresAt               │
         │                  └─────────────────────────┘
         │
         ▼
┌─────────────────────┐          ┌──────────────────┐
│   LinkedAccount     │          │ ClaimDefinition  │
├─────────────────────┤          ├──────────────────┤
│ Id (PK)             │          │ Id (PK)          │
│ UserId (FK)         │          │ Name             │
│ Provider            │          │ Description      │
│ ProviderAccountId   │          │ Category         │
│ ProviderMetadata    │          │ IsSystemClaim    │
│   (JSON)            │          │ CreatedAt        │
│ LinkedAt            │          └──────────────────┘
│ LastUsedAt          │
└─────────────────────┘

┌─────────────────────┐
│   RefreshToken      │
├─────────────────────┤
│ Id (PK)             │
│ UserId (FK)         │
│ Token (hashed)      │
│ OrganizationId      │
│ IssuedAt            │
│ ExpiresAt           │
│ RevokedAt           │
│ DeviceInfo          │
└─────────────────────┘
```

### Entity Definitions

#### User Entity

```csharp
using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;

namespace Dhadgar.Identity.Data.Entities;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// External authentication ID from Better Auth (sub claim from exchange token)
    /// </summary>
    [Required]
    [MaxLength(255)]
    public string ExternalAuthId { get; set; } = null!;

    [Required]
    [MaxLength(320)]
    [EmailAddress]
    public string Email { get; set; } = null!;

    public bool EmailVerified { get; set; }

    /// <summary>
    /// User's preferred organization (sticky choice across sessions)
    /// </summary>
    public Guid? PreferredOrganizationId { get; set; }

    /// <summary>
    /// Flag indicating user has passkeys registered in Better Auth
    /// Synced via webhook - actual passkeys stored in Better Auth
    /// </summary>
    public bool HasPasskeysRegistered { get; set; }

    public DateTime? LastPasskeyAuthAt { get; set; }
    public DateTime? LastAuthenticatedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; } // Soft delete

    /// <summary>
    /// PostgreSQL xmin-based optimistic concurrency
    /// </summary>
    public uint Version { get; set; }

    // Navigation properties
    public Organization? PreferredOrganization { get; set; }
    public ICollection<UserOrganization> Organizations { get; set; } = new List<UserOrganization>();
    public ICollection<LinkedAccount> LinkedAccounts { get; set; } = new List<LinkedAccount>();
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
}
```

#### Organization Entity

```csharp
namespace Dhadgar.Identity.Data.Entities;

public class Organization
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = null!;

    /// <summary>
    /// URL-safe identifier (e.g., "acme-corp")
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string Slug { get; set; } = null!;

    /// <summary>
    /// User who owns this organization (typically creator)
    /// </summary>
    public Guid OwnerId { get; set; }

    /// <summary>
    /// Organization settings stored as JSON
    /// </summary>
    public OrganizationSettings Settings { get; set; } = new();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; } // Soft delete

    public uint Version { get; set; }

    // Navigation properties
    public User Owner { get; set; } = null!;
    public ICollection<UserOrganization> Members { get; set; } = new List<UserOrganization>();
}

public class OrganizationSettings
{
    public bool AllowMemberInvites { get; set; } = true;
    public bool RequireEmailVerification { get; set; } = true;
    public int MaxMembers { get; set; } = 10; // Default quota
    public Dictionary<string, string> CustomSettings { get; set; } = new();
}
```

#### UserOrganization Entity (Join Table with Payload)

```csharp
namespace Dhadgar.Identity.Data.Entities;

public class UserOrganization
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }
    public Guid OrganizationId { get; set; }

    /// <summary>
    /// Role within this organization (owner, admin, operator, viewer)
    /// Role implies a set of claims (see RoleDefinitions)
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string Role { get; set; } = "viewer";

    public bool IsActive { get; set; } = true;

    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LeftAt { get; set; }

    public Guid? InvitedByUserId { get; set; }
    public DateTime? InvitationAcceptedAt { get; set; }

    // Navigation properties
    public User User { get; set; } = null!;
    public Organization Organization { get; set; } = null!;
    public User? InvitedBy { get; set; }
    public ICollection<UserOrganizationClaim> CustomClaims { get; set; } = new List<UserOrganizationClaim>();
}
```

#### UserOrganizationClaim Entity

```csharp
namespace Dhadgar.Identity.Data.Entities;

public class UserOrganizationClaim
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserOrganizationId { get; set; }

    /// <summary>
    /// Grant: Adds permission beyond role-implied
    /// Deny: Revokes permission even if role-implied
    /// </summary>
    public ClaimType ClaimType { get; set; }

    /// <summary>
    /// The claim value (e.g., "servers:delete", "billing:read")
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string ClaimValue { get; set; } = null!;

    /// <summary>
    /// Optional resource scoping (e.g., "server", "node")
    /// </summary>
    [MaxLength(50)]
    public string? ResourceType { get; set; }

    /// <summary>
    /// Optional specific resource ID
    /// </summary>
    public Guid? ResourceId { get; set; }

    public DateTime GrantedAt { get; set; } = DateTime.UtcNow;
    public Guid GrantedByUserId { get; set; }

    /// <summary>
    /// Optional expiration for temporary grants
    /// </summary>
    public DateTime? ExpiresAt { get; set; }

    // Navigation properties
    public UserOrganization UserOrganization { get; set; } = null!;
    public User GrantedBy { get; set; } = null!;
}

public enum ClaimType
{
    Grant = 1,
    Deny = 2
}
```

#### LinkedAccount Entity

```csharp
namespace Dhadgar.Identity.Data.Entities;

public class LinkedAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    /// <summary>
    /// OAuth provider (e.g., "discord", "steam", "epic")
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string Provider { get; set; } = null!;

    /// <summary>
    /// Provider's user ID
    /// </summary>
    [Required]
    [MaxLength(255)]
    public string ProviderAccountId { get; set; } = null!;

    /// <summary>
    /// Provider-specific metadata (avatar, username, etc.)
    /// Stored as JSON in PostgreSQL
    /// </summary>
    public LinkedAccountMetadata? ProviderMetadata { get; set; }

    public DateTime LinkedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastUsedAt { get; set; }

    // Navigation properties
    public User User { get; set; } = null!;
}

public class LinkedAccountMetadata
{
    public string? AvatarUrl { get; set; }
    public string? DisplayName { get; set; }
    public string? Username { get; set; }
    public Dictionary<string, string> ExtraData { get; set; } = new();
}
```

#### RefreshToken Entity

```csharp
namespace Dhadgar.Identity.Data.Entities;

public class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    /// <summary>
    /// Hashed refresh token (SHA256)
    /// </summary>
    [Required]
    [MaxLength(128)]
    public string TokenHash { get; set; } = null!;

    /// <summary>
    /// Organization context for this token
    /// </summary>
    public Guid OrganizationId { get; set; }

    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }

    /// <summary>
    /// Optional device information for audit trail
    /// </summary>
    [MaxLength(500)]
    public string? DeviceInfo { get; set; }

    // Navigation properties
    public User User { get; set; } = null!;
    public Organization Organization { get; set; } = null!;
}
```

#### ClaimDefinition Entity

```csharp
namespace Dhadgar.Identity.Data.Entities;

public class ClaimDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Claim name (e.g., "servers:read", "nodes:manage")
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = null!;

    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>
    /// Category for grouping (e.g., "servers", "billing", "organization")
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string Category { get; set; } = null!;

    /// <summary>
    /// System claims cannot be deleted
    /// </summary>
    public bool IsSystemClaim { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

### EF Core Configuration

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dhadgar.Identity.Data.Configuration;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");

        builder.HasKey(u => u.Id);

        builder.Property(u => u.ExternalAuthId)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(u => u.Email)
            .IsRequired()
            .HasMaxLength(320);

        builder.Property(u => u.Version)
            .IsRowVersion(); // PostgreSQL xmin

        // Indexes
        builder.HasIndex(u => u.ExternalAuthId)
            .IsUnique()
            .HasDatabaseName("ix_users_external_auth_id");

        builder.HasIndex(u => u.Email)
            .HasDatabaseName("ix_users_email");

        builder.HasIndex(u => u.DeletedAt)
            .HasFilter("deleted_at IS NULL")
            .HasDatabaseName("ix_users_active");

        // Relationships
        builder.HasOne(u => u.PreferredOrganization)
            .WithMany()
            .HasForeignKey(u => u.PreferredOrganizationId)
            .OnDelete(DeleteBehavior.SetNull);

        // Query filter for soft deletes
        builder.HasQueryFilter(u => u.DeletedAt == null);
    }
}

public class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> builder)
    {
        builder.ToTable("organizations");

        builder.HasKey(o => o.Id);

        builder.Property(o => o.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(o => o.Slug)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(o => o.Version)
            .IsRowVersion();

        // JSON column for settings
        builder.OwnsOne(o => o.Settings, settings =>
        {
            settings.ToJson();
        });

        // Indexes
        builder.HasIndex(o => o.Slug)
            .IsUnique()
            .HasDatabaseName("ix_organizations_slug");

        builder.HasIndex(o => o.OwnerId)
            .HasDatabaseName("ix_organizations_owner_id");

        builder.HasIndex(o => o.DeletedAt)
            .HasFilter("deleted_at IS NULL")
            .HasDatabaseName("ix_organizations_active");

        // Relationships
        builder.HasOne(o => o.Owner)
            .WithMany()
            .HasForeignKey(o => o.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasQueryFilter(o => o.DeletedAt == null);
    }
}

public class UserOrganizationConfiguration : IEntityTypeConfiguration<UserOrganization>
{
    public void Configure(EntityTypeBuilder<UserOrganization> builder)
    {
        builder.ToTable("user_organizations");

        builder.HasKey(uo => uo.Id);

        builder.Property(uo => uo.Role)
            .IsRequired()
            .HasMaxLength(50)
            .HasDefaultValue("viewer");

        // Indexes
        builder.HasIndex(uo => new { uo.UserId, uo.OrganizationId })
            .HasFilter("left_at IS NULL")
            .IsUnique()
            .HasDatabaseName("ix_user_organizations_active_membership");

        builder.HasIndex(uo => uo.OrganizationId)
            .HasDatabaseName("ix_user_organizations_organization_id");

        // Relationships
        builder.HasOne(uo => uo.User)
            .WithMany(u => u.Organizations)
            .HasForeignKey(uo => uo.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(uo => uo.Organization)
            .WithMany(o => o.Members)
            .HasForeignKey(uo => uo.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(uo => uo.InvitedBy)
            .WithMany()
            .HasForeignKey(uo => uo.InvitedByUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public class UserOrganizationClaimConfiguration : IEntityTypeConfiguration<UserOrganizationClaim>
{
    public void Configure(EntityTypeBuilder<UserOrganizationClaim> builder)
    {
        builder.ToTable("user_organization_claims");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.ClaimValue)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(c => c.ResourceType)
            .HasMaxLength(50);

        // Indexes
        builder.HasIndex(c => c.UserOrganizationId)
            .HasDatabaseName("ix_user_org_claims_user_org_id");

        builder.HasIndex(c => new { c.UserOrganizationId, c.ClaimValue })
            .HasDatabaseName("ix_user_org_claims_lookup");

        // Relationships
        builder.HasOne(c => c.UserOrganization)
            .WithMany(uo => uo.CustomClaims)
            .HasForeignKey(c => c.UserOrganizationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(c => c.GrantedBy)
            .WithMany()
            .HasForeignKey(c => c.GrantedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class LinkedAccountConfiguration : IEntityTypeConfiguration<LinkedAccount>
{
    public void Configure(EntityTypeBuilder<LinkedAccount> builder)
    {
        builder.ToTable("linked_accounts");

        builder.HasKey(la => la.Id);

        builder.Property(la => la.Provider)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(la => la.ProviderAccountId)
            .IsRequired()
            .HasMaxLength(255);

        // JSON column for metadata
        builder.OwnsOne(la => la.ProviderMetadata, metadata =>
        {
            metadata.ToJson();
        });

        // Indexes
        builder.HasIndex(la => new { la.Provider, la.ProviderAccountId })
            .IsUnique()
            .HasDatabaseName("ix_linked_accounts_provider_account");

        builder.HasIndex(la => la.UserId)
            .HasDatabaseName("ix_linked_accounts_user_id");

        // Relationships
        builder.HasOne(la => la.User)
            .WithMany(u => u.LinkedAccounts)
            .HasForeignKey(la => la.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");

        builder.HasKey(rt => rt.Id);

        builder.Property(rt => rt.TokenHash)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(rt => rt.DeviceInfo)
            .HasMaxLength(500);

        // Indexes
        builder.HasIndex(rt => rt.TokenHash)
            .IsUnique()
            .HasDatabaseName("ix_refresh_tokens_token_hash");

        builder.HasIndex(rt => new { rt.UserId, rt.ExpiresAt })
            .HasFilter("revoked_at IS NULL")
            .HasDatabaseName("ix_refresh_tokens_user_active");

        // Relationships
        builder.HasOne(rt => rt.User)
            .WithMany(u => u.RefreshTokens)
            .HasForeignKey(rt => rt.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(rt => rt.Organization)
            .WithMany()
            .HasForeignKey(rt => rt.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class ClaimDefinitionConfiguration : IEntityTypeConfiguration<ClaimDefinition>
{
    public void Configure(EntityTypeBuilder<ClaimDefinition> builder)
    {
        builder.ToTable("claim_definitions");

        builder.HasKey(cd => cd.Id);

        builder.Property(cd => cd.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(cd => cd.Category)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(cd => cd.Description)
            .HasMaxLength(500);

        // Indexes
        builder.HasIndex(cd => cd.Name)
            .IsUnique()
            .HasDatabaseName("ix_claim_definitions_name");

        builder.HasIndex(cd => cd.Category)
            .HasDatabaseName("ix_claim_definitions_category");
    }
}
```

### DbContext

```csharp
using Microsoft.EntityFrameworkCore;
using Dhadgar.Identity.Data.Entities;

namespace Dhadgar.Identity.Data;

public class IdentityDbContext : DbContext
{
    public IdentityDbContext(DbContextOptions<IdentityDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<UserOrganization> UserOrganizations => Set<UserOrganization>();
    public DbSet<UserOrganizationClaim> UserOrganizationClaims => Set<UserOrganizationClaim>();
    public DbSet<LinkedAccount> LinkedAccounts => Set<LinkedAccount>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<ClaimDefinition> ClaimDefinitions => Set<ClaimDefinition>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply configurations
        modelBuilder.ApplyConfiguration(new UserConfiguration());
        modelBuilder.ApplyConfiguration(new OrganizationConfiguration());
        modelBuilder.ApplyConfiguration(new UserOrganizationConfiguration());
        modelBuilder.ApplyConfiguration(new UserOrganizationClaimConfiguration());
        modelBuilder.ApplyConfiguration(new LinkedAccountConfiguration());
        modelBuilder.ApplyConfiguration(new RefreshTokenConfiguration());
        modelBuilder.ApplyConfiguration(new ClaimDefinitionConfiguration());

        // Seed system claim definitions
        SeedClaimDefinitions(modelBuilder);
    }

    private void SeedClaimDefinitions(ModelBuilder modelBuilder)
    {
        var claims = new List<ClaimDefinition>
        {
            // Organization claims
            new() { Id = Guid.Parse("00000000-0000-0000-0001-000000000001"), Name = "org:read", Category = "organization", Description = "View organization details", IsSystemClaim = true },
            new() { Id = Guid.Parse("00000000-0000-0000-0001-000000000002"), Name = "org:write", Category = "organization", Description = "Update organization settings", IsSystemClaim = true },
            new() { Id = Guid.Parse("00000000-0000-0000-0001-000000000003"), Name = "org:delete", Category = "organization", Description = "Delete organization", IsSystemClaim = true },
            new() { Id = Guid.Parse("00000000-0000-0000-0001-000000000004"), Name = "org:billing", Category = "organization", Description = "Manage billing and subscriptions", IsSystemClaim = true },

            // Member management claims
            new() { Id = Guid.Parse("00000000-0000-0000-0002-000000000001"), Name = "members:read", Category = "members", Description = "View organization members", IsSystemClaim = true },
            new() { Id = Guid.Parse("00000000-0000-0000-0002-000000000002"), Name = "members:invite", Category = "members", Description = "Invite new members", IsSystemClaim = true },
            new() { Id = Guid.Parse("00000000-0000-0000-0002-000000000003"), Name = "members:remove", Category = "members", Description = "Remove members", IsSystemClaim = true },
            new() { Id = Guid.Parse("00000000-0000-0000-0002-000000000004"), Name = "members:roles", Category = "members", Description = "Assign member roles", IsSystemClaim = true },

            // Server management claims
            new() { Id = Guid.Parse("00000000-0000-0000-0003-000000000001"), Name = "servers:read", Category = "servers", Description = "View servers", IsSystemClaim = true },
            new() { Id = Guid.Parse("00000000-0000-0000-0003-000000000002"), Name = "servers:write", Category = "servers", Description = "Create and update servers", IsSystemClaim = true },
            new() { Id = Guid.Parse("00000000-0000-0000-0003-000000000003"), Name = "servers:delete", Category = "servers", Description = "Delete servers", IsSystemClaim = true },
            new() { Id = Guid.Parse("00000000-0000-0000-0003-000000000004"), Name = "servers:start", Category = "servers", Description = "Start servers", IsSystemClaim = true },
            new() { Id = Guid.Parse("00000000-0000-0000-0003-000000000005"), Name = "servers:stop", Category = "servers", Description = "Stop servers", IsSystemClaim = true },
            new() { Id = Guid.Parse("00000000-0000-0000-0003-000000000006"), Name = "servers:restart", Category = "servers", Description = "Restart servers", IsSystemClaim = true },

            // Node management claims
            new() { Id = Guid.Parse("00000000-0000-0000-0004-000000000001"), Name = "nodes:read", Category = "nodes", Description = "View nodes", IsSystemClaim = true },
            new() { Id = Guid.Parse("00000000-0000-0000-0004-000000000002"), Name = "nodes:manage", Category = "nodes", Description = "Manage node configuration", IsSystemClaim = true },

            // File management claims
            new() { Id = Guid.Parse("00000000-0000-0000-0005-000000000001"), Name = "files:read", Category = "files", Description = "View and download files", IsSystemClaim = true },
            new() { Id = Guid.Parse("00000000-0000-0000-0005-000000000002"), Name = "files:write", Category = "files", Description = "Upload and modify files", IsSystemClaim = true },
            new() { Id = Guid.Parse("00000000-0000-0000-0005-000000000003"), Name = "files:delete", Category = "files", Description = "Delete files", IsSystemClaim = true },

            // Mod management claims
            new() { Id = Guid.Parse("00000000-0000-0000-0006-000000000001"), Name = "mods:read", Category = "mods", Description = "View mods", IsSystemClaim = true },
            new() { Id = Guid.Parse("00000000-0000-0000-0006-000000000002"), Name = "mods:write", Category = "mods", Description = "Install and update mods", IsSystemClaim = true },
            new() { Id = Guid.Parse("00000000-0000-0000-0006-000000000003"), Name = "mods:delete", Category = "mods", Description = "Uninstall mods", IsSystemClaim = true },
        };

        modelBuilder.Entity<ClaimDefinition>().HasData(claims);
    }
}
```

---

## Authentication Flow

### 1. User Login via Better Auth (Social OAuth)

```
┌────────────┐                                              ┌──────────────┐
│   User     │                                              │ Better Auth  │
│  (Browser) │                                              │  (Node.js)   │
└──────┬─────┘                                              └──────┬───────┘
       │                                                            │
       │ 1. Click "Login with Discord"                             │
       ├───────────────────────────────────────────────────────────►
       │                                                            │
       │ 2. Redirect to Discord OAuth                              │
       │◄───────────────────────────────────────────────────────────┤
       │                                                            │
       │ 3. User authorizes on Discord                             │
       │                                                            │
       │ 4. Discord redirects back with code                       │
       ├───────────────────────────────────────────────────────────►
       │                                                            │
       │ 5. Better Auth exchanges code for tokens                  │
       │    Creates/updates session                                │
       │    Generates 60s exchange token (ES256)                   │
       │                                                            │
       │ 6. Returns session + X-Exchange-Token header              │
       │◄───────────────────────────────────────────────────────────┤
       │                                                            │
       │ 7. POST /api/auth/exchange                                │
       │    Body: { exchangeToken }                                │
       ├───────────────────────────────────────►┌────────────────────┐
       │                                        │  .NET Identity     │
       │                                        │   Service          │
       │                                        └─────────┬──────────┘
       │                                                  │
       │                                        8. Validate exchange token
       │                                           - ES256 signature
       │                                           - Issuer/audience
       │                                           - Expiry (60s)
       │                                                  │
       │                                        9. Check Redis (SETNX)
       │                                           - Ensure single-use
       │                                                  │
       │                                        10. DB Transaction:
       │                                           - Upsert user
       │                                           - Load/create org
       │                                           - Calculate perms
       │                                                  │
       │                                        11. Issue JWT
       │                                           - Access: 15min
       │                                           - Refresh: 7 days
       │                                           - ES256 signed
       │                                                  │
       │ 12. Return tokens                                │
       │    { accessToken, refreshToken, expiresIn }      │
       │◄─────────────────────────────────────────────────┤
       │                                                  │
       │ 13. Store tokens (httpOnly cookie)              │
       │     Ready to make API calls                      │
       │                                                  │
```

### 2. User Login via Gaming OAuth (Steam)

```
┌────────────┐                                              ┌──────────────┐
│   User     │                                              │ .NET Identity│
│  (Browser) │                                              │   Service    │
└──────┬─────┘                                              └──────┬───────┘
       │                                                            │
       │ 1. Click "Login with Steam"                               │
       ├───────────────────────────────────────────────────────────►
       │                                                            │
       │ 2. Redirect to Steam OpenID 2.0                           │
       │◄───────────────────────────────────────────────────────────┤
       │                                                            │
       │ 3. User authorizes on Steam                               │
       │                                                            │
       │ 4. Steam redirects back                                   │
       ├───────────────────────────────────────────────────────────►
       │                                                            │
       │ 5. .NET validates Steam response                          │
       │    Creates LinkedAccount for Steam                        │
       │    Upsert user, load org, calculate perms                 │
       │                                                            │
       │ 6. Issue JWT (access + refresh)                           │
       │◄───────────────────────────────────────────────────────────┤
       │                                                            │
       │ 7. Ready to make API calls                                │
       │                                                            │
```

### 3. API Request with JWT

```
┌────────────┐       ┌──────────┐       ┌──────────┐       ┌──────────┐
│   Client   │       │ Gateway  │       │ Identity │       │ Servers  │
│  (Browser) │       │  (YARP)  │       │ Service  │       │ Service  │
└──────┬─────┘       └────┬─────┘       └────┬─────┘       └────┬─────┘
       │                  │                   │                   │
       │ GET /servers     │                   │                   │
       │ Authorization:   │                   │                   │
       │  Bearer <JWT>    │                   │                   │
       ├─────────────────►│                   │                   │
       │                  │                   │                   │
       │                  │ 1. Validate JWT   │                   │
       │                  │    (JWKS cache)   │                   │
       │                  │                   │                   │
       │                  │ 2. Strip headers  │                   │
       │                  │    X-User-Id      │                   │
       │                  │    X-Org-Id       │                   │
       │                  │                   │                   │
       │                  │ 3. Inject claims  │                   │
       │                  │    X-User-Id: <id>│                   │
       │                  │    X-Org-Id: <id> │                   │
       │                  │    X-User-Perms   │                   │
       │                  │                   │                   │
       │                  │ 4. Proxy request  │                   │
       │                  ├───────────────────────────────────────►
       │                  │                   │                   │
       │                  │                   │ 5. Trust headers  │
       │                  │                   │    Check perms    │
       │                  │                   │    Exec query     │
       │                  │                   │                   │
       │                  │ 6. Response       │                   │
       │                  │◄───────────────────────────────────────┤
       │                  │                   │                   │
       │ 7. Response      │                   │                   │
       │◄─────────────────┤                   │                   │
       │                  │                   │                   │
```

### 4. Token Refresh Flow

```
┌────────────┐                                              ┌──────────────┐
│   Client   │                                              │ .NET Identity│
│  (Browser) │                                              │   Service    │
└──────┬─────┘                                              └──────┬───────┘
       │                                                            │
       │ 1. Access token expires (15 min)                          │
       │                                                            │
       │ 2. POST /api/auth/refresh                                 │
       │    Body: { refreshToken }                                 │
       ├───────────────────────────────────────────────────────────►
       │                                                            │
       │ 3. Hash refresh token (SHA256)                            │
       │    Lookup in database                                     │
       │    - Verify not revoked                                   │
       │    - Verify not expired                                   │
       │                                                            │
       │ 4. Check if permissions changed                           │
       │    - If unchanged: reissue with same claims (fast)        │
       │    - If changed: recalculate permissions (slow)           │
       │                                                            │
       │ 5. Issue new access token                                 │
       │    (optionally rotate refresh token)                      │
       │                                                            │
       │ 6. Return tokens                                          │
       │◄───────────────────────────────────────────────────────────┤
       │                                                            │
```

---

## Authorization Model

### Hybrid Roles + Claims

**Philosophy:** Roles provide convenient groupings of claims, but claims are the authorization primitive.

**Flow:**
1. User is assigned a **role** within an organization (owner, admin, operator, viewer)
2. Role **implies** a set of claims (defined in `RoleDefinitions`)
3. User can have **custom grant** claims (add permissions beyond role)
4. User can have **custom deny** claims (revoke permissions even if role-implied)

**Effective Permissions = (Role-Implied Claims) + (Grant Claims) - (Deny Claims)**

### Role Definitions

```csharp
using Dhadgar.Identity.Data.Entities;

namespace Dhadgar.Identity.Authorization;

public class RoleDefinition
{
    public string Name { get; init; } = null!;
    public string Description { get; init; } = null!;
    public IReadOnlyList<string> ImpliedClaims { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> CanAssignRoles { get; init; } = Array.Empty<string>();
}

public static class RoleDefinitions
{
    public static readonly IReadOnlyDictionary<string, RoleDefinition> Roles = new Dictionary<string, RoleDefinition>
    {
        ["owner"] = new RoleDefinition
        {
            Name = "Owner",
            Description = "Full control over organization",
            ImpliedClaims = new[]
            {
                // Organization
                "org:read", "org:write", "org:delete", "org:billing",
                // Members
                "members:read", "members:invite", "members:remove", "members:roles",
                // Servers
                "servers:read", "servers:write", "servers:delete",
                "servers:start", "servers:stop", "servers:restart",
                // Nodes
                "nodes:read", "nodes:manage",
                // Files
                "files:read", "files:write", "files:delete",
                // Mods
                "mods:read", "mods:write", "mods:delete",
            },
            CanAssignRoles = new[] { "admin", "operator", "viewer" }
        },

        ["admin"] = new RoleDefinition
        {
            Name = "Administrator",
            Description = "Manage servers and members",
            ImpliedClaims = new[]
            {
                // Organization (read-only)
                "org:read",
                // Members
                "members:read", "members:invite", "members:remove",
                // Servers
                "servers:read", "servers:write", "servers:delete",
                "servers:start", "servers:stop", "servers:restart",
                // Nodes
                "nodes:read",
                // Files
                "files:read", "files:write", "files:delete",
                // Mods
                "mods:read", "mods:write", "mods:delete",
            },
            CanAssignRoles = new[] { "operator", "viewer" }
        },

        ["operator"] = new RoleDefinition
        {
            Name = "Operator",
            Description = "Operate servers and manage files",
            ImpliedClaims = new[]
            {
                // Organization (read-only)
                "org:read",
                // Members (read-only)
                "members:read",
                // Servers
                "servers:read", "servers:write",
                "servers:start", "servers:stop", "servers:restart",
                // Nodes (read-only)
                "nodes:read",
                // Files
                "files:read", "files:write",
                // Mods
                "mods:read", "mods:write",
            },
            CanAssignRoles = Array.Empty<string>()
        },

        ["viewer"] = new RoleDefinition
        {
            Name = "Viewer",
            Description = "Read-only access",
            ImpliedClaims = new[]
            {
                "org:read",
                "members:read",
                "servers:read",
                "nodes:read",
                "files:read",
                "mods:read",
            },
            CanAssignRoles = Array.Empty<string>()
        }
    };

    public static RoleDefinition GetRole(string roleName)
    {
        return Roles.TryGetValue(roleName, out var role)
            ? role
            : Roles["viewer"]; // Default to viewer
    }

    public static bool IsValidRole(string roleName)
    {
        return Roles.ContainsKey(roleName);
    }

    public static bool CanAssignRole(string assignerRole, string targetRole)
    {
        var definition = GetRole(assignerRole);
        return definition.CanAssignRoles.Contains(targetRole);
    }
}
```

### Permission Calculation Service

```csharp
using Dhadgar.Identity.Data;
using Dhadgar.Identity.Data.Entities;
using Microsoft.EntityFrameworkCore;
using System.Collections.Immutable;

namespace Dhadgar.Identity.Services;

public interface IPermissionService
{
    Task<IReadOnlySet<string>> CalculatePermissionsAsync(Guid userId, Guid organizationId, CancellationToken ct = default);
    Task<bool> HasPermissionAsync(Guid userId, Guid organizationId, string claim, CancellationToken ct = default);
    Task<bool> HavePermissionsChangedAsync(Guid userId, Guid organizationId, DateTime since, CancellationToken ct = default);
}

public class PermissionService : IPermissionService
{
    private readonly IdentityDbContext _context;
    private readonly ILogger<PermissionService> _logger;

    public PermissionService(IdentityDbContext context, ILogger<PermissionService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<IReadOnlySet<string>> CalculatePermissionsAsync(
        Guid userId,
        Guid organizationId,
        CancellationToken ct = default)
    {
        var membership = await _context.UserOrganizations
            .Include(uo => uo.CustomClaims.Where(c => c.ExpiresAt == null || c.ExpiresAt > DateTime.UtcNow))
            .FirstOrDefaultAsync(uo =>
                uo.UserId == userId &&
                uo.OrganizationId == organizationId &&
                uo.IsActive &&
                uo.LeftAt == null, ct);

        if (membership == null)
        {
            _logger.LogWarning("User {UserId} has no active membership in organization {OrgId}", userId, organizationId);
            return ImmutableHashSet<string>.Empty;
        }

        // Start with role-implied claims
        var roleDefinition = RoleDefinitions.GetRole(membership.Role);
        var permissions = new HashSet<string>(roleDefinition.ImpliedClaims);

        // Add custom grants
        foreach (var claim in membership.CustomClaims.Where(c => c.ClaimType == ClaimType.Grant))
        {
            permissions.Add(claim.ClaimValue);
        }

        // Remove explicit denials
        foreach (var claim in membership.CustomClaims.Where(c => c.ClaimType == ClaimType.Deny))
        {
            permissions.Remove(claim.ClaimValue);
        }

        _logger.LogDebug("Calculated {Count} permissions for user {UserId} in org {OrgId}",
            permissions.Count, userId, organizationId);

        return permissions.ToImmutableHashSet();
    }

    public async Task<bool> HasPermissionAsync(
        Guid userId,
        Guid organizationId,
        string claim,
        CancellationToken ct = default)
    {
        var permissions = await CalculatePermissionsAsync(userId, organizationId, ct);
        return permissions.Contains(claim);
    }

    public async Task<bool> HavePermissionsChangedAsync(
        Guid userId,
        Guid organizationId,
        DateTime since,
        CancellationToken ct = default)
    {
        // Check if role changed or custom claims added/modified since timestamp
        var changed = await _context.UserOrganizations
            .Where(uo =>
                uo.UserId == userId &&
                uo.OrganizationId == organizationId &&
                (uo.UpdatedAt > since ||
                 uo.CustomClaims.Any(c => c.GrantedAt > since)))
            .AnyAsync(ct);

        return changed;
    }
}
```

### ASP.NET Core Authorization Policies

```csharp
using Microsoft.AspNetCore.Authorization;

namespace Dhadgar.Identity.Authorization;

public static class AuthorizationPolicyExtensions
{
    public static IServiceCollection AddDhadgarAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            // Organization management
            options.AddPolicy("CanManageOrg", policy =>
                policy.RequireClaim("permission", "org:write"));

            options.AddPolicy("CanDeleteOrg", policy =>
                policy.RequireClaim("permission", "org:delete"));

            options.AddPolicy("CanManageBilling", policy =>
                policy.RequireClaim("permission", "org:billing"));

            // Member management
            options.AddPolicy("CanInviteMembers", policy =>
                policy.RequireClaim("permission", "members:invite"));

            options.AddPolicy("CanRemoveMembers", policy =>
                policy.RequireClaim("permission", "members:remove"));

            options.AddPolicy("CanAssignRoles", policy =>
                policy.RequireClaim("permission", "members:roles"));

            // Server management
            options.AddPolicy("CanManageServers", policy =>
                policy.RequireClaim("permission", "servers:write"));

            options.AddPolicy("CanDeleteServers", policy =>
                policy.RequireClaim("permission", "servers:delete"));

            options.AddPolicy("CanStartServers", policy =>
                policy.RequireClaim("permission", "servers:start"));

            // Node management
            options.AddPolicy("CanManageNodes", policy =>
                policy.RequireClaim("permission", "nodes:manage"));

            // File management
            options.AddPolicy("CanUploadFiles", policy =>
                policy.RequireClaim("permission", "files:write"));

            options.AddPolicy("CanDeleteFiles", policy =>
                policy.RequireClaim("permission", "files:delete"));

            // Mod management
            options.AddPolicy("CanInstallMods", policy =>
                policy.RequireClaim("permission", "mods:write"));
        });

        return services;
    }
}
```

---

## Implementation Phases

### Phase 1: Foundation (Week 1)

**Objective:** Establish security perimeter and core database

**Tasks:**
1. ✅ Create database schema and migrations
2. ✅ Implement Gateway JWT validation
3. ✅ Set up OpenIddict with persistent certificates
4. ✅ Implement token exchange endpoint (ES256, with transaction)
5. ✅ Configure Redis for single-use token tracking

**Deliverables:**
- EF Core migrations applied
- Gateway validates JWTs and injects headers
- Token exchange endpoint working end-to-end
- Unit tests for permission calculation

**Files to Create/Modify:**
- `src/Dhadgar.Identity/Data/Entities/*.cs` (7 entities)
- `src/Dhadgar.Identity/Data/Configuration/*.cs` (7 configurations)
- `src/Dhadgar.Identity/Data/IdentityDbContext.cs`
- `src/Dhadgar.Identity/Data/Migrations/*.cs` (generated)
- `src/Dhadgar.Gateway/Program.cs` (add JWT validation)
- `src/Dhadgar.Identity/Program.cs` (OpenIddict setup)
- `src/Dhadgar.Identity/Endpoints/TokenExchangeEndpoint.cs`

---

### Phase 2: OAuth Providers (Week 2)

**Objective:** Implement gaming OAuth handlers

**Tasks:**
1. ✅ Steam OAuth (OpenID 2.0 via aspnet-contrib)
2. ✅ Battle.net OAuth (via aspnet-contrib)
3. ✅ Epic Games custom OAuth handler
4. ✅ Xbox Live OAuth (Microsoft Account)
5. ✅ Account linking flow (link additional providers to existing user)

**Deliverables:**
- All gaming OAuth providers working
- Account linking endpoints functional
- Integration tests for OAuth flows

**Files to Create/Modify:**
- `src/Dhadgar.Identity/OAuth/EpicGamesOAuthHandler.cs`
- `src/Dhadgar.Identity/Endpoints/OAuthEndpoints.cs`
- `src/Dhadgar.Identity/Program.cs` (register OAuth handlers)
- `tests/Dhadgar.Identity.Tests/OAuth/*.cs`

---

### Phase 3: Authorization & Organizations (Week 3)

**Objective:** Multi-tenant claims system

**Tasks:**
1. ✅ Implement permission calculation service
2. ✅ Organization management endpoints (CRUD)
3. ✅ Member invitation endpoints
4. ✅ Role assignment endpoints
5. ✅ Custom claim grant/deny endpoints
6. ✅ Organization switching endpoint

**Deliverables:**
- REST API for organizations and members
- Permission service with role + claim logic
- Organization switching working
- Integration tests for authorization

**Files to Create/Modify:**
- `src/Dhadgar.Identity/Services/PermissionService.cs`
- `src/Dhadgar.Identity/Services/OrganizationService.cs`
- `src/Dhadgar.Identity/Services/MembershipService.cs`
- `src/Dhadgar.Identity/Endpoints/OrganizationEndpoints.cs`
- `src/Dhadgar.Identity/Endpoints/MembershipEndpoints.cs`
- `src/Dhadgar.Identity/Authorization/RoleDefinitions.cs`

---

### Phase 4: Integration & Events (Week 4)

**Objective:** Service integration and observability

**Tasks:**
1. ✅ MassTransit event publishing (UserAuthenticated, OrgMembershipChanged, UserDeactivated)
2. ✅ Better Auth webhook receiver
3. ✅ Service-to-service authentication (Client Credentials flow)
4. ✅ OpenTelemetry distributed tracing
5. ✅ Rate limiting on auth endpoints

**Deliverables:**
- Identity events published to RabbitMQ
- Better Auth webhooks integrated
- Other services can authenticate via Client Credentials
- Distributed tracing working across Gateway → Identity
- Rate limiting protecting auth endpoints

**Files to Create/Modify:**
- `src/Shared/Dhadgar.Contracts/Identity/IdentityEvents.cs`
- `src/Dhadgar.Identity/Consumers/*.cs` (if consuming events)
- `src/Dhadgar.Identity/Endpoints/WebhookEndpoint.cs`
- `src/Dhadgar.Identity/Program.cs` (MassTransit, OpenTelemetry, rate limiting)
- `src/Dhadgar.ServiceDefaults/ServiceAuthenticationHandler.cs`

---

### Phase 5: Testing & Hardening (Week 5-6)

**Objective:** Production readiness

**Tasks:**
1. ✅ Integration tests for full auth flow
2. ✅ Load testing (token exchange, JWT validation)
3. ✅ Security audit (SQL injection, XSS, CSRF)
4. ✅ Error handling and logging
5. ✅ Documentation (API docs, deployment guide)

**Deliverables:**
- 80%+ test coverage
- Performance benchmarks (target: <100ms token exchange)
- Security audit report
- Deployment runbook

---

## Code Specifications

### Critical Security Requirements

#### 1. Exchange Token (Better Auth → .NET)

**MUST use ES256 asymmetric signing:**

```typescript
// Better Auth (Astro/Node.js)
import { SignJWT } from "jose";

const EXCHANGE_TOKEN_PRIVATE_KEY = await importPKCS8(
  process.env.EXCHANGE_TOKEN_PRIVATE_KEY!,
  "ES256"
);

hooks: {
  after: [{
    handler: async (ctx) => {
      const exchangeToken = await new SignJWT({
        sub: user.id,
        email: user.email,
        provider: account?.providerId,
        provider_user_id: account?.providerAccountId,
        purpose: "token_exchange",
        aud: "https://identity.meridian.gg/api/auth/exchange",
        iss: "https://auth.meridian.gg",
      })
        .setProtectedHeader({ alg: "ES256" })
        .setIssuedAt()
        .setExpirationTime("60s")
        .setJti(crypto.randomUUID())
        .sign(EXCHANGE_TOKEN_PRIVATE_KEY);

      ctx.context.responseHeaders?.set("X-Exchange-Token", exchangeToken);
    }
  }]
}
```

```csharp
// .NET Identity - Validate exchange token
var tokenHandler = new JsonWebTokenHandler();

var result = await tokenHandler.ValidateTokenAsync(exchangeToken, new TokenValidationParameters
{
    ValidateIssuer = true,
    ValidIssuer = "https://auth.meridian.gg",

    ValidateAudience = true,
    ValidAudience = "https://identity.meridian.gg/api/auth/exchange",

    ValidateLifetime = true,
    ClockSkew = TimeSpan.FromSeconds(30),

    ValidateIssuerSigningKey = true,
    IssuerSigningKey = betterAuthPublicKey, // ES256 public key from JWKS
});

if (!result.IsValid)
{
    return Results.Unauthorized();
}
```

#### 2. Redis Single-Use Token Enforcement

```csharp
// Ensure exchange token can only be used once
var redis = connectionMultiplexer.GetDatabase();
var jti = result.ClaimsIdentity.FindFirst("jti")?.Value;

if (string.IsNullOrEmpty(jti))
{
    return Results.BadRequest(new { error = "missing_jti" });
}

var key = $"exchange_token:{jti}";
var wasSet = await redis.StringSetAsync(key, "used", TimeSpan.FromMinutes(2), when: When.NotExists);

if (!wasSet)
{
    _logger.LogWarning("Exchange token replay attempt detected: {Jti}", jti);
    return Results.BadRequest(new { error = "token_already_used" });
}
```

#### 3. Transaction Boundary for Token Exchange

```csharp
// CRITICAL: Wrap user upsert + org operations in transaction
await using var transaction = await _dbContext.Database.BeginTransactionAsync();

try
{
    var user = await _userService.UpsertFromExternalProviderAsync(externalUserInfo);
    var memberships = await _userService.GetActiveMembershipsAsync(user.Id);

    Organization defaultOrg;
    if (!memberships.Any())
    {
        defaultOrg = await _organizationService.CreateDefaultOrganizationAsync(user.Id);
    }
    else
    {
        defaultOrg = await ResolveActiveOrganizationAsync(user.Id, memberships);
    }

    var permissions = await _permissionService.CalculatePermissionsAsync(user.Id, defaultOrg.Id);

    await transaction.CommitAsync();

    // JWT issuance OUTSIDE transaction (idempotent, retry-safe)
    var claims = BuildClaims(user, defaultOrg, permissions);
    var (accessToken, refreshToken, expiresIn) = await _jwtService.GenerateTokenPairAsync(claims);

    return Results.Ok(new { accessToken, refreshToken, expiresIn });
}
catch (Exception ex)
{
    await transaction.RollbackAsync();
    _logger.LogError(ex, "Token exchange failed for external user {ExternalId}", externalUserInfo.ExternalAuthId);
    throw;
}
```

#### 4. OpenIddict Configuration (Persistent Certificates)

```csharp
builder.Services.AddOpenIddict()
    .AddCore(options =>
    {
        options.UseEntityFrameworkCore()
            .UseDbContext<IdentityDbContext>();
    })
    .AddServer(options =>
    {
        // CRITICAL: No leading slashes
        options.SetAuthorizationEndpointUris("connect/authorize")
            .SetTokenEndpointUris("connect/token")
            .SetUserInfoEndpointUris("connect/userinfo") // Note: capital 'I'
            .SetIntrospectionEndpointUris("connect/introspect")
            .SetRevocationEndpointUris("connect/revocation");

        // JWKS endpoint for public key distribution
        options.SetJsonWebKeySetEndpointUris(".well-known/jwks.json");

        // Enable flows
        options.AllowAuthorizationCodeFlow()
            .RequireProofKeyForCodeExchange() // PKCE required
            .AllowClientCredentialsFlow()
            .AllowRefreshTokenFlow();

        // Register scopes
        options.RegisterScopes(
            OpenIddictConstants.Scopes.OpenId,
            OpenIddictConstants.Scopes.Profile,
            OpenIddictConstants.Scopes.Email,
            "servers:read",
            "servers:write",
            "nodes:manage",
            "billing:read"
        );

        // CRITICAL: Persistent certificates for production
        if (builder.Environment.IsProduction())
        {
            var signingCert = LoadCertificateFromKeyVault("identity-signing-cert");
            var encryptionCert = LoadCertificateFromKeyVault("identity-encryption-cert");

            options.AddSigningCertificate(signingCert)
                .AddEncryptionCertificate(encryptionCert);
        }
        else
        {
            // Development only
            options.AddEphemeralEncryptionKey();
            options.AddEphemeralSigningKey();
        }

        options.UseAspNetCore()
            .EnableAuthorizationEndpointPassthrough()
            .EnableTokenEndpointPassthrough()
            .EnableUserInfoEndpointPassthrough();
    })
    .AddValidation(options =>
    {
        options.UseLocalServer();
        options.UseAspNetCore();
    });
```

#### 5. Gateway JWT Validation

```csharp
// src/Dhadgar.Gateway/Program.cs
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Yarp.ReverseProxy.Transforms;

var builder = WebApplication.CreateBuilder(args);

// JWT Bearer authentication
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = builder.Configuration["Auth:Issuer"]; // https://identity.meridian.gg
        options.Audience = builder.Configuration["Auth:Audience"]; // meridian-api
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };

        // JWKS caching
        options.BackchannelHttpHandler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        };
        options.RefreshOnIssuerKeyNotFound = true;
    });

builder.Services.AddAuthorization();

// YARP with transforms
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddTransforms(context =>
    {
        // SECURITY: Strip incoming identity headers
        context.AddRequestTransform(ctx =>
        {
            var dangerousHeaders = new[]
            {
                "X-User-Id", "X-Org-Id", "X-User-Permissions",
                "X-User-Roles", "X-Authenticated"
            };
            foreach (var header in dangerousHeaders)
            {
                ctx.ProxyRequest.Headers.Remove(header);
            }
            return ValueTask.CompletedTask;
        });

        // Inject validated claims as headers
        context.AddRequestTransform(ctx =>
        {
            var user = ctx.HttpContext.User;
            if (user.Identity?.IsAuthenticated == true)
            {
                ctx.ProxyRequest.Headers.Add("X-User-Id",
                    user.FindFirst("sub")?.Value ?? "");
                ctx.ProxyRequest.Headers.Add("X-Org-Id",
                    user.FindFirst("org_id")?.Value ?? "");
                ctx.ProxyRequest.Headers.Add("X-User-Permissions",
                    string.Join(",", user.FindAll("permission").Select(c => c.Value)));
                ctx.ProxyRequest.Headers.Add("X-Authenticated", "true");
            }
            return ValueTask.CompletedTask;
        });
    });

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapReverseProxy();

app.Run();
```

#### 6. JWT Generation (ES256)

```csharp
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using System.Security.Cryptography;

namespace Dhadgar.Identity.Services;

public interface IJwtService
{
    Task<(string AccessToken, string RefreshToken, int ExpiresIn)> GenerateTokenPairAsync(
        IEnumerable<Claim> claims,
        CancellationToken ct = default);

    Task<TokenValidationResult> ValidateAccessTokenAsync(string token, CancellationToken ct = default);
}

public class JwtService : IJwtService
{
    private readonly ECDsa _signingKey;
    private readonly JsonWebTokenHandler _tokenHandler;
    private readonly TimeProvider _timeProvider;
    private readonly IConfiguration _config;
    private readonly ILogger<JwtService> _logger;

    public JwtService(
        IConfiguration config,
        TimeProvider timeProvider,
        ILogger<JwtService> logger)
    {
        _config = config;
        _timeProvider = timeProvider;
        _logger = logger;
        _tokenHandler = new JsonWebTokenHandler();

        // Load or create ES256 key
        var keyPath = config["Auth:SigningKeyPath"];
        if (File.Exists(keyPath))
        {
            _signingKey = ECDsa.Create();
            _signingKey.ImportFromPem(File.ReadAllText(keyPath));
        }
        else
        {
            _signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            _logger.LogWarning("Generating ephemeral ES256 key - not suitable for production");
        }
    }

    public async Task<(string AccessToken, string RefreshToken, int ExpiresIn)> GenerateTokenPairAsync(
        IEnumerable<Claim> claims,
        CancellationToken ct = default)
    {
        var now = _timeProvider.GetUtcNow();
        var expiresIn = 900; // 15 minutes

        var signingCredentials = new SigningCredentials(
            new ECDsaSecurityKey(_signingKey),
            SecurityAlgorithms.EcdsaSha256);

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = now.AddSeconds(expiresIn).UtcDateTime,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Issuer = _config["Auth:Issuer"], // https://identity.meridian.gg
            Audience = _config["Auth:Audience"], // meridian-api
            SigningCredentials = signingCredentials
        };

        var accessToken = _tokenHandler.CreateToken(descriptor);
        var refreshToken = GenerateRefreshToken();

        _logger.LogDebug("Generated JWT for user {UserId}", claims.FirstOrDefault(c => c.Type == "sub")?.Value);

        return (accessToken, refreshToken, expiresIn);
    }

    public async Task<TokenValidationResult> ValidateAccessTokenAsync(string token, CancellationToken ct = default)
    {
        var result = await _tokenHandler.ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = _config["Auth:Issuer"],

            ValidateAudience = true,
            ValidAudience = _config["Auth:Audience"],

            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),

            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new ECDsaSecurityKey(_signingKey.ExportParameters(false))
        });

        return result;
    }

    private string GenerateRefreshToken()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
    }

    public ECDsaSecurityKey GetPublicKey()
    {
        return new ECDsaSecurityKey(_signingKey.ExportParameters(false));
    }
}
```

#### 7. Rate Limiting

```csharp
// Add to Program.cs
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Token exchange - strict limit
    options.AddPolicy("token-exchange", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

    // General auth endpoints
    options.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetSlidingWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                SegmentsPerWindow = 6,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 2
            }));
});

// Apply to endpoints
app.MapPost("/api/auth/exchange", HandleExchange)
    .RequireRateLimiting("token-exchange");

app.MapPost("/api/auth/refresh", HandleRefresh)
    .RequireRateLimiting("auth");
```

---

## Security Requirements

### TLS/HTTPS

- ✅ All external communication over HTTPS (enforced at Cloudflare edge)
- ✅ Internal service mesh with mTLS (future: via Linkerd or Istio)
- ✅ Certificate rotation automated via cert-manager (Kubernetes)

### Secrets Management

- ✅ Development: `dotnet user-secrets`
- ✅ Production: Azure Key Vault
- ✅ Never commit secrets to git
- ✅ Rotate signing keys every 90 days

### Input Validation

- ✅ All user input validated via ASP.NET Core ModelState
- ✅ Email validation via `[EmailAddress]` annotation
- ✅ SQL injection prevented via EF Core parameterized queries
- ✅ XSS prevented via ASP.NET Core automatic encoding

### Audit Logging

```csharp
// Log all security-sensitive operations
public class AuditLog
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? OrganizationId { get; set; }
    public string Action { get; set; } = null!; // "user.login", "org.member.removed"
    public string IpAddress { get; set; } = null!;
    public string UserAgent { get; set; } = null!;
    public DateTime Timestamp { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
}

// Log on sensitive operations
_logger.LogWarning("User {UserId} removed member {MemberId} from org {OrgId}",
    actorUserId, targetUserId, organizationId);
```

---

## Testing Strategy

### Unit Tests

```csharp
// Example: Permission calculation
[Fact]
public async Task CalculatePermissions_WithOwnerRole_ReturnsAllClaims()
{
    // Arrange
    var userId = Guid.NewGuid();
    var orgId = Guid.NewGuid();
    // ... setup test data

    // Act
    var permissions = await _permissionService.CalculatePermissionsAsync(userId, orgId);

    // Assert
    Assert.Contains("servers:delete", permissions);
    Assert.Contains("org:billing", permissions);
}

[Fact]
public async Task CalculatePermissions_WithDenyClaim_RemovesPermission()
{
    // Arrange: User with admin role but explicit deny on servers:delete
    // Act
    var permissions = await _permissionService.CalculatePermissionsAsync(userId, orgId);

    // Assert
    Assert.DoesNotContain("servers:delete", permissions);
}
```

### Integration Tests

```csharp
[Fact]
public async Task TokenExchange_WithValidExchangeToken_ReturnsJWT()
{
    // Arrange
    var exchangeToken = GenerateMockExchangeToken();
    var client = _factory.CreateClient();

    // Act
    var response = await client.PostAsJsonAsync("/api/auth/exchange", new { exchangeToken });

    // Assert
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var result = await response.Content.ReadFromJsonAsync<TokenResponse>();
    Assert.NotNull(result.AccessToken);
    Assert.NotNull(result.RefreshToken);
}

[Fact]
public async Task TokenExchange_WithReplayedToken_Returns400()
{
    // Arrange: Use same exchange token twice
    // Act & Assert: Second request should fail
}
```

---

## Deployment Considerations

### Environment Variables

```bash
# .NET Identity Service
Auth__Issuer=https://identity.meridian.gg
Auth__Audience=meridian-api
Auth__SigningKeyPath=/secrets/identity-signing-key.pem

ConnectionStrings__Postgres=Host=postgres;Database=dhadgar_identity;Username=dhadgar;Password=...
ConnectionStrings__Redis=redis:6379,password=...

RabbitMq__Host=rabbitmq
RabbitMq__Username=dhadgar
RabbitMq__Password=...

BetterAuth__PublicKeyUrl=https://auth.meridian.gg/.well-known/jwks.json

OAuth__Steam__ApiKey=...
OAuth__EpicGames__ClientId=...
OAuth__EpicGames__ClientSecret=...
```

### Kubernetes Deployment

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: identity
spec:
  replicas: 3
  selector:
    matchLabels:
      app: identity
  template:
    metadata:
      labels:
        app: identity
    spec:
      containers:
      - name: identity
        image: dhadgar/identity:latest
        ports:
        - containerPort: 8080
        env:
        - name: Auth__SigningKeyPath
          value: /secrets/identity-signing-key.pem
        volumeMounts:
        - name: signing-key
          mountPath: /secrets
          readOnly: true
        livenessProbe:
          httpGet:
            path: /healthz
            port: 8080
          initialDelaySeconds: 10
          periodSeconds: 10
        resources:
          requests:
            memory: "512Mi"
            cpu: "250m"
          limits:
            memory: "1Gi"
            cpu: "500m"
      volumes:
      - name: signing-key
        secret:
          secretName: identity-signing-key
```

---

## Additional Implemented Features

The following features have been implemented and are documented here for completeness.

### Audit Event Logging

The Identity service maintains an audit trail for security-relevant events.

**Entity: `AuditEvent`**

```csharp
public sealed class AuditEvent
{
    public Guid Id { get; set; }
    public Guid? UserId { get; set; }
    public Guid? OrganizationId { get; set; }
    public string EventType { get; set; }  // e.g., "login", "logout", "role_changed"
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? Details { get; set; }   // JSON for event-specific data
    public DateTime OccurredAt { get; set; }
}
```

**Tracked Events:**
- `login` - User authentication (token exchange)
- `logout` - Session revocation
- `organization_created` - New organization created
- `membership_invited` - User invited to organization
- `membership_accepted` - Invitation accepted
- `membership_removed` - User removed from organization
- `role_changed` - User's role changed
- `claim_added` - Custom claim granted
- `claim_removed` - Custom claim revoked
- `user_deleted` - User account deleted (via webhook)

**API Endpoint:**
- `GET /me/activity` - View current user's activity log (paginated)

---

### Session Management

Users can view and revoke active sessions across devices.

**Endpoints:**
- `GET /me/sessions` - List active sessions
- `DELETE /me/sessions/{sessionId}` - Revoke a specific session

**Session Data:**
```json
{
  "id": "session-id",
  "userAgent": "Mozilla/5.0...",
  "ipAddress": "192.168.1.1",
  "createdAt": "2026-01-16T12:00:00Z",
  "lastActiveAt": "2026-01-16T14:30:00Z",
  "isCurrent": true
}
```

**Implementation Notes:**
- Sessions are tracked via refresh tokens
- Revoking a session invalidates the associated refresh token
- Users can revoke all sessions except the current one
- Session activity is updated on each token refresh

---

### MFA Policy Management

Organizations can configure MFA requirements for their members.

**Endpoints:**
- `GET /organizations/{id}/mfa-policy` - Get current MFA policy
- `PATCH /organizations/{id}/mfa-policy` - Update MFA policy

**MFA Policy Options:**
```json
{
  "requireMfa": false,
  "allowedMethods": ["totp", "passkey", "sms"],
  "gracePeriodDays": 7,
  "exemptRoles": []
}
```

**Implementation Notes:**
- MFA policy is stored in `Organization.Settings`
- Enforcement is handled at the Better Auth layer
- The `.NET Identity service reads the policy for token claims
- Users with passkeys registered satisfy MFA requirements

---

### Invitation Management

Comprehensive invitation system with expiration and cleanup.

**Features:**
- Invitations expire after 7 days by default
- Users can view pending invitations via `GET /me/invitations`
- Inviters can withdraw invitations
- Invitees can reject invitations
- Background cleanup service marks expired invitations

**Invitation States:**
- `pending` - Invitation sent, awaiting response
- `accepted` - User joined the organization
- `rejected` - User declined the invitation
- `withdrawn` - Inviter revoked the invitation
- `expired` - Invitation expired (cleaned up by background service)

**Background Service: `InvitationCleanupService`**
- Runs periodically (configurable interval)
- Marks expired invitations as `LeftAt`
- Logs cleanup statistics

---

### Permission Caching

The `CachedPermissionService` provides in-memory caching of permission calculations to improve performance.

**Cache Behavior:**
- Permissions cached per user+organization combination
- Cache duration: 5 minutes (configurable)
- Cache invalidated on:
  - Role assignment changes
  - Custom claim additions/removals
  - Organization switch

**Implementation:**
```csharp
public interface ICachedPermissionService
{
    Task<IReadOnlySet<string>> GetPermissionsAsync(Guid userId, Guid organizationId, CancellationToken ct);
    void InvalidateCache(Guid userId, Guid organizationId);
}
```

---

### Search Functionality

Search endpoints for finding organizations, users, and roles.

**Endpoints:**
- `GET /organizations/search?query=` - Search user's organizations
- `GET /organizations/{id}/users/search?query=` - Search users in organization
- `GET /organizations/{id}/roles/search?query=` - Search roles

**Search Behavior:**
- Case-insensitive matching
- Searches name, slug, email, and display name fields
- Uses PostgreSQL `LIKE` with proper escaping
- Results limited to user's accessible resources

---

### Related Documentation

For detailed API specifications, see:
- [Identity API Reference](../identity-api-reference.md)
- [Identity Claims Reference](../identity-claims-reference.md)
- [Identity Webhooks](../identity-webhooks.md)
- [OAuth Provider Setup](../identity-oauth-providers.md)
- [Deployment Runbook](../runbooks/identity-service-deployment.md)

---

## Summary

This comprehensive implementation plan provides:

✅ **Complete architecture** - Hybrid Better Auth + .NET Identity with all integration points
✅ **Full database schema** - 7 entities with EF Core configurations and indexes
✅ **Security-first design** - ES256 signing, single-use tokens, transaction boundaries
✅ **Hybrid authorization** - Roles + claims with grant/deny overrides
✅ **Production-ready** - OpenIddict OIDC, persistent certificates, rate limiting
✅ **Observable** - OpenTelemetry tracing, structured logging, metrics
✅ **Testable** - TimeProvider injection, clear service boundaries

**Next Step:** Begin Phase 1 implementation (database schema + Gateway JWT validation).

All critical security issues from the agent review have been addressed in this final plan.
