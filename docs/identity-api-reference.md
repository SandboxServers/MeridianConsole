# Identity Service API Reference

Complete API reference for the Dhadgar Identity service.

## Base URL

- **Development**: `http://localhost:5010` (direct) or `http://localhost:5000/api/v1/identity` (via Gateway)
- **Production**: `https://api.meridianconsole.com/api/v1/identity`

## Authentication

Most endpoints require a valid JWT Bearer token in the `Authorization` header:

```
Authorization: Bearer <your-jwt-token>
```

Tokens are obtained via the `/exchange` endpoint after authenticating through Better Auth.

## Endpoints

### Authentication

#### POST `/exchange`

Exchange a Better Auth exchange token for a JWT access token and refresh token.

**Authentication**: None (anonymous)

**Request Body**:
```json
{
  "exchange_token": "string"
}
```

**Response** `200 OK`:
```json
{
  "access_token": "eyJhbGciOiJFUzI1NiI...",
  "refresh_token": "rt_abc123...",
  "token_type": "Bearer",
  "expires_in": 900,
  "scope": "openid profile email"
}
```

**Error Responses**:
- `400 Bad Request` - Invalid or missing exchange token
- `401 Unauthorized` - Exchange token validation failed
- `429 Too Many Requests` - Rate limit exceeded

**Notes**:
- Exchange tokens are single-use and expire after 60 seconds
- Tokens are validated using ES256 asymmetric signature
- Redis is used for replay prevention

---

#### POST `/connect/token`

OpenIddict token endpoint for refresh token exchange.

**Authentication**: None

**Request Body** (form-urlencoded):
```
grant_type=refresh_token
refresh_token=rt_abc123...
```

**Response** `200 OK`:
```json
{
  "access_token": "eyJhbGciOiJFUzI1NiI...",
  "refresh_token": "rt_xyz789...",
  "token_type": "Bearer",
  "expires_in": 900
}
```

---

### Organizations

#### GET `/organizations`

List all organizations the current user belongs to.

**Authentication**: Required

**Response** `200 OK`:
```json
[
  {
    "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "name": "My Gaming Org",
    "slug": "my-gaming-org",
    "role": "owner",
    "isActive": true,
    "ownerId": "3fa85f64-5717-4562-b3fc-2c963f66afa6"
  }
]
```

---

#### POST `/organizations`

Create a new organization.

**Authentication**: Required

**Request Body**:
```json
{
  "name": "My New Org",
  "slug": "my-new-org"
}
```

**Response** `201 Created`:
```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "name": "My New Org",
  "slug": "my-new-org",
  "ownerId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "settings": {
    "maxMembers": 10,
    "allowMemberInvites": true,
    "requireEmailVerification": false
  },
  "createdAt": "2026-01-16T12:00:00Z"
}
```

**Error Responses**:
- `400 Bad Request` - Invalid name or slug
- `409 Conflict` - Slug already exists

---

#### GET `/organizations/{organizationId}`

Get organization details.

**Authentication**: Required (must be a member)

**Path Parameters**:
- `organizationId` (guid) - Organization ID

**Response** `200 OK`:
```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "name": "My Gaming Org",
  "slug": "my-gaming-org",
  "ownerId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "settings": {
    "maxMembers": 10,
    "allowMemberInvites": true,
    "requireEmailVerification": false,
    "customSettings": {}
  },
  "createdAt": "2026-01-01T00:00:00Z",
  "updatedAt": "2026-01-15T10:30:00Z"
}
```

---

#### PATCH `/organizations/{organizationId}`

Update organization details.

**Authentication**: Required (`org:write` permission)

**Path Parameters**:
- `organizationId` (guid) - Organization ID

**Request Body**:
```json
{
  "name": "Updated Org Name",
  "slug": "updated-slug",
  "settings": {
    "maxMembers": 25,
    "allowMemberInvites": false
  }
}
```

**Response** `200 OK`: Updated organization object

---

#### DELETE `/organizations/{organizationId}`

Soft-delete an organization.

**Authentication**: Required (`org:delete` permission - owner only)

**Response** `204 No Content`

---

#### POST `/organizations/{organizationId}/switch`

