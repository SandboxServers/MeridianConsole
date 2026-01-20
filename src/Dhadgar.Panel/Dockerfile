FROM node:22-alpine AS base
WORKDIR /app

# Install dependencies
FROM base AS deps
WORKDIR /app/src/Dhadgar.Panel
# Copy shared auth package first (needed for file: reference)
COPY src/Dhadgar.SharedAuth/ /app/src/Dhadgar.SharedAuth/
COPY src/Dhadgar.Panel/package*.json ./
RUN npm ci

# Build the application
FROM base AS builder
WORKDIR /app/src/Dhadgar.Panel
COPY --from=deps /app/src/Dhadgar.SharedAuth /app/src/Dhadgar.SharedAuth
COPY --from=deps /app/src/Dhadgar.Panel/node_modules ./node_modules
COPY src/Dhadgar.Panel/ .

# Build arguments for environment variables at build time
ARG PUBLIC_GATEWAY_URL=http://localhost:5000
ARG PUBLIC_BETTER_AUTH_URL=http://localhost:5000/api/v1/betterauth
ENV PUBLIC_GATEWAY_URL=${PUBLIC_GATEWAY_URL}
ENV PUBLIC_BETTER_AUTH_URL=${PUBLIC_BETTER_AUTH_URL}

# Build and prune dev dependencies
RUN npm run build && npm prune --omit=dev

# Production image
FROM base AS runner
ENV NODE_ENV=production
ENV HOST=0.0.0.0
ENV PORT=8080

# Copy built application and production dependencies
COPY --from=builder /app/src/Dhadgar.Panel/dist ./dist
COPY --from=builder /app/src/Dhadgar.Panel/node_modules ./node_modules
COPY --from=builder /app/src/Dhadgar.Panel/package.json ./

# Create non-root user
RUN addgroup --system --gid 1001 nodejs && \
    adduser --system --uid 1001 astro && \
    chown -R astro:nodejs /app

USER astro

EXPOSE 8080

CMD ["node", "dist/server/entry.mjs"]
