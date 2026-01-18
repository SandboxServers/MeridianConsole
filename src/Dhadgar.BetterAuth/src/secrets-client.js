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
  const secretMapping = {
    "betterauth-secret": "BETTER_AUTH_SECRET",
    "betterauth-exchange-private-key": "EXCHANGE_TOKEN_PRIVATE_KEY",
  };

  for (const [secretName, envVar] of Object.entries(secretMapping)) {
    if (betterAuthSecrets[secretName]) {
      process.env[envVar] = betterAuthSecrets[secretName];
    }
  }

  // Load infrastructure secrets (optional - fallback to env vars)
  try {
    const infraSecrets = await getInfrastructureSecrets();
    if (infraSecrets["postgres-password"]) {
      const host = process.env.POSTGRES_HOST ?? "localhost";
      const port = process.env.POSTGRES_PORT ?? "5432";
      const database = process.env.POSTGRES_DATABASE ?? "dhadgar_platform";
      const username = process.env.POSTGRES_USERNAME ?? "dhadgar";
      process.env.DATABASE_URL = `postgresql://${username}:${infraSecrets["postgres-password"]}@${host}:${port}/${database}`;
    }
  } catch (error) {
    console.log("  Infrastructure secrets not available, using env vars...");
  }

  // Build DATABASE_URL from env vars if not already set from secrets
  if (!process.env.DATABASE_URL) {
    const host = process.env.POSTGRES_HOST ?? "localhost";
    const port = process.env.POSTGRES_PORT ?? "5432";
    const database = process.env.POSTGRES_DATABASE ?? "dhadgar_platform";
    const username = process.env.POSTGRES_USERNAME ?? "dhadgar";
    const password = process.env.POSTGRES_PASSWORD ?? "dhadgar";
    process.env.DATABASE_URL = `postgresql://${username}:${password}@${host}:${port}/${database}`;
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
    "oauth-microsoft-personal-client-id": "OAUTH_MICROSOFT_CLIENT_ID",
    "oauth-microsoft-personal-client-secret": "OAUTH_MICROSOFT_CLIENT_SECRET",
  };

  for (const [secretName, envVar] of Object.entries(oauthMapping)) {
    if (oauthSecrets[secretName]) {
      process.env[envVar] = oauthSecrets[secretName];
    }
  }

  console.log("  Secrets loaded successfully");
}
