// Lab only (meridian-lab L34, D-ML144). Never shipped with BetterAuth.
// Preloaded into the lab BetterAuth container with NODE_OPTIONS=--import=file:///lab/secrets-stub.mjs.
// It stands in for Dhadgar.Secrets and its /connect/token, which the lab does not run, so BetterAuth's real
// loadSecrets() (src/Dhadgar.BetterAuth/src/secrets-client.js) runs unchanged. It serves only values the lab
// already put in this container's environment, listens on loopback inside the container, and logs nothing.
import http from "node:http";

const port = Number(process.env.LAB_SECRETS_STUB_PORT ?? "");
const secret = process.env.BETTER_AUTH_SECRET;
const privateKey = process.env.EXCHANGE_TOKEN_PRIVATE_KEY;

if (!Number.isInteger(port) || port <= 0 || port > 65535) {
  throw new Error("lab secrets stub: LAB_SECRETS_STUB_PORT is required");
}
if (process.env.SECRETS_SERVICE_URL !== `http://127.0.0.1:${port}`) {
  throw new Error("lab secrets stub: SECRETS_SERVICE_URL must be the loopback stub");
}
if (process.env.IDENTITY_SERVICE_URL) {
  throw new Error("lab secrets stub: IDENTITY_SERVICE_URL must be unset so token requests stay in the container");
}
if (!secret || !privateKey) {
  throw new Error("lab secrets stub: BETTER_AUTH_SECRET and EXCHANGE_TOKEN_PRIVATE_KEY are required");
}

const accessToken = globalThis.crypto.randomUUID();
const bodies = {
  "/api/v1/secrets/betterauth": {
    secrets: { "betterauth-secret": secret, "betterauth-exchange-private-key": privateKey },
  },
  "/api/v1/secrets/oauth": { secrets: {} },
  "/api/v1/secrets/infrastructure": { secrets: {} },
};

const server = http.createServer((req, res) => {
  const send = (status, body) => {
    res.writeHead(status, { "Content-Type": "application/json" });
    res.end(JSON.stringify(body));
  };
  if (req.method === "POST" && req.url === "/connect/token") {
    req.resume();
    return send(200, { access_token: accessToken, token_type: "Bearer", expires_in: 3600 });
  }
  if (req.method !== "GET" || !Object.hasOwn(bodies, req.url ?? "")) {
    return send(404, { error: "not_found" });
  }
  if (req.headers.authorization !== `Bearer ${accessToken}`) {
    return send(401, { error: "unauthorized" });
  }
  return send(200, bodies[req.url]);
});

await new Promise((resolve, reject) => {
  server.once("error", reject);
  server.listen(port, "127.0.0.1", resolve);
});
