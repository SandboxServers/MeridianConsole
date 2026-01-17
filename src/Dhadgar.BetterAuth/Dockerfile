FROM node:22-alpine AS base
WORKDIR /app

# Install dependencies
FROM base AS deps
COPY src/Dhadgar.BetterAuth/package*.json ./
RUN npm ci --omit=dev

# Production image
FROM base AS runner
ENV NODE_ENV=production
ENV PORT=8080

# Copy dependencies
COPY --from=deps /app/node_modules ./node_modules

# Copy source
COPY src/Dhadgar.BetterAuth/src ./src
COPY src/Dhadgar.BetterAuth/package.json ./

# Create non-root user
RUN addgroup --system --gid 1001 nodejs && \
    adduser --system --uid 1001 betterauth && \
    chown -R betterauth:nodejs /app

USER betterauth

EXPOSE 8080

CMD ["node", "src/server.js"]