Switch to a different organization context. Issues new tokens with permissions for the target organization.

**Authentication**: Required (must be active member of target org)

**Rate Limiting**: `auth` policy

**Path Parameters**:
- `organizationId` (guid) - Target organization ID

**Response** `200 OK`:
```json
{
  "access_token": "eyJhbGciOiJFUzI1NiI...",
  "refresh_token": "rt_newtoken...",
  "organization": {
    "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "name": "Target Org",
    "role": "admin"
  }
}
```

---

#### POST `/organizations/{organizationId}/transfer-ownership`

Transfer organization ownership to another active member.

**Authentication**: Required (current owner only)

**Request Body**:
```json
{
  "newOwnerId": "3fa85f64-5717-4562-b3fc-2c963f66afa6"
}
```

**Response** `200 OK`

**Error Responses**:
- `400 Bad Request` - Cannot transfer to self
- `403 Forbidden` - Not the current owner
- `404 Not Found` - New owner not an active member

---

### Memberships

#### GET `/organizations/{organizationId}/members`

List all members in an organization.

**Authentication**: Required (`members:read` permission)

**Response** `200 OK`:
```json
[
  {
    "userId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "organizationId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "email": "user@example.com",
    "role": "admin",
    "isActive": true,
    "joinedAt": "2026-01-01T00:00:00Z"
  }
]
```

---

#### POST `/organizations/{organizationId}/members/invite`

Invite a user to the organization.

**Authentication**: Required (`members:invite` permission)

**Rate Limiting**: `invite` policy (5 invites per user per minute)

**Request Body**:
```json
{
  "userId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "email": "newuser@example.com",
  "role": "viewer"
}
```

Provide either `userId` or `email`, not both.

**Response** `201 Created`:
```json
{
  "membershipId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "userId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "role": "viewer",
  "isActive": false,
  "invitedAt": "2026-01-16T12:00:00Z",
  "expiresAt": "2026-01-23T12:00:00Z"
}
```

**Error Responses**:
- `400 Bad Request` - Invalid role or user not found
- `409 Conflict` - User already a member or has pending invite
- `422 Unprocessable Entity` - Member limit reached

---

#### POST `/organizations/{organizationId}/members/accept`

Accept a pending invitation.

**Authentication**: Required (the invited user)

**Response** `200 OK`:
```json
{
  "membershipId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "isActive": true,
  "joinedAt": "2026-01-16T12:00:00Z"
}
```

**Error Responses**:
- `404 Not Found` - No pending invitation
- `410 Gone` - Invitation expired

---

#### POST `/organizations/{organizationId}/members/reject`

Reject a pending invitation (user declines).

**Authentication**: Required (the invited user)

**Response** `204 No Content`

---

#### DELETE `/organizations/{organizationId}/invitations/{targetUserId}`

Withdraw a pending invitation (inviter revokes).

**Authentication**: Required (`members:invite` permission)

**Response** `204 No Content`

---

#### DELETE `/organizations/{organizationId}/members/{memberId}`

Remove a member from the organization.

**Authentication**: Required (`members:remove` permission)

**Response** `204 No Content`

**Error Responses**:
- `400 Bad Request` - Cannot remove the owner

---

#### POST `/organizations/{organizationId}/members/{memberId}/role`

Assign a role to a member.

**Authentication**: Required (`members:roles` permission)

**Request Body**:
```json
{
  "role": "operator"
}
```

**Response** `200 OK`: Updated membership object

**Error Responses**:
- `400 Bad Request` - Invalid role
- `403 Forbidden` - Cannot assign higher role than your own

---

#### POST `/organizations/{organizationId}/members/{memberId}/claims`

Add a custom claim to a member (grant or deny specific permissions).

**Authentication**: Required (`members:roles` permission)

**Request Body**:
```json
{
  "claimType": "grant",
  "claimValue": "servers:delete",
  "resourceType": "server",
  "resourceId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "expiresAt": "2026-12-31T23:59:59Z"
}
```

**ClaimType Values**:
- `grant` - Explicitly allow the permission
- `deny` - Explicitly deny the permission (overrides role grants)

**Response** `201 Created`

---

