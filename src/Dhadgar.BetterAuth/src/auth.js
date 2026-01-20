import { betterAuth } from "better-auth";
import { genericOAuth } from "better-auth/plugins";
import { Pool } from "pg";

export const trustedOrigins = (process.env.BETTER_AUTH_TRUSTED_ORIGINS ?? "")
  .split(",")
  .map((origin) => origin.trim())
  .filter(Boolean);

const baseUrl =
  process.env.BETTER_AUTH_URL ?? "http://localhost:5130/api/v1/betterauth";

if (!process.env.DATABASE_URL) {
  throw new Error("DATABASE_URL is required for Better Auth");
}

if (!process.env.BETTER_AUTH_SECRET) {
  throw new Error("BETTER_AUTH_SECRET is required for Better Auth");
}

// Build socialProviders config from environment variables (loaded from Key Vault)
const socialProviders = {};

// Facebook
if (process.env.OAUTH_FACEBOOK_APP_ID && process.env.OAUTH_FACEBOOK_APP_SECRET) {
  socialProviders.facebook = {
    clientId: process.env.OAUTH_FACEBOOK_APP_ID,
    clientSecret: process.env.OAUTH_FACEBOOK_APP_SECRET,
  };
}

// Google
if (process.env.OAUTH_GOOGLE_CLIENT_ID && process.env.OAUTH_GOOGLE_CLIENT_SECRET) {
  socialProviders.google = {
    clientId: process.env.OAUTH_GOOGLE_CLIENT_ID,
    clientSecret: process.env.OAUTH_GOOGLE_CLIENT_SECRET,
  };
}

// Discord
if (process.env.OAUTH_DISCORD_CLIENT_ID && process.env.OAUTH_DISCORD_CLIENT_SECRET) {
  socialProviders.discord = {
    clientId: process.env.OAUTH_DISCORD_CLIENT_ID,
    clientSecret: process.env.OAUTH_DISCORD_CLIENT_SECRET,
  };
}

// Twitch
if (process.env.OAUTH_TWITCH_CLIENT_ID && process.env.OAUTH_TWITCH_CLIENT_SECRET) {
  socialProviders.twitch = {
    clientId: process.env.OAUTH_TWITCH_CLIENT_ID,
    clientSecret: process.env.OAUTH_TWITCH_CLIENT_SECRET,
  };
}

// GitHub
if (process.env.OAUTH_GITHUB_CLIENT_ID && process.env.OAUTH_GITHUB_CLIENT_SECRET) {
  socialProviders.github = {
    clientId: process.env.OAUTH_GITHUB_CLIENT_ID,
    clientSecret: process.env.OAUTH_GITHUB_CLIENT_SECRET,
  };
}

// Apple
if (process.env.OAUTH_APPLE_CLIENT_ID && process.env.OAUTH_APPLE_CLIENT_SECRET) {
  socialProviders.apple = {
    clientId: process.env.OAUTH_APPLE_CLIENT_ID,
    clientSecret: process.env.OAUTH_APPLE_CLIENT_SECRET,
  };
}

// Microsoft is configured via genericOAuth plugin with federated credentials (no client secret)

// Identity service URL for WIF token requests
const identityServiceUrl = process.env.IDENTITY_SERVICE_URL ?? "http://localhost:5010";

// WIF client credentials for Microsoft federated auth
// The client_id "betterauth-client" matches the "subject" in the Azure federated credential
const wifClientId = process.env.WIF_CLIENT_ID ?? "betterauth-client";
const wifClientSecret = process.env.WIF_CLIENT_SECRET ?? "betterauth-client-dev-secret-change-in-prod";

// Helper function to get WIF token from Identity service for Microsoft federated auth
async function getMicrosoftClientAssertion() {
  const tokenUrl = `${identityServiceUrl}/connect/token`;

  const params = new URLSearchParams({
    grant_type: "client_credentials",
    client_id: wifClientId,
    client_secret: wifClientSecret,
    scope: "wif"
  });

  const response = await fetch(tokenUrl, {
    method: "POST",
    headers: { "Content-Type": "application/x-www-form-urlencoded" },
    body: params.toString()
  });

  if (!response.ok) {
    const error = await response.text();
    console.error("Failed to get WIF token from Identity:", error);
    throw new Error(`Failed to get client assertion: ${response.status}`);
  }

  const data = await response.json();
  return data.access_token;
}

// Build Microsoft OAuth plugin config (uses federated credentials via client_assertion)
const microsoftClientId = process.env.OAUTH_MICROSOFT_CLIENT_ID;
const microsoftEnabled = !!microsoftClientId;

