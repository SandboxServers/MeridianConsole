import { betterAuth } from "better-auth";
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

// Microsoft (for Microsoft Account login)
if (process.env.OAUTH_MICROSOFT_CLIENT_ID && process.env.OAUTH_MICROSOFT_CLIENT_SECRET) {
  socialProviders.microsoft = {
    clientId: process.env.OAUTH_MICROSOFT_CLIENT_ID,
    clientSecret: process.env.OAUTH_MICROSOFT_CLIENT_SECRET,
  };
}

const configuredProviders = Object.keys(socialProviders);
if (configuredProviders.length > 0) {
  console.log(`  OAuth providers: ${configuredProviders.join(", ")}`);
} else {
  console.log("  OAuth providers: (none configured)");
}

export const auth = betterAuth({
  appName: "Meridian Console",
  baseURL: baseUrl,
  basePath: "/api/v1/betterauth",
  secret: process.env.BETTER_AUTH_SECRET,
  trustedOrigins,
  database: new Pool({ connectionString: process.env.DATABASE_URL }),
  session: {
    // 7 days matches refresh token lifetime
    expiresIn: 60 * 60 * 24 * 7,
    updateAge: 60 * 60 * 24
  },
  emailAndPassword: {
    enabled: true,
    requireEmailVerification: false
  },
  socialProviders
});