#### DELETE `/organizations/{organizationId}/members/{memberId}/claims/{claimId}`

Remove a custom claim from a member.

**Authentication**: Required (`members:roles` permission)

**Response** `204 No Content`

---

#### POST `/organizations/{organizationId}/members/bulk-invite`

Invite multiple users in a single request.

**Authentication**: Required (`members:invite` permission)

**Rate Limiting**: `invite` policy

**Request Body**:
```json
{
  "invites": [
    { "email": "user1@example.com", "role": "viewer" },
    { "email": "user2@example.com", "role": "operator" }
  ]
}
```

**Response** `200 OK`:
```json
{
  "succeeded": ["user-id-1", "user-id-2"],
  "failed": [
    { "itemId": "00000000-0000-0000-0000-000000000000", "error": "user_not_found" }
  ]
}
```

---

### Users

#### GET `/organizations/{organizationId}/users`

List all users in an organization.

**Authentication**: Required (`members:read` permission)

**Response** `200 OK`: Array of user objects

---

#### GET `/organizations/{organizationId}/users/{userId}`

Get user details.

**Authentication**: Required (`members:read` permission)

**Response** `200 OK`:
```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "email": "user@example.com",
  "displayName": "John Doe",
  "emailVerified": true,
  "linkedAccounts": [
    {
      "id": "...",
      "provider": "steam",
      "providerId": "76561198012345678",
      "linkedAt": "2026-01-01T00:00:00Z"
    }
  ],
  "createdAt": "2026-01-01T00:00:00Z"
}
```

---

#### DELETE `/organizations/{organizationId}/users/{userId}/linked-accounts/{linkedAccountId}`

Unlink an OAuth provider account from a user.

**Authentication**: Required (own account or admin)

**Response** `204 No Content`

---

### Roles

#### GET `/organizations/{organizationId}/roles`

List all roles (system + custom) in the organization.

**Response** `200 OK`:
```json
{
  "systemRoles": [
    { "name": "owner", "description": "Full control", "isSystem": true },
    { "name": "admin", "description": "Manage servers and members", "isSystem": true },
    { "name": "operator", "description": "Operate servers", "isSystem": true },
    { "name": "viewer", "description": "Read-only access", "isSystem": true }
  ],
  "customRoles": [
    { "id": "...", "name": "Mod Manager", "description": "Can manage mods only", "claims": ["mods:read", "mods:write"] }
  ]
}
```

---

#### POST `/organizations/{organizationId}/roles`

Create a custom role.

**Authentication**: Required (`members:roles` permission)

**Request Body**:
```json
{
  "name": "Mod Manager",
  "description": "Can manage mods only",
  "claims": ["mods:read", "mods:write", "mods:delete"]
}
```

**Response** `201 Created`

---

### Self-Service Endpoints (`/me`)

#### GET `/me`

Get current authenticated user's profile.

**Response** `200 OK`:
```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "email": "user@example.com",
  "displayName": "John Doe",
  "emailVerified": true,
  "preferredOrganizationId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "hasPasskeysRegistered": false,
  "createdAt": "2026-01-01T00:00:00Z"
}
```

---

#### PATCH `/me`

Update current user's profile.

**Request Body**:
```json
{
  "displayName": "Jane Doe",
  "preferredOrganizationId": "3fa85f64-5717-4562-b3fc-2c963f66afa6"
}
```

**Response** `200 OK`: Updated profile

---

#### GET `/me/organizations`

List organizations the current user belongs to.

**Response** `200 OK`: Array of organization summaries

---

#### GET `/me/permissions`

Get current user's permissions in the active organization.

**Response** `200 OK`:
```json
{
  "organizationId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "role": "admin",
  "permissions": [
    "org:read",
    "members:read",
    "members:invite",
    "servers:read",
    "servers:write",
    "servers:start",
    "servers:stop"
  ]
}
```

---

#### GET `/me/invitations`

List pending invitations for the current user.

**Response** `200 OK`:
```json
[
  {
    "membershipId": "...",
    "organizationId": "...",
    "organizationName": "Cool Gaming Org",
    "role": "viewer",
    "invitedByEmail": "admin@example.com",
    "invitedAt": "2026-01-15T00:00:00Z",
    "expiresAt": "2026-01-22T00:00:00Z"
  }
]
```

