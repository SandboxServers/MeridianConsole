import "dotenv/config";
import express from "express";
import cors from "cors";
import { toNodeHandler, fromNodeHeaders } from "better-auth/node";
import { getMigrations } from "better-auth/db";
import { loadSecrets } from "./secrets-client.js";

// Load secrets from Secrets Service before initializing the app
await loadSecrets();

// Import auth after secrets are loaded (it depends on env vars)
const { auth, authConfig, trustedOrigins } = await import("./auth.js");

// Run database migrations on startup
async function runMigrations() {
  console.log("Checking for database migrations...");
  try {
    const { toBeCreated, toBeAdded, runMigrations } = await getMigrations(authConfig);

    if (toBeCreated.length === 0 && toBeAdded.length === 0) {
      console.log("  Database schema is up to date");
      return;
    }

    if (toBeCreated.length > 0) {
      console.log(`  Tables to create: ${toBeCreated.map(t => t.table).join(", ")}`);
    }
    if (toBeAdded.length > 0) {
      console.log(`  Fields to add: ${toBeAdded.map(t => `${t.table}.${t.fields?.join(", ")}`).join("; ")}`);
    }

    await runMigrations();
    console.log("  Migrations completed successfully");
  } catch (error) {
    console.error("  Migration error:", error.message);
    // Don't exit - the tables might already exist
  }
}

await runMigrations();
const { createExchangeToken } = await import("./exchange.js");

const app = express();
const port = Number(process.env.PORT ?? 5130);

app.use(express.json());

app.use(
  cors({
    origin: (origin, callback) => {
      if (!origin) {
        return callback(null, true);
      }

      if (trustedOrigins.length === 0 || trustedOrigins.includes(origin)) {
        return callback(null, true);
      }

      return callback(new Error("Origin not allowed"), false);
    },
    credentials: true,
    allowedHeaders: ["Content-Type", "Authorization"],
    methods: ["GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS"]
  })
);

app.get("/healthz", (_req, res) => {
  res.json({ service: "Dhadgar.BetterAuth", status: "ok" });
});

// Exchange token endpoint: emits ES256 one-time exchange token for Identity
app.post("/api/v1/betterauth/exchange", async (req, res) => {
  try {
    const session = await auth.api.getSession({
      headers: fromNodeHeaders(req.headers)
    });

    if (!session?.user) {
      return res.status(401).json({ error: "unauthorized" });
    }

    const exchangeToken = await createExchangeToken({
      user: session.user,
      origin: req.headers.origin,
      clientApp: req.body?.clientApp
    });

    return res.json({ exchangeToken });
  } catch (error) {
    console.error("Exchange token error:", error);
    return res.status(500).json({ error: "exchange_failed" });
  }
});

// Better Auth handles all auth routes
app.all("/api/v1/betterauth/*", toNodeHandler(auth));

const server = app.listen(port, () => {
  console.log(`Dhadgar.BetterAuth listening on http://localhost:${port}`);
});

server.on('error', (err) => {
  console.error('Failed to start server:', err);
  process.exit(1);
});
