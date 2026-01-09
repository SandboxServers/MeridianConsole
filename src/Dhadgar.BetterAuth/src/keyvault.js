import { DefaultAzureCredential } from "@azure/identity";
import { SecretClient } from "@azure/keyvault-secrets";

const secretCache = new Map();

let secretClient = null;

function getSecretClient() {
  if (secretClient) {
    return secretClient;
  }

  const vaultUrl = process.env.AZURE_KEYVAULT_URL;
  if (!vaultUrl) {
    throw new Error(
      "AZURE_KEYVAULT_URL is required. " +
      "Set it to your Key Vault URL (e.g., https://mc-secrets-gbl.vault.azure.net/). " +
      "Ensure you are logged in via 'az login' for local development."
    );
  }

  const credential = new DefaultAzureCredential();
  secretClient = new SecretClient(vaultUrl, credential);
  return secretClient;
}

export async function getSecret(secretName) {
  if (secretCache.has(secretName)) {
    return secretCache.get(secretName);
  }

  const client = getSecretClient();

  try {
    const secret = await client.getSecret(secretName);
    const value = secret.value;

    // Cache the secret
    secretCache.set(secretName, value);

    return value;
  } catch (error) {
    throw new Error(
      `Failed to retrieve secret '${secretName}' from Key Vault: ${error.message}. ` +
      "Ensure you are logged in via 'az login' and have 'Key Vault Secrets User' role."
    );
  }
}

async function getSecretOptional(secretName) {
  try {
    const value = await getSecret(secretName);
    // Skip placeholder values
    if (value === "PLACEHOLDER-UPDATE-ME") {
      return null;
    }
    return value;
  } catch {
    return null;
  }
}

export async function loadSecrets() {
  console.log("Loading secrets from Azure Key Vault...");

  const vaultUrl = process.env.AZURE_KEYVAULT_URL;
  if (!vaultUrl) {
    throw new Error("AZURE_KEYVAULT_URL environment variable is required");
  }

  console.log(`  Vault: ${vaultUrl}`);

  // Required secrets
  const requiredSecrets = {
    BETTER_AUTH_SECRET: "betterauth-secret",
    EXCHANGE_TOKEN_PRIVATE_KEY: "betterauth-exchange-private-key",
    POSTGRES_PASSWORD: "postgres-password",
  };

  // OAuth provider secrets (optional - only load if they exist and are not placeholders)
  const optionalSecrets = {
    OAUTH_FACEBOOK_APP_ID: "oauth-facebook-app-id",
    OAUTH_FACEBOOK_APP_SECRET: "oauth-facebook-app-secret",
    OAUTH_GOOGLE_CLIENT_ID: "oauth-google-client-id",
    OAUTH_GOOGLE_CLIENT_SECRET: "oauth-google-client-secret",
    OAUTH_DISCORD_CLIENT_ID: "oauth-discord-client-id",
    OAUTH_DISCORD_CLIENT_SECRET: "oauth-discord-client-secret",
    OAUTH_TWITCH_CLIENT_ID: "oauth-twitch-client-id",
    OAUTH_TWITCH_CLIENT_SECRET: "oauth-twitch-client-secret",
    OAUTH_GITHUB_CLIENT_ID: "oauth-github-client-id",
    OAUTH_GITHUB_CLIENT_SECRET: "oauth-github-client-secret",
    OAUTH_APPLE_CLIENT_ID: "oauth-apple-client-id",
    OAUTH_APPLE_CLIENT_SECRET: "oauth-apple-client-secret",
    OAUTH_MICROSOFT_CLIENT_ID: "oauth-microsoft-client-id",
    OAUTH_MICROSOFT_CLIENT_SECRET: "oauth-microsoft-client-secret",
  };

  // Load required secrets
  const secrets = {};
  for (const [envVar, secretName] of Object.entries(requiredSecrets)) {
    secrets[envVar] = await getSecret(secretName);
  }

  // Load optional OAuth secrets (silently skip if not found or placeholder)
  for (const [envVar, secretName] of Object.entries(optionalSecrets)) {
    const value = await getSecretOptional(secretName);
    if (value) {
      secrets[envVar] = value;
    }
  }

  // Set them as environment variables for the app to use
  for (const [key, value] of Object.entries(secrets)) {
    if (value) {
      process.env[key] = value;
    }
  }

  // Build database URL with the password from Key Vault
  if (!process.env.DATABASE_URL && secrets.POSTGRES_PASSWORD) {
    const host = process.env.POSTGRES_HOST ?? "localhost";
    const port = process.env.POSTGRES_PORT ?? "5432";
    const database = process.env.POSTGRES_DATABASE ?? "dhadgar_platform";
    const username = process.env.POSTGRES_USERNAME ?? "dhadgar";
    process.env.DATABASE_URL = `postgresql://${username}:${secrets.POSTGRES_PASSWORD}@${host}:${port}/${database}`;
  }

  console.log("  Secrets loaded successfully");
  return secrets;
}
