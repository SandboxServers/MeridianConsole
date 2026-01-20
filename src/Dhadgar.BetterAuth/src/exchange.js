import { SignJWT, importPKCS8 } from "jose";
import { randomUUID } from "crypto";

const EXCHANGE_TTL_SECONDS = 60;

let cachedKey;

async function getPrivateKey() {
  if (cachedKey) {
    return cachedKey;
  }

  const privateKeyPem = process.env.EXCHANGE_TOKEN_PRIVATE_KEY;
  if (!privateKeyPem) {
    throw new Error("EXCHANGE_TOKEN_PRIVATE_KEY is required");
  }

  cachedKey = await importPKCS8(privateKeyPem, "ES256");
  return cachedKey;
}

function resolveClientApp({ bodyClientApp, origin, allowedApps }) {
  // Use strict hostname comparison to prevent origin spoofing
  if (origin) {
    try {
      const url = new URL(origin);
      if (url.hostname === "panel.meridianconsole.com") {
        return "panel";
      }
      if (url.hostname === "cart.meridianconsole.com") {
        return "shop";
      }
      if (url.hostname === "meridianconsole.com" || url.hostname === "www.meridianconsole.com") {
        return "shop";
      }
    } catch {
      // Invalid origin URL, fall through
    }
  }

  if (bodyClientApp && allowedApps.includes(bodyClientApp)) {
    return bodyClientApp;
  }

  return "unknown";
}

export async function createExchangeToken({ user, origin, clientApp, provider, providers }) {
  const key = await getPrivateKey();
  const issuer =
    process.env.EXCHANGE_TOKEN_ISSUER ??
    process.env.BETTER_AUTH_URL ??
    "https://meridianconsole.com/api/v1/betterauth";
  const audience =
    process.env.EXCHANGE_TOKEN_AUDIENCE ??
    "https://meridianconsole.com/api/v1/identity/exchange";
  const allowedApps = (process.env.BETTER_AUTH_CLIENT_APPS ?? "")
    .split(",")
    .map((value) => value.trim())
    .filter(Boolean);

  const resolvedClientApp = resolveClientApp({
    bodyClientApp: clientApp,
    origin,
    allowedApps
  });

  return await new SignJWT({
    sub: user.id,
    email: user.email,
    name: user.name || null,
    picture: user.image || null,
    purpose: "token_exchange",
    client_app: resolvedClientApp,
    provider: provider ?? "unknown",
    providers: providers ?? []
  })
    .setProtectedHeader({
      alg: "ES256",
      kid: process.env.EXCHANGE_TOKEN_KID
    })
    .setIssuedAt()
    .setExpirationTime(`${EXCHANGE_TTL_SECONDS}s`)
    .setJti(randomUUID())
    .setIssuer(issuer)
    .setAudience(audience)
    .sign(key);
}
