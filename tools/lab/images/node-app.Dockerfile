# syntax=docker/dockerfile:1
# L34: one image for the Node lab apps (BetterAuth, Panel). The build context is the folder LabBuilder.StageNodeApp fills:
# app/ (the app's source), SharedAuth/ (the file: dependency the Panel names) and lab/ (lab-only files, D-ML144).
# Node major matches the Panel's engines (>=22.23.4) and the BetterAuth Dockerfile (node:22).
FROM node:22-bookworm-slim

ARG APP_DIR=app
ARG APP_ENTRY=src/server.js
ARG PUBLIC_GATEWAY_URL=http://localhost:15000

WORKDIR /src
# The Panel's package.json and package-lock.json name file:../Dhadgar.SharedAuth, resolved from /src/app.
COPY SharedAuth /src/Dhadgar.SharedAuth
# Lab-only Secrets stub; loaded only where the adapter sets NODE_OPTIONS (BetterAuth, D-ML144).
COPY lab /lab
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

# exec so node is PID 1 and receives docker stop's SIGTERM.
CMD ["sh", "-c", "exec node \"$APP_ENTRY\""]
