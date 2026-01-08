import "dotenv/config";
import express from "express";
import cors from "cors";
import { toNodeHandler, fromNodeHeaders } from "better-auth/node";
import { loadSecrets } from "./secrets-client.js";

// Load secrets from Secrets Service before initializing the app
await loadSecrets();

// Import auth after secrets are loaded (it depends on env vars)
const { auth } = await import("./auth.js");
const { createExchangeToken } = await import("./exchange.js");

const app = express();
const port = Number(process.env.PORT ?? 5130);

const trustedOrigins = (process.env.BETTER_AUTH_TRUSTED_ORIGINS ?? "")
  .split(",")
  .map((origin) => origin.trim())
  .filter(Boolean);

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

app.all("/api/v1/betterauth/*", toNodeHandler(auth));

const server = app.listen(port, () => {
  console.log(`Dhadgar.BetterAuth listening on http://localhost:${port}`);
});

server.on('error', (err) => {
  console.error('Failed to start server:', err);
  process.exit(1);
});
