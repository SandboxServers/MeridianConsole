import "./env.js";
import express from "express";
import cors from "cors";
import pg from "pg";
import { toNodeHandler, fromNodeHeaders } from "better-auth/node";
// better-auth >= 1.6 moved getMigrations from "better-auth/db" to "better-auth/db/migration"
import { getMigrations } from "better-auth/db/migration";
import { loadSecrets } from "./secrets-client.js";
import { createExchangeRateLimiter, parseTrustProxy } from "./rate-limit.js";

// Load secrets from Secrets Service before initializing the app
await loadSecrets();

// Import auth after secrets are loaded (it depends on env vars)
const { auth, authConfig, trustedOrigins } = await import("./auth.js");

// Create a database pool for direct queries (shared connection with Better Auth)
const dbPool = new pg.Pool({
  connectionString: process.env.DATABASE_URL,
  statement_timeout: 5000, // 5 second timeout for queries
});

// Handle idle client errors to prevent unhandled exceptions
dbPool.on('error', (err) => {
  console.error('Unexpected error on idle database client:', err.message);
});

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
      // t.fields is a Record<string, FieldAttribute>, not an array
      console.log(`  Fields to add: ${toBeAdded.map(t => `${t.table}.${Object.keys(t.fields ?? {}).join(", ")}`).join("; ")}`);
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

// Which proxy hops to trust when deriving req.ip (used by the rate limiter).
// Off by default; behind the Gateway set TRUST_PROXY=1. See rate-limit.js.
app.set("trust proxy", parseTrustProxy(process.env.TRUST_PROXY));
const exchangeRateLimiter = createExchangeRateLimiter();

// No global body parser: Better Auth must read the raw request stream, so
// express.json() is applied only to the routes that need it (see below).

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
// Registered before the Better Auth catch-all so it is matched first.
app.post("/api/v1/betterauth/exchange", exchangeRateLimiter, express.json(), async (req, res) => {
  try {
    const session = await auth.api.getSession({
      headers: fromNodeHeaders(req.headers)
    });

    if (!session?.user) {
      return res.status(401).json({ error: "unauthorized" });
    }

    // Get ALL user's linked accounts to pass to Identity
    // Query the BetterAuth 'account' table directly
    let providers = [];
    let currentProvider = "unknown";
    try {
      const result = await dbPool.query(
        `SELECT "providerId", "accountId", "updatedAt", "createdAt"
         FROM "account"
         WHERE "userId" = $1
         ORDER BY COALESCE("updatedAt", "createdAt") DESC`,
        [session.user.id]
      );

      if (result.rows.length > 0) {
        // Current provider is the most recently used
        currentProvider = result.rows[0].providerId ?? "unknown";
        // All providers the user has linked
        providers = result.rows.map(row => ({
          providerId: row.providerId,
          accountId: row.accountId
        }));
      }
    } catch (accountError) {
      // Log but continue - we can still issue token with unknown provider
      console.warn("Failed to fetch user accounts:", accountError.message);
    }

    const exchangeToken = await createExchangeToken({
      user: session.user,
      origin: req.headers.origin,
      clientApp: req.body?.clientApp,
      provider: currentProvider,
      providers
    });

    return res.json({ exchangeToken });
  } catch (error) {
    console.error("Exchange token error:", error);
    return res.status(500).json({ error: "exchange_failed" });
  }
});

// Better Auth handles all auth routes.
// Express 5 (path-to-regexp v8) requires a named wildcard; a bare "*" throws at startup.
app.all("/api/v1/betterauth/*splat", toNodeHandler(auth));

// Express 5 also calls the listen callback with the error when the server fails
// to bind; the 'error' handler below reports it and exits.
const server = app.listen(port, (err) => {
  if (err) return;
  console.log(`Dhadgar.BetterAuth listening on http://localhost:${port}`);
});

server.on('error', (err) => {
  console.error('Failed to start server:', err);
  process.exit(1);
});

// Graceful shutdown handling
let isShuttingDown = false;

async function gracefulShutdown(signal) {
  if (isShuttingDown) {
    console.log('Shutdown already in progress...');
    return;
  }
  isShuttingDown = true;

  console.log(`\nReceived ${signal}. Starting graceful shutdown...`);

  try {
    // Stop accepting new connections
    await new Promise((resolve, reject) => {
      server.close((err) => {
        if (err) {
          console.error('Error closing HTTP server:', err.message);
          reject(err);
        } else {
          console.log('HTTP server closed');
          resolve();
        }
      });
    });
  } catch (err) {
    console.error('Failed to close HTTP server:', err.message);
  }

  try {
    // Close database pool
    await dbPool.end();
    console.log('Database pool closed');
  } catch (err) {
    console.error('Error closing database pool:', err.message);
  }

  console.log('Graceful shutdown completed');
  process.exit(0);
}

// Register shutdown handlers
process.on('SIGINT', () => gracefulShutdown('SIGINT'));
process.on('SIGTERM', () => gracefulShutdown('SIGTERM'));

process.on('uncaughtException', (err) => {
  console.error('Uncaught exception:', err);
  gracefulShutdown('uncaughtException').finally(() => process.exit(1));
});

process.on('unhandledRejection', (reason, promise) => {
  console.error('Unhandled rejection at:', promise, 'reason:', reason);
  gracefulShutdown('unhandledRejection').finally(() => process.exit(1));
});