const plugins = [];

if (microsoftEnabled) {
  plugins.push(
    genericOAuth({
      config: [
        {
          providerId: "microsoft",
          clientId: microsoftClientId,
          // Authorization uses /common for both personal and work/school accounts
          authorizationUrl: "https://login.microsoftonline.com/common/oauth2/v2.0/authorize",
          tokenUrl: "https://login.microsoftonline.com/common/oauth2/v2.0/token",
          scopes: ["openid", "profile", "email", "User.Read"],
          // Custom token exchange using client_assertion instead of client_secret
          getToken: async ({ code, redirectURI }) => {
            const clientAssertion = await getMicrosoftClientAssertion();

            const params = new URLSearchParams({
              client_id: microsoftClientId,
              code: code,
              redirect_uri: redirectURI,
              grant_type: "authorization_code",
              client_assertion_type: "urn:ietf:params:oauth:client-assertion-type:jwt-bearer",
              client_assertion: clientAssertion
            });

            const response = await fetch(
              "https://login.microsoftonline.com/common/oauth2/v2.0/token",
              {
                method: "POST",
                headers: { "Content-Type": "application/x-www-form-urlencoded" },
                body: params.toString()
              }
            );

            if (!response.ok) {
              const error = await response.text();
              console.error("Microsoft token exchange failed:", error);
              throw new Error(`Token exchange failed: ${response.status}`);
            }

            const data = await response.json();

            return {
              accessToken: data.access_token,
              refreshToken: data.refresh_token,
              accessTokenExpiresAt: new Date(Date.now() + data.expires_in * 1000),
              idToken: data.id_token,
              scopes: data.scope?.split(" ") ?? [],
              raw: data
            };
          },
          // Fetch user info from Microsoft Graph
          getUserInfo: async (tokens) => {
            const response = await fetch("https://graph.microsoft.com/v1.0/me", {
              headers: { Authorization: `Bearer ${tokens.accessToken}` }
            });

            if (!response.ok) {
              throw new Error(`Failed to fetch user info: ${response.status}`);
            }

            const data = await response.json();

            return {
              id: data.id,
              name: data.displayName,
              email: data.mail ?? data.userPrincipalName,
              image: null, // Microsoft Graph photo requires separate call
              emailVerified: true // Microsoft accounts have verified emails
            };
          }
        }
      ]
    })
  );
  console.log("  Microsoft OAuth: enabled (federated credentials)");
}

const configuredProviders = Object.keys(socialProviders);
if (microsoftEnabled) {
  configuredProviders.push("microsoft");
}
if (configuredProviders.length > 0) {
  console.log(`  OAuth providers: ${configuredProviders.join(", ")}`);
} else {
  console.log("  OAuth providers: (none configured)");
}

// Export config for use by getMigrations
export const authConfig = {
  appName: "Meridian Console",
  baseURL: baseUrl,
  basePath: "/api/v1/betterauth",
  secret: process.env.BETTER_AUTH_SECRET,
  trustedOrigins,
  database: new Pool({ connectionString: process.env.DATABASE_URL }),
  session: {
    // 7 days matches refresh token lifetime
    expiresIn: 60 * 60 * 24 * 7,
    updateAge: 60 * 60 * 24,
    cookieCache: {
      enabled: true,
      maxAge: 60 * 5 // 5 minutes
    }
  },
  emailAndPassword: {
    enabled: true,
    requireEmailVerification: false
  },
  socialProviders,
  plugins,
  // Account configuration for OAuth and account linking
  account: {
    // Store OAuth state in cookie for cross-origin flows
    storeStateStrategy: "cookie",
    // Enable account linking: users can link multiple OAuth providers to one account
    // if they share the same verified email address
    accountLinking: {
      enabled: true,
      // Providers trusted for automatic account linking (email must match)
      trustedProviders: [
        "discord",
        "google",
        "github",
        "twitch",
        "facebook",
        "apple",
        "microsoft"
      ]
    }
  },
  advanced: {
    // Cross-origin cookies require SameSite=None and Secure
    // This allows cart.meridianconsole.com to authenticate with dev.meridianconsole.com
    crossSubDomainCookies: {
      enabled: true,
      domain: "meridianconsole.com" // Share cookies across subdomains (no leading dot)
    },
    defaultCookieAttributes: {
      sameSite: "none",
      secure: true,
      httpOnly: true,
      partitioned: true // Required for third-party cookie support in modern browsers
    }
  }
};

export const auth = betterAuth(authConfig);