---

### Sessions

#### GET `/me/sessions`

List active sessions for the current user.

**Response** `200 OK`:
```json
[
  {
    "id": "session-id",
    "userAgent": "Mozilla/5.0...",
    "ipAddress": "192.168.1.1",
    "createdAt": "2026-01-16T12:00:00Z",
    "lastActiveAt": "2026-01-16T14:30:00Z",
    "isCurrent": true
  }
]
```

---

#### DELETE `/me/sessions/{sessionId}`

Revoke a specific session (logout that device).

**Response** `204 No Content`

---

### Activity Log

#### GET `/me/activity`

Get activity log for the current user.

**Query Parameters**:
- `page` (int) - Page number (default: 1)
- `pageSize` (int) - Items per page (default: 20, max: 100)
- `eventType` (string) - Filter by event type

**Response** `200 OK`:
```json
{
  "items": [
    {
      "id": "...",
      "eventType": "login",
      "timestamp": "2026-01-16T12:00:00Z",
      "ipAddress": "192.168.1.1",
      "userAgent": "Mozilla/5.0...",
      "details": {}
    }
  ],
  "page": 1,
  "pageSize": 20,
  "totalCount": 150,
  "totalPages": 8
}
```

---

### Search

#### GET `/organizations/search`

Search organizations the current user belongs to.

**Query Parameters**:
- `query` (string) - Search term (searches name and slug)

**Response** `200 OK`: Array of matching organizations

---

#### GET `/organizations/{organizationId}/users/search`

Search users within an organization.

**Query Parameters**:
- `query` (string) - Search term (searches email and display name)

**Response** `200 OK`: Array of matching users

---

### Internal Endpoints (Service-to-Service)

These endpoints are for internal microservice communication and are blocked from external access via the Gateway.

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/internal/users/{userId}` | GET | Get user info by ID |
| `/internal/users/batch` | POST | Get multiple users by IDs |
| `/internal/organizations/{organizationId}` | GET | Get organization info |
| `/internal/organizations/{organizationId}/exists` | GET | Check if org exists |
| `/internal/organizations/{organizationId}/members` | GET | Get org members |
| `/internal/permissions/check` | POST | Check user permission |
| `/internal/users/{userId}/organizations/{organizationId}/permissions` | GET | Get user permissions |
| `/internal/users/{userId}/organizations/{organizationId}/membership` | GET | Get membership info |

---

## Error Responses

All errors follow RFC 7807 Problem Details format:

```json
{
  "type": "https://tools.ietf.org/html/rfc7807",
  "title": "Bad Request",
  "status": 400,
  "detail": "The request body is invalid.",
  "instance": "/organizations"
}
```

### Common Error Codes

| Error | Description |
|-------|-------------|
| `org_not_found` | Organization does not exist |
| `user_not_found` | User does not exist |
| `membership_not_found` | User is not a member of the organization |
| `invalid_role` | The specified role is not valid |
| `already_member` | User is already a member |
| `invitation_exists` | User already has a pending invitation |
| `invite_expired` | The invitation has expired |
| `invites_disabled` | Organization has disabled invitations |
| `member_limit_reached` | Organization has reached max member limit |
| `cannot_remove_owner` | Cannot remove the organization owner |
| `forbidden_role_assignment` | Cannot assign a higher role than your own |
| `email_verification_required` | Action requires verified email |

---

## Rate Limiting

Rate limits are applied per policy:

| Policy | Limit | Window | Applied To |
|--------|-------|--------|------------|
| `auth` | 30 requests | 60 seconds | Authentication endpoints |
| `invite` | 5 requests | 60 seconds | Invitation endpoints |
| `per-user` | 100 requests | 60 seconds | Per authenticated user |
| `per-tenant` | 100 requests | 60 seconds | Per organization |

When rate limited, the response includes:
- Status: `429 Too Many Requests`
- Header: `Retry-After: <seconds>`

---

## Webhooks

The Identity service can receive webhook events from Better Auth for user lifecycle management.

See [Identity Webhooks](identity-webhooks.md) for details.
