# OAuth Provider Setup Guide

This guide explains how to configure OAuth providers for gaming platform account linking in the Identity service.

## Overview

The Identity service supports linking gaming platform accounts to user profiles:

| Provider | Platform | Use Case |
|----------|----------|----------|
| Steam | Steam | Link Steam account, verify game ownership |
| Epic Games | Epic Games Store | Link Epic account |
| Battle.net | Blizzard | Link Battle.net account |
| Xbox Live | Microsoft | Link Xbox/Microsoft account |
| Mock | Development | Testing OAuth flows |

## Provider Registry

Supported providers are defined in `OAuthProviderRegistry`:

```csharp
public static readonly IReadOnlySet<string> SupportedProviders = new HashSet<string>(
    StringComparer.OrdinalIgnoreCase)
{
    "steam",
    "epic",
    "battlenet",
    "xboxlive",
    "mock"
};
```

## Steam

### Setup

1. Create a Steam Web API Key at [Steam Developer Portal](https://steamcommunity.com/dev/apikey)
2. Configure in `appsettings.json` or user secrets:

```json
{
  "OAuth": {
    "Steam": {
      "ApplicationKey": "your-steam-api-key"
    }
  }
}
```

Or via user secrets:
```bash
dotnet user-secrets set "OAuth:Steam:ApplicationKey" "your-key" --project src/Dhadgar.Identity
```

### Authentication Flow

1. User clicks "Link Steam Account"
2. Redirect to Steam OpenID endpoint
3. User authenticates on Steam
4. Steam redirects back with SteamID64
5. Identity service creates `LinkedAccount` record

### SteamID Format

Steam returns a 64-bit Steam ID (e.g., `76561198012345678`).

## Epic Games

### Setup

1. Create an application in the [Epic Games Developer Portal](https://dev.epicgames.com/portal)
2. Configure OAuth client credentials
3. Set authorized redirect URIs
4. Configure in Identity service:

```json
{
  "OAuth": {
    "Epic": {
      "ClientId": "your-epic-client-id",
      "ClientSecret": "your-epic-client-secret"
    }
  }
}
```

### Redirect URI

Configure in Epic Games portal:
```
https://meridianconsole.com/api/v1/identity/signin-epic
```

### Scopes

Requested scopes:
- `basic_profile` - Access to basic profile information
- `friends_list` - (Optional) Access to friends list

### Custom Handler

Epic Games uses a custom OAuth handler (`EpicGamesOAuthHandler`) because the standard .NET OAuth middleware doesn't fully support Epic's authentication flow.

## Battle.net

### Setup

1. Create an application in the [Blizzard Developer Portal](https://develop.battle.net/)
2. Select the appropriate region
3. Configure OAuth client credentials
4. Configure in Identity service:

```json
{
  "OAuth": {
    "BattleNet": {
      "ClientId": "your-battlenet-client-id",
      "ClientSecret": "your-battlenet-client-secret",
      "Region": "America"
    }
  }
}
```

### Regions

| Region | Authorization Endpoint |
|--------|----------------------|
| America | `https://us.battle.net/oauth/authorize` |
| Europe | `https://eu.battle.net/oauth/authorize` |
| Asia | `https://apac.battle.net/oauth/authorize` |

### Redirect URI

```
https://meridianconsole.com/api/v1/identity/signin-battlenet
```

## Xbox Live

### Setup

1. Register an application in [Azure Portal](https://portal.azure.com/)
2. Configure authentication platform as "Web"
3. Add Xbox Live API permissions
4. Configure in Identity service:

```json
{
  "OAuth": {
    "Xbox": {
      "ClientId": "your-azure-app-client-id",
      "ClientSecret": "your-azure-app-client-secret",
      "TenantId": "consumers"
    }
  }
}
```

### Required API Permissions

- `XboxLive.signin` - Sign in and read Xbox Live profile

### Redirect URI

```
https://meridianconsole.com/api/v1/identity/signin-xboxlive
```

## Mock Provider (Development)

For local development and testing, a mock OAuth provider is available.

### Enable Mock Provider

Only available in Development environment:

```json
{
  "OAuth": {
    "Mock": {
      "Enabled": true
    }
  }
}
```

### Usage

```
GET /oauth/mock/link?returnUrl=/dashboard
```

The mock provider:
- Skips actual OAuth flow
- Generates a fake provider ID
- Creates a `LinkedAccount` record
- Redirects to the return URL

## Account Linking Flow

### Initiate Link

```
GET /oauth/{provider}/link?returnUrl=/settings/accounts
```

**Parameters**:
- `provider` - OAuth provider name (steam, epic, battlenet, xboxlive)
- `returnUrl` - URL to redirect after linking (must be on allowed host)

**Security**:
- User must be authenticated (JWT required)
- Return URL validated against allowlist

### Allowed Redirect Hosts

Configure allowed redirect hosts:

```json
{
  "OAuth": {
    "AllowedRedirectHosts": [
      "meridianconsole.com",
      "panel.meridianconsole.com",
      "localhost"
    ]
  }
}
```

### Link Callback

After successful OAuth, the provider redirects to:
```
/signin-{provider}
```

The Identity service:
1. Extracts provider user ID from OAuth response
2. Creates or updates `LinkedAccount` record
3. Redirects to the original return URL

### Unlink Account

```
DELETE /organizations/{orgId}/users/{userId}/linked-accounts/{linkedAccountId}
```

## Linked Account Data

Each linked account stores:

```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "userId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "provider": "steam",
  "providerId": "76561198012345678",
  "providerDisplayName": "SteamUsername",
  "linkedAt": "2026-01-16T12:00:00Z",
  "lastUsedAt": "2026-01-16T14:30:00Z",
  "providerMetadata": {
    "avatarUrl": "https://...",
    "profileUrl": "https://..."
  }
}
```

## Security Considerations

### State Management

OAuth state parameters:
- Include user ID for linking
- Set 10-minute expiration
- Validated on callback

### Secret Storage

All OAuth secrets should be stored in:
- Azure Key Vault (production)
- User secrets (development)

Never commit secrets to source control.

### Rate Limiting

OAuth linking endpoints are rate-limited:
- Policy: `auth`
- Limit: 30 requests per minute per IP

### Replay Prevention

Each OAuth callback is validated to prevent replay attacks:
- State parameter includes timestamp
- State expires after 10 minutes
- Provider tokens are not stored

## Troubleshooting

### "Unsupported Provider" Error

- Verify provider name is lowercase
- Check `OAuthProviderRegistry.IsSupported()`
- Ensure provider is configured in appsettings

### OAuth Callback Fails

1. Check redirect URI matches exactly (including trailing slash)
2. Verify client ID and secret are correct
3. Check provider console for error logs
4. Verify the provider is enabled

### "Invalid Return URL" Error

- Verify host is in `AllowedRedirectHosts`
- Check URL is properly encoded
- Ensure URL starts with `/` for relative URLs

### Steam Authentication Fails

- Verify Steam API key is valid
- Check if Steam services are operational
- Verify OpenID realm configuration

## Monitoring

Key metrics:

| Metric | Description |
|--------|-------------|
| `identity.oauth.link.started` | OAuth link flows initiated |
| `identity.oauth.link.completed` | Successful account links |
| `identity.oauth.link.failed` | Failed link attempts |
| `identity.oauth.provider.{name}` | Per-provider metrics |
