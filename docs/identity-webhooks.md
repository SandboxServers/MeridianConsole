# Identity Webhooks

The Dhadgar Identity service can receive webhook events from Better Auth to synchronize user lifecycle changes.

## Overview

Better Auth manages the primary authentication flow (social OAuth, passkeys, magic links). When user events occur in Better Auth, webhooks notify the Identity service to update its local user records.

## Webhook Endpoint

```
POST /webhooks/better-auth
```

This endpoint is:
- **Anonymous** - No JWT required (uses signature validation)
- **Rate limited** - Protected by the `auth` rate limiting policy

## Supported Events

### `user.deleted`

Triggered when a user account is deleted in Better Auth.

**Action**: Soft-deletes the user in Identity service and deactivates all memberships.

**Payload**:
```json
{
  "event": "user.deleted",
  "data": {
    "externalAuthId": "ba_user_abc123",
    "email": "user@example.com"
  }
}
```

**Processing**:
1. Finds user by `externalAuthId`
2. Sets `DeletedAt` timestamp
3. Marks all active memberships as `LeftAt` and `IsActive = false`
4. Publishes `UserDeactivated` event to message bus

---

### `user.updated`

Triggered when user profile is updated in Better Auth.

**Action**: Syncs email and email verification status.

**Payload**:
```json
{
  "event": "user.updated",
  "data": {
    "externalAuthId": "ba_user_abc123",
    "email": "newemail@example.com",
    "emailVerified": true
  }
}
```

**Processing**:
1. Finds user by `externalAuthId`
2. Updates email if changed
3. Updates `EmailVerified` status if changed
4. Sets `UpdatedAt` timestamp

---

### `passkey.registered`

Triggered when a user registers a passkey (WebAuthn).

**Action**: Updates passkey registration status on user record.

**Payload**:
```json
{
  "event": "passkey.registered",
  "data": {
    "externalAuthId": "ba_user_abc123",
    "passkeyId": "passkey_xyz789"
  }
}
```

**Processing**:
1. Finds user by `externalAuthId`
2. Sets `HasPasskeysRegistered = true`
3. Updates `LastPasskeyAuthAt` timestamp

## Signature Validation

All webhooks must be signed using HMAC-SHA256. The Identity service validates signatures before processing.

### Signature Format

The signature is sent in a header (configurable, default: `X-Webhook-Signature`):

```
X-Webhook-Signature: t=1705402800,v1=abc123def456...
```

Components:
- `t` - Unix timestamp of when the webhook was sent
- `v1` - HMAC-SHA256 signature

### Signature Calculation

```
signature = HMAC-SHA256(timestamp + "." + rawBody, webhookSecret)
```

### Validation Steps

1. Parse the signature header to extract `t` and `v1`
2. Verify timestamp is within allowed window (default: 5 minutes)
3. Compute expected signature: `HMAC-SHA256(t.body, secret)`
4. Compare signatures using constant-time comparison

### Configuration

```json
{
  "Webhooks": {
    "BetterAuth": {
      "SignatureHeader": "X-Webhook-Signature",
      "MaxTimestampAgeSeconds": 300,
      "SecretKeyVaultName": "better-auth-webhook-secret"
    }
  }
}
```

## Secret Management

The webhook secret is stored in Azure Key Vault:

1. Create a secret in Key Vault named `better-auth-webhook-secret`
2. Configure the `IWebhookSecretProvider` to read from Key Vault
3. In development, signature validation can be skipped if no secret is configured

## Development Mode

In Development or Testing environments, if no webhook secret is configured:
- A warning is logged
- Signature validation is skipped
- Webhooks are processed normally

**Warning**: Never skip signature validation in production.

## Error Handling

| Scenario | Response |
|----------|----------|
| Missing signature header | `401 Unauthorized` |
| Invalid signature format | `401 Unauthorized` |
| Timestamp too old | `401 Unauthorized` |
| Signature mismatch | `401 Unauthorized` |
| Missing event type | `400 Bad Request` |
| Unknown user | `200 OK` (logged, no action) |
| Unknown event type | `200 OK` (logged, no action) |
| Key Vault error | `503 Service Unavailable` |

## Message Publishing

When processing `user.deleted`, the service publishes a `UserDeactivated` event:

```csharp
public record UserDeactivated(
    Guid UserId,
    string ExternalAuthId,
    string Reason,
    DateTimeOffset OccurredAt);
```

Other services can subscribe to this event to clean up user-related data.

## Testing Webhooks

### Local Testing

Use curl to send test webhooks in development:

```bash
curl -X POST http://localhost:5010/webhooks/better-auth \
  -H "Content-Type: application/json" \
  -d '{
    "event": "user.updated",
    "data": {
      "externalAuthId": "test_user_123",
      "email": "test@example.com",
      "emailVerified": true
    }
  }'
```

### Generating Test Signatures

For signature validation testing:

```bash
# Generate signature
timestamp=$(date +%s)
body='{"event":"user.updated","data":{"externalAuthId":"test"}}'
secret="your-webhook-secret"
payload="${timestamp}.${body}"
signature=$(echo -n "$payload" | openssl dgst -sha256 -hmac "$secret" | cut -d' ' -f2)

# Send with signature
curl -X POST http://localhost:5010/webhooks/better-auth \
  -H "Content-Type: application/json" \
  -H "X-Webhook-Signature: t=${timestamp},v1=${signature}" \
  -d "$body"
```

## Monitoring

Key metrics to monitor:

| Metric | Description |
|--------|-------------|
| `identity.webhooks.received` | Total webhooks received |
| `identity.webhooks.validated` | Webhooks that passed signature validation |
| `identity.webhooks.processed` | Webhooks successfully processed |
| `identity.webhooks.errors` | Webhook processing errors |

## Troubleshooting

### Webhook Not Processing

1. Check logs for signature validation errors
2. Verify the webhook secret matches between Better Auth and Key Vault
3. Ensure timestamp is within the allowed window
4. Check if the user exists in the Identity database

### Signature Validation Failing

1. Verify the secret is correctly configured in both systems
2. Check for whitespace or encoding issues in the secret
3. Ensure the timestamp format is correct (Unix seconds)
4. Verify the payload hasn't been modified in transit

### User Not Found

This is expected for new users. The webhook is acknowledged (200 OK) but no action is taken. Users are created during the first token exchange, not via webhooks.
