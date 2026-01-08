/**
 * Secrets Service Client
 *
 * This client calls the Dhadgar.Secrets service to retrieve secrets.
 * The Secrets service is the "dispersing officer" that controls access to Key Vault.
 *
 * This replaces direct Key Vault access for BetterAuth.
 */

const secretCache = new Map();

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
 * Fetches a single secret from the Secrets service.
 * @param {string} secretName - The name of the secret to retrieve
 * @returns {Promise<string|null>} The secret value, or null if not found
 */
export async function getSecret(secretName) {
  if (secretCache.has(secretName)) {
    return secretCache.get(secretName);
  }

  const baseUrl = getSecretsServiceUrl();

  try {
    const response = await fetch(`${baseUrl}/api/v1/secrets/${secretName}`);

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
  const baseUrl = getSecretsServiceUrl();

  try {
    const response = await fetch(`${baseUrl}/api/v1/secrets/batch`, {
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
  const baseUrl = getSecretsServiceUrl();

  try {
    const response = await fetch(`${baseUrl}/api/v1/secrets/betterauth`);

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
  const baseUrl = getSecretsServiceUrl();

  try {
    const response = await fetch(`${baseUrl}/api/v1/secrets/oauth`);

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
  const baseUrl = getSecretsServiceUrl();

  try {
    const response = await fetch(`${baseUrl}/api/v1/secrets/infrastructure`);

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

  // Load infrastructure secrets
  const infraSecrets = await getInfrastructureSecrets();

  if (infraSecrets["postgres-password"]) {
    const host = process.env.POSTGRES_HOST ?? "localhost";
    const port = process.env.POSTGRES_PORT ?? "5432";
    const database = process.env.POSTGRES_DATABASE ?? "dhadgar_platform";
    const username = process.env.POSTGRES_USERNAME ?? "dhadgar";
    process.env.DATABASE_URL = `postgresql://${username}:${infraSecrets["postgres-password"]}@${host}:${port}/${database}`;
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
