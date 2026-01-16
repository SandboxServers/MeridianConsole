# Identity Claims Reference

This document describes the permission claims system used by the Dhadgar Identity service.

## Overview

The Identity service uses a **hybrid roles + claims** authorization model:

1. **Roles** provide a baseline set of permissions
2. **Claims** enable fine-grained permission control
3. **Custom Claims** can grant or deny specific permissions per user

## System Roles

Four built-in roles are available in every organization:

| Role | Description | Can Assign |
|------|-------------|------------|
| `owner` | Full control over the organization | admin, operator, viewer |
| `admin` | Manage servers, members, and resources | operator, viewer |
| `operator` | Operate servers and manage files | - |
| `viewer` | Read-only access to all resources | - |

### Role Hierarchy

```
owner
  └── admin
        └── operator
              └── viewer
```

Each role includes all permissions of roles below it, plus additional permissions.

## System Claims

The following claims are seeded in the database and available for all organizations:

### Organization Claims

| Claim | Description | Included In Roles |
|-------|-------------|-------------------|
| `org:read` | View organization details | owner, admin, operator, viewer |
| `org:write` | Update organization settings | owner |
| `org:delete` | Delete the organization | owner |
| `org:billing` | Manage billing and subscriptions | owner |

### Member Claims

| Claim | Description | Included In Roles |
|-------|-------------|-------------------|
| `members:read` | View organization members | owner, admin, operator, viewer |
| `members:invite` | Invite new members | owner, admin |
| `members:remove` | Remove members | owner, admin |
| `members:roles` | Assign roles to members | owner |

### Server Claims

| Claim | Description | Included In Roles |
|-------|-------------|-------------------|
| `servers:read` | View servers | owner, admin, operator, viewer |
| `servers:write` | Create and update servers | owner, admin, operator |
| `servers:delete` | Delete servers | owner, admin |
| `servers:start` | Start servers | owner, admin, operator |
| `servers:stop` | Stop servers | owner, admin, operator |
| `servers:restart` | Restart servers | owner, admin, operator |

### Node Claims

| Claim | Description | Included In Roles |
|-------|-------------|-------------------|
| `nodes:read` | View nodes | owner, admin, operator, viewer |
| `nodes:manage` | Manage node configuration | owner |

### File Claims

| Claim | Description | Included In Roles |
|-------|-------------|-------------------|
| `files:read` | View and download files | owner, admin, operator, viewer |
| `files:write` | Upload and modify files | owner, admin, operator |
| `files:delete` | Delete files | owner, admin |

### Mod Claims

| Claim | Description | Included In Roles |
|-------|-------------|-------------------|
| `mods:read` | View mods | owner, admin, operator, viewer |
| `mods:write` | Install and update mods | owner, admin, operator |
| `mods:delete` | Uninstall mods | owner, admin |

## Permission Calculation

When calculating a user's effective permissions:

1. Start with the role's implied claims
2. Add any `grant` custom claims
3. Remove any `deny` custom claims
4. Apply resource-scoped claims if applicable

### Example

```
User: alice@example.com
Role: operator
Custom Claims:
  - grant: servers:delete (for server-123 only)
  - deny: mods:write

Effective Permissions:
  - org:read
  - members:read
  - servers:read, servers:write, servers:start, servers:stop, servers:restart
  - servers:delete (only for server-123)
  - nodes:read
  - files:read, files:write
  - mods:read (mods:write denied by custom claim)
```

## Custom Claims

Custom claims allow fine-grained permission control:

### Claim Types

| Type | Description |
|------|-------------|
| `grant` | Explicitly allows a permission (additive) |
| `deny` | Explicitly denies a permission (overrides grants) |

### Scoping

Claims can be scoped to specific resources:

```json
{
  "claimType": "grant",
  "claimValue": "servers:delete",
  "resourceType": "server",
  "resourceId": "3fa85f64-5717-4562-b3fc-2c963f66afa6"
}
```

