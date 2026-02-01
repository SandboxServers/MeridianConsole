/**
 * Secrets Service Client
 *
 * This client calls the Dhadgar.Secrets service to retrieve secrets.
 * The Secrets service is the "dispersing officer" that controls access to Key Vault.
 *
 * For authentication, uses client credentials flow via Identity service.
 */

const secretCache = new Map();
let cachedAccessToken = null;
let tokenExpiresAt = null;

/**
 * Gets the Secrets service base URL from environment.
 * @returns {string} The secrets service URL
 */
function getSecretsServiceUrl() {
  const url = process.env.SECRETS_SERVICE_URL;
  if (!url) {
    throw new Error(
      "SECRETS_SERVICE_URL is required. " +
        "Set it to the Secrets service URL (e.g., http://localhost:5000 or https://secrets.meridianconsole.internal)."
    );
  }
  return url.replace(/\/$/, ""); // Remove trailing slash
}

/**
 * Gets the Identity service token endpoint from environment.
 * @returns {string} The token endpoint URL
 */
function getIdentityTokenUrl() {
  // In Docker, use internal URL; the public issuer is different from internal endpoint
  const internalUrl = process.env.IDENTITY_SERVICE_URL ?? process.env.SECRETS_SERVICE_URL;
  if (!internalUrl) {
    throw new Error("IDENTITY_SERVICE_URL or SECRETS_SERVICE_URL is required");
  }
  // Token endpoint is at /connect/token (OpenIddict standard endpoint)
  return `${internalUrl.replace(/\/$/, "")}/connect/token`;
}

/**
 * Gets an access token using client credentials flow.
 * @returns {Promise<string>} The access token
 */
async function getAccessToken() {
  // Return cached token if still valid (with 60s buffer)
  if (cachedAccessToken && tokenExpiresAt && Date.now() < tokenExpiresAt - 60000) {
    return cachedAccessToken;
  }

  const clientId = process.env.SERVICE_CLIENT_ID ?? "dev-client";
  const clientSecret = process.env.SERVICE_CLIENT_SECRET ?? "dev-secret";
  const tokenUrl = getIdentityTokenUrl();

  console.log(`  Requesting access token from ${tokenUrl}...`);

  try {
    const response = await fetch(tokenUrl, {
      method: "POST",
      headers: {
        "Content-Type": "application/x-www-form-urlencoded",
      },
      body: new URLSearchParams({
        grant_type: "client_credentials",
        client_id: clientId,
        client_secret: clientSecret,
        scope: "secrets:read",
      }),
    });

    if (!response.ok) {
      const errorText = await response.text();
      throw new Error(`Token request failed: ${response.status} - ${errorText}`);
    }

    const data = await response.json();
    cachedAccessToken = data.access_token;
    tokenExpiresAt = Date.now() + (data.expires_in ?? 3600) * 1000;

    console.log("  Access token obtained successfully");
    return cachedAccessToken;
  } catch (error) {
    throw new Error(`Failed to get access token from Identity service: ${error.message}`);
  }
}

/**
 * Makes an authenticated request to the Secrets service.
 * @param {string} path - The API path
 * @param {Object} options - Fetch options
 * @returns {Promise<Response>} The fetch response
 */
async function authenticatedFetch(path, options = {}) {
  const token = await getAccessToken();
  const baseUrl = getSecretsServiceUrl();

  return fetch(`${baseUrl}${path}`, {
    ...options,
    headers: {
      ...options.headers,
      Authorization: `Bearer ${token}`,
    },
  });
}

/**
 * Fetches a single secret from the Secrets service.
 * @param {string} secretName - The name of the secret to retrieve
 * @returns {Promise<string|null>} The secret value, or null if not found
 */
export async function getSecret(secretName) {
  if (secretCache.has(secretName)) {
    return secretCache.get(secretName);
  }

  try {
    const response = await authenticatedFetch(`/api/v1/secrets/${secretName}`);

    if (response.status === 404) {
      return null;
    }

    if (response.status === 403) {
      console.warn(`Secret '${secretName}' access denied by Secrets service.`);
      return null;
    }

    if (!response.ok) {
      throw new Error(`Secrets service returned ${response.status}`);
    }

    const data = await response.json();
    const value = data.value;

    // Cache the secret
    secretCache.set(secretName, value);

    return value;
  } catch (error) {
    throw new Error(
      `Failed to retrieve secret '${secretName}' from Secrets service: ${error.message}`
    );
  }
}

