# syntax=docker/dockerfile:1
# L34: one image for the Node lab apps (BetterAuth, Panel). The build context is the staged folder that
# LabBuilder fills: app/ (the app's source) and SharedAuth/ (the file: dependency the Panel names).
# Node major matches the Panel's engines (>=22.23.4) and the BetterAuth Dockerfile (node:22).
FROM node:22-bookworm-slim

ARG APP_DIR=app
ARG APP_ENTRY=src/server.js
ARG PUBLIC_GATEWAY_URL=http://localhost:15000

WORKDIR /src
COPY SharedAuth /src/SharedAuth
COPY ${APP_DIR} /src/app
WORKDIR /src/app

# npm ci needs the npm registry; the lab build is RequiresNetwork.
RUN npm ci

ENV PUBLIC_GATEWAY_URL=${PUBLIC_GATEWAY_URL}
RUN if grep -q '"build"' package.json; then npm run build && npm prune --omit=dev; fi

ENV NODE_ENV=production
ENV HOST=0.0.0.0
ENV PORT=8080
ENV APP_ENTRY=${APP_ENTRY}

USER node
EXPOSE 8080

# Shell form so the entry expands from APP_ENTRY: "dist/server/entry.mjs" for the Panel, "src/server.js" for BetterAuth.
CMD node ${APP_ENTRY}