When `resourceType` and `resourceId` are specified, the claim only applies to that specific resource.

### Expiration

Claims can have an expiration date:

```json
{
  "claimType": "grant",
  "claimValue": "servers:delete",
  "expiresAt": "2026-12-31T23:59:59Z"
}
```

Expired claims are automatically excluded from permission calculations.

## Role Permissions Matrix

### Owner Role

```
org:read, org:write, org:delete, org:billing
members:read, members:invite, members:remove, members:roles
servers:read, servers:write, servers:delete, servers:start, servers:stop, servers:restart
nodes:read, nodes:manage
files:read, files:write, files:delete
mods:read, mods:write, mods:delete
```

### Admin Role

```
org:read
members:read, members:invite, members:remove
servers:read, servers:write, servers:delete, servers:start, servers:stop, servers:restart
nodes:read
files:read, files:write, files:delete
mods:read, mods:write, mods:delete
```

### Operator Role

```
org:read
members:read
servers:read, servers:write, servers:start, servers:stop, servers:restart
nodes:read
files:read, files:write
mods:read, mods:write
```

### Viewer Role

```
org:read
members:read
servers:read
nodes:read
files:read
mods:read
```

## Custom Roles

Organizations can create custom roles with specific claim sets:

```json
{
  "name": "Mod Manager",
  "description": "Can only manage mods",
  "claims": [
    "org:read",
    "mods:read",
    "mods:write",
    "mods:delete"
  ]
}
```

Custom roles:
- Cannot include claims not in the system claim registry
- Can be assigned by users with `members:roles` permission
- Are organization-specific

## JWT Claims

When a JWT is issued, it includes the following claims:

| Claim | Type | Description |
|-------|------|-------------|
| `sub` | string (GUID) | User ID |
| `email` | string | User's email |
| `email_verified` | boolean | Email verification status |
| `org_id` | string (GUID) | Current organization ID |
| `org_role` | string | Role in current organization |
| `permissions` | string[] | Array of permission claims |

### Example JWT Payload

```json
{
  "sub": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "email": "user@example.com",
  "email_verified": true,
  "org_id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "org_role": "admin",
  "permissions": [
    "org:read",
    "members:read",
    "members:invite",
    "servers:read",
    "servers:write",
    "servers:start",
    "servers:stop",
    "servers:restart"
  ],
  "iat": 1705402800,
  "exp": 1705403700,
  "iss": "https://meridianconsole.com/api/v1/identity",
  "aud": "meridian-api"
}
```

## API Usage

### Get User Permissions

```http
GET /me/permissions
Authorization: Bearer <token>
```

Response:
```json
{
  "organizationId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "role": "admin",
  "permissions": [
    "org:read",
    "members:read",
    "members:invite",
    "servers:read",
    "servers:write"
  ]
}
```

### Add Custom Claim

```http
POST /organizations/{orgId}/members/{memberId}/claims
Authorization: Bearer <token>
Content-Type: application/json

{
  "claimType": "grant",
  "claimValue": "servers:delete",
  "resourceType": "server",
  "resourceId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "expiresAt": "2026-12-31T23:59:59Z"
}
```

### Internal Permission Check

For service-to-service permission checks:

```http
POST /internal/permissions/check
Content-Type: application/json

{
  "userId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "organizationId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "permission": "servers:delete"
}
```

Response:
```json
{
  "hasPermission": true
}
```

## Best Practices

1. **Use roles first** - Assign roles that match the user's general responsibility level
2. **Use grants sparingly** - Only add grants when a role doesn't cover a specific need
3. **Prefer scoped claims** - Scope grants to specific resources when possible
4. **Use deny with caution** - Deny claims are powerful overrides; document why they're needed
5. **Set expirations** - For temporary access, always set an expiration date
6. **Review permissions regularly** - Audit custom claims periodically