/**
 * Fetches multiple secrets from the Secrets service in a single request.
 * @param {string[]} secretNames - Array of secret names to retrieve
 * @returns {Promise<Object>} Object mapping secret names to values
 */
export async function getSecretsBatch(secretNames) {
  try {
    const response = await authenticatedFetch("/api/v1/secrets/batch", {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
      },
      body: JSON.stringify({ secretNames }),
    });

    if (!response.ok) {
      throw new Error(`Secrets service returned ${response.status}`);
    }

    const data = await response.json();

    // Cache the secrets
    for (const [name, value] of Object.entries(data.secrets)) {
      secretCache.set(name, value);
    }

    return data.secrets;
  } catch (error) {
    throw new Error(
      `Failed to retrieve secrets batch from Secrets service: ${error.message}`
    );
  }
}

/**
 * Fetches all BetterAuth-related secrets.
 * @returns {Promise<Object>} Object mapping secret names to values
 */
export async function getBetterAuthSecrets() {
  try {
    const response = await authenticatedFetch("/api/v1/secrets/betterauth");

    if (!response.ok) {
      throw new Error(`Secrets service returned ${response.status}`);
    }

    const data = await response.json();

    // Cache the secrets
    for (const [name, value] of Object.entries(data.secrets)) {
      secretCache.set(name, value);
    }

    return data.secrets;
  } catch (error) {
    throw new Error(
      `Failed to retrieve BetterAuth secrets from Secrets service: ${error.message}`
    );
  }
}

/**
 * Fetches all OAuth provider secrets.
 * @returns {Promise<Object>} Object mapping secret names to values
 */
export async function getOAuthSecrets() {
  try {
    const response = await authenticatedFetch("/api/v1/secrets/oauth");

    if (!response.ok) {
      throw new Error(`Secrets service returned ${response.status}`);
    }

    const data = await response.json();

    // Cache the secrets
    for (const [name, value] of Object.entries(data.secrets)) {
      secretCache.set(name, value);
    }

    return data.secrets;
  } catch (error) {
    throw new Error(
      `Failed to retrieve OAuth secrets from Secrets service: ${error.message}`
    );
  }
}

/**
 * Fetches infrastructure secrets (database, messaging).
 * @returns {Promise<Object>} Object mapping secret names to values
 */
export async function getInfrastructureSecrets() {
  try {
    const response = await authenticatedFetch("/api/v1/secrets/infrastructure");

    if (!response.ok) {
      throw new Error(`Secrets service returned ${response.status}`);
    }

    const data = await response.json();

    // Cache the secrets
    for (const [name, value] of Object.entries(data.secrets)) {
      secretCache.set(name, value);
    }

    return data.secrets;
  } catch (error) {
    throw new Error(
      `Failed to retrieve infrastructure secrets from Secrets service: ${error.message}`
    );
  }
}

/**
 * Loads all required secrets for BetterAuth and sets them as environment variables.
 * This is the main entry point for secret loading at startup.
 *
 * @throws {Error} If any required BetterAuth secrets cannot be retrieved
 */
