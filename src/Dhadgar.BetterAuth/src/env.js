// Loads a local .env file for development. Imported first by server.js so the
// values are in process.env before any other module evaluates.
// dotenv >= 17 logs an "injecting env" line on every load unless quiet is set.
// In containers there is no .env file and this is a silent no-op.
import { config } from "dotenv";

config({ quiet: true });
