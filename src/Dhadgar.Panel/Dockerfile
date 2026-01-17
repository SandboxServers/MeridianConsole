FROM node:22-alpine AS base
WORKDIR /app

# Install dependencies
FROM base AS deps
COPY src/Dhadgar.Panel/package*.json ./
RUN npm ci

# Build the application
FROM base AS builder
COPY --from=deps /app/node_modules ./node_modules
COPY src/Dhadgar.Panel/ .

# Build arguments for environment variables at build time
ARG PUBLIC_GATEWAY_URL=http://localhost:5000
ARG PUBLIC_BETTER_AUTH_URL=http://localhost:5000/api/v1/betterauth
ENV PUBLIC_GATEWAY_URL=${PUBLIC_GATEWAY_URL}
ENV PUBLIC_BETTER_AUTH_URL=${PUBLIC_BETTER_AUTH_URL}

RUN npm run build

# Prune dev dependencies after build
RUN npm prune --omit=dev

# Production image
FROM base AS runner
ENV NODE_ENV=production
ENV HOST=0.0.0.0
ENV PORT=8080

# Copy built application and production dependencies
COPY --from=builder /app/dist ./dist
COPY --from=builder /app/node_modules ./node_modules
COPY --from=builder /app/package.json ./

# Create non-root user
RUN addgroup --system --gid 1001 nodejs && \
    adduser --system --uid 1001 astro && \
    chown -R astro:nodejs /app

USER astro

EXPOSE 8080

CMD ["node", "dist/server/entry.mjs"]