export async function loadSecrets() {
  console.log("Loading secrets from Secrets Service...");

  const secretsUrl = process.env.SECRETS_SERVICE_URL;
  if (!secretsUrl) {
    throw new Error("SECRETS_SERVICE_URL environment variable is required");
  }

  console.log(`  Secrets Service: ${secretsUrl}`);

  // Load BetterAuth core secrets
  const betterAuthSecrets = await getBetterAuthSecrets();

  // Map Key Vault secret names to environment variables
  // All secrets in this mapping are REQUIRED for BetterAuth to function
  const requiredSecrets = {
    "betterauth-secret": "BETTER_AUTH_SECRET",
    "betterauth-exchange-private-key": "EXCHANGE_TOKEN_PRIVATE_KEY",
  };

  const missingSecrets = [];

  for (const [secretName, envVar] of Object.entries(requiredSecrets)) {
    if (betterAuthSecrets[secretName]) {
      process.env[envVar] = betterAuthSecrets[secretName];
      console.log(`  ✓ Loaded ${secretName}`);
    } else {
      missingSecrets.push(secretName);
      console.error(`  ✗ Missing required secret: ${secretName}`);
    }
  }

  // Fail fast if any required secrets are missing
  if (missingSecrets.length > 0) {
    throw new Error(
      `BetterAuth cannot start: missing required secrets from Secrets Service: ${missingSecrets.join(", ")}. ` +
      `Ensure these secrets exist in Key Vault and the Secrets Service is authorized to access them.`
    );
  }

  // Build DATABASE_URL from env vars (always used for development, fallback for production)
  const host = process.env.POSTGRES_HOST ?? "localhost";
  const port = process.env.POSTGRES_PORT ?? "5432";
  const database = process.env.POSTGRES_DATABASE ?? "dhadgar_platform";
  const username = process.env.POSTGRES_USERNAME ?? "dhadgar";
  const envPassword = process.env.POSTGRES_PASSWORD ?? "dhadgar";

  // Start with env var based URL
  process.env.DATABASE_URL = `postgresql://${username}:${envPassword}@${host}:${port}/${database}`;
  console.log(`  Database URL built from env vars: postgresql://${username}:***@${host}:${port}/${database}`);

  // In development, use env vars for infrastructure (docker-compose handles this)
  // In production, load from Key Vault
  const isProduction = process.env.NODE_ENV === "production" && !process.env.USE_ENV_DB_PASSWORD;
  if (!isProduction) {
    console.log(`  Using env var database password (NODE_ENV=${process.env.NODE_ENV ?? "unset"}, USE_ENV_DB_PASSWORD=${process.env.USE_ENV_DB_PASSWORD ?? "unset"})`);
  } else {
    // Try to load infrastructure secrets from Key Vault (production only)
    try {
      const infraSecrets = await getInfrastructureSecrets();
      console.log(`  Infrastructure secrets received: ${Object.keys(infraSecrets).join(", ") || "(none)"}`);

      const kvPassword = infraSecrets["postgres-password"];
      if (kvPassword && kvPassword !== "PLACEHOLDER-UPDATE-ME" && kvPassword.length > 0) {
        process.env.DATABASE_URL = `postgresql://${username}:${kvPassword}@${host}:${port}/${database}`;
        console.log(`  ✓ DATABASE_URL overridden with postgres-password from Key Vault`);
      } else if (kvPassword) {
        console.log(`  ⚠ postgres-password from Key Vault is a placeholder or empty, using env var`);
      } else {
        console.log(`  postgres-password not in Key Vault, using env var`);
      }
    } catch (error) {
      console.log(`  Infrastructure secrets not available (${error.message}), using env vars...`);
    }
  }

  // Load OAuth secrets
  const oauthSecrets = await getOAuthSecrets();

  // Map OAuth secret names to environment variables
  const oauthMapping = {
    "oauth-facebook-app-id": "OAUTH_FACEBOOK_APP_ID",
    "oauth-facebook-app-secret": "OAUTH_FACEBOOK_APP_SECRET",
    "oauth-google-client-id": "OAUTH_GOOGLE_CLIENT_ID",
    "oauth-google-client-secret": "OAUTH_GOOGLE_CLIENT_SECRET",
    "oauth-discord-client-id": "OAUTH_DISCORD_CLIENT_ID",
    "oauth-discord-client-secret": "OAUTH_DISCORD_CLIENT_SECRET",
    "oauth-twitch-client-id": "OAUTH_TWITCH_CLIENT_ID",
    "oauth-twitch-client-secret": "OAUTH_TWITCH_CLIENT_SECRET",
    "oauth-github-client-id": "OAUTH_GITHUB_CLIENT_ID",
    "oauth-github-client-secret": "OAUTH_GITHUB_CLIENT_SECRET",
    "oauth-apple-client-id": "OAUTH_APPLE_CLIENT_ID",
    "oauth-apple-client-secret": "OAUTH_APPLE_CLIENT_SECRET",
    // Microsoft uses federated credentials (no client secret needed)
    "oauth-microsoft-client-id": "OAUTH_MICROSOFT_CLIENT_ID",
  };

  for (const [secretName, envVar] of Object.entries(oauthMapping)) {
    if (oauthSecrets[secretName]) {
      process.env[envVar] = oauthSecrets[secretName];
    }
  }

  console.log("  Secrets loaded successfully");
}
