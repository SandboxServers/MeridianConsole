// HF-02 (D-48, D-49): node:test coverage for the trusted-origin and cookie rules in src/config.js.
import { test } from "node:test";
import assert from "node:assert/strict";
import {
  parseTrustedOrigins,
  assertTrustedOrigins,
  isCorsOriginAllowed,
  buildAdvancedOptions,
} from "../src/config.js";

const COMPOSE_ORIGINS = [
  "https://dev.meridianconsole.com",
  "https://panel.meridianconsole.com",
  "http://localhost:4321",
];
const PROD = { NODE_ENV: "production" };
const DEV = { NODE_ENV: "development" };

test("parseTrustedOrigins trims, drops empties, and maps undefined to []", () => {
  assert.deepEqual(parseTrustedOrigins(undefined), []);
  assert.deepEqual(parseTrustedOrigins(""), []);
  assert.deepEqual(
    parseTrustedOrigins(" https://a.example , ,https://b.example ,"),
    ["https://a.example", "https://b.example"],
  );
});

test("assertTrustedOrigins throws for an empty list outside development", () => {
  assert.throws(() => assertTrustedOrigins([], PROD), /must be set outside development/);
  assert.throws(() => assertTrustedOrigins([], {}), /must be set outside development/);
});

test("assertTrustedOrigins rejects wildcard, trailing slash and bare host outside development", () => {
  assert.throws(() => assertTrustedOrigins(["*"], PROD), /cannot contain '\*'/);
  assert.throws(() => assertTrustedOrigins(["https://panel.meridianconsole.com/"], PROD), /exact origin/);
  assert.throws(() => assertTrustedOrigins(["panel.meridianconsole.com"], PROD), /not a URL|exact origin/);
});

test("assertTrustedOrigins passes for the compose list and for an empty list in development", () => {
  assert.doesNotThrow(() => assertTrustedOrigins(COMPOSE_ORIGINS, PROD));
  assert.doesNotThrow(() => assertTrustedOrigins([], DEV));
});

test("isCorsOriginAllowed refuses an unlisted origin when the list is empty outside development", () => {
  assert.equal(isCorsOriginAllowed("https://evil.example", [], PROD), false);
  assert.equal(isCorsOriginAllowed("https://evil.example", [], {}), false);
});

test("isCorsOriginAllowed allows a listed origin and a missing origin", () => {
  assert.equal(isCorsOriginAllowed("https://panel.meridianconsole.com", COMPOSE_ORIGINS, PROD), true);
  assert.equal(isCorsOriginAllowed(undefined, COMPOSE_ORIGINS, PROD), true);
  assert.equal(isCorsOriginAllowed(undefined, [], PROD), true);
});

test("isCorsOriginAllowed allows everything only in development with an empty list", () => {
  assert.equal(isCorsOriginAllowed("https://evil.example", [], DEV), true);
});

test("isCorsOriginAllowed refuses an unlisted origin with a non-empty list in both modes", () => {
  assert.equal(isCorsOriginAllowed("https://evil.example", COMPOSE_ORIGINS, PROD), false);
  assert.equal(isCorsOriginAllowed("https://evil.example", COMPOSE_ORIGINS, DEV), false);
  assert.equal(isCorsOriginAllowed("http://localhost:4322", COMPOSE_ORIGINS, DEV), false);
});

test("buildAdvancedOptions has no cross-subdomain or domain settings and exactly the four cookie attributes", () => {
  const options = buildAdvancedOptions();
  const json = JSON.stringify(options);
  assert.equal("crossSubDomainCookies" in options, false);
  assert.equal(json.includes('"domain"'), false);
  assert.deepEqual(options, {
    defaultCookieAttributes: { sameSite: "none", secure: true, httpOnly: true, partitioned: true },
  });
});
