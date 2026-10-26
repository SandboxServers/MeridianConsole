// Rate limiting for the exchange endpoint, plus the trust-proxy setting that
// decides which client IP the limiter keys on. Kept in its own module so it can
// be exercised without booting the full server (which needs secrets and a DB).
import { rateLimit } from "express-rate-limit";

export const DEFAULT_EXCHANGE_RATE_LIMIT_WINDOW_MS = 60_000;
export const DEFAULT_EXCHANGE_RATE_LIMIT_MAX = 30;

function parsePositiveInt(name, raw, fallback) {
  if (raw === undefined || raw.trim() === "") {
    return fallback;
  }
  const value = Number(raw.trim());
  if (!Number.isSafeInteger(value) || value <= 0) {
    throw new Error(`${name} must be a positive integer, got "${raw}"`);
  }
  return value;
}

/**
 * Maps TRUST_PROXY to Express's "trust proxy" setting.
 *
 * - unset, "", "false" or "0": trust nothing; req.ip is the socket peer.
 * - a positive integer N: trust N proxy hops (e.g. "1" behind the Gateway).
 * - anything else: a comma-separated list of addresses, subnets or Express
 *   presets ("loopback", "linklocal", "uniquelocal") to trust.
 *
 * "true" is refused: it trusts every hop, so any client can pick its own IP
 * by sending X-Forwarded-For and dodge the rate limit.
 */
export function parseTrustProxy(raw) {
  const value = raw?.trim() ?? "";
  if (value === "" || value.toLowerCase() === "false" || value === "0") {
    return false;
  }
  if (value.toLowerCase() === "true") {
    throw new Error(
      'TRUST_PROXY=true is not allowed (clients could spoof X-Forwarded-For); ' +
        "set a hop count such as 1, or the proxy addresses/subnets to trust"
    );
  }
  if (/^\d+$/.test(value)) {
    return Number(value);
  }
  if (/^-?\d+(\.\d+)?$/.test(value)) {
    throw new Error(`TRUST_PROXY hop count must be a non-negative integer, got "${raw}"`);
  }
  return value;
}

export function exchangeRateLimitOptions(env = process.env) {
  return {
    windowMs: parsePositiveInt(
      "EXCHANGE_RATE_LIMIT_WINDOW_MS",
      env.EXCHANGE_RATE_LIMIT_WINDOW_MS,
      DEFAULT_EXCHANGE_RATE_LIMIT_WINDOW_MS
    ),
    limit: parsePositiveInt(
      "EXCHANGE_RATE_LIMIT_MAX",
      env.EXCHANGE_RATE_LIMIT_MAX,
      DEFAULT_EXCHANGE_RATE_LIMIT_MAX
    ),
  };
}

/**
 * Per-client limiter for POST /api/v1/betterauth/exchange. Each request does a
 * session lookup, a DB query and signs a token, so it is limited here as well
 * as at the Gateway. Clients are keyed by req.ip (IPv6 grouped by /56, the
 * library default), which depends on the app's "trust proxy" setting.
 */
export function createExchangeRateLimiter(env = process.env) {
  const { windowMs, limit } = exchangeRateLimitOptions(env);
  return rateLimit({
    windowMs,
    limit,
    standardHeaders: "draft-8",
    legacyHeaders: false,
    handler: (_req, res, _next, options) => {
      res.status(options.statusCode).json({ error: "rate_limited" });
    },
  });
}
