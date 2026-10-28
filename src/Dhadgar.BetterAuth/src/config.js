// HF-02 (D-48, D-49): trusted origins and cookie options, kept pure so node:test can load them.
export function parseTrustedOrigins(raw) {
  return (raw ?? "").split(",").map((o) => o.trim()).filter(Boolean);
}

export function isDevelopment(env) {
  return env.NODE_ENV === "development"; // unset is NOT development: fail closed
}

// Throws outside development when the list is empty, holds "*", or holds an entry that is not an exact origin.
export function assertTrustedOrigins(origins, env) {
  if (isDevelopment(env)) return;
  if (origins.length === 0) throw new Error("BETTER_AUTH_TRUSTED_ORIGINS must be set outside development (NODE_ENV=development)");
  for (const o of origins) {
    if (o === "*") throw new Error("BETTER_AUTH_TRUSTED_ORIGINS cannot contain '*' outside development");
    let url;
    try { url = new URL(o); } catch { throw new Error(`BETTER_AUTH_TRUSTED_ORIGINS entry is not a URL: ${o}`); }
    if ((url.protocol !== "https:" && url.protocol !== "http:") || url.origin !== o) {
      throw new Error(`BETTER_AUTH_TRUSTED_ORIGINS entry must be an exact origin (scheme://host[:port], no path or trailing slash): ${o}`);
    }
  }
}

export function loadTrustedOrigins(env) {
  const origins = parseTrustedOrigins(env.BETTER_AUTH_TRUSTED_ORIGINS);
  assertTrustedOrigins(origins, env);
  return origins;
}

export function isCorsOriginAllowed(origin, origins, env) {
  if (!origin) return true;                               // non-browser callers, unchanged
  if (origins.includes(origin)) return true;
  return origins.length === 0 && isDevelopment(env);      // allow-all only in development
}

// D-48 (a): no crossSubDomainCookies and no Domain attribute, so cookies are host-only.
export function buildAdvancedOptions() {
  return {
    defaultCookieAttributes: { sameSite: "none", secure: true, httpOnly: true, partitioned: true },
  };
}
