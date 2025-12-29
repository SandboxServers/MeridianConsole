import { betterAuth } from "better-auth";
import { Pool } from "pg";

const trustedOrigins = (process.env.BETTER_AUTH_TRUSTED_ORIGINS ?? "")
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
  }
});
