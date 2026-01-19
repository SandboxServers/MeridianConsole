FROM node:22-alpine AS base
WORKDIR /app

# Install dependencies
FROM base AS deps
COPY src/Dhadgar.ShoppingCart/package*.json ./
RUN npm ci

# Build the application
FROM base AS builder
COPY --from=deps /app/node_modules ./node_modules
COPY src/Dhadgar.ShoppingCart/ .

# Build arguments for environment variables at build time
ARG PUBLIC_GATEWAY_URL=http://localhost:5000
ARG PUBLIC_BETTER_AUTH_URL=http://localhost:5000/api/v1/betterauth
ENV PUBLIC_GATEWAY_URL=${PUBLIC_GATEWAY_URL}
ENV PUBLIC_BETTER_AUTH_URL=${PUBLIC_BETTER_AUTH_URL}

RUN npm run build

# Production image - serve static files with nginx
FROM nginx:alpine AS runner

# Copy built static files
COPY --from=builder /app/_swa_publish/wwwroot /usr/share/nginx/html

# Copy nginx config for SPA routing
RUN cat > /etc/nginx/conf.d/default.conf <<'EOF'
server {
    listen 8080;
    root /usr/share/nginx/html;
    index index.html;
    location / {
        try_files $uri $uri/ /index.html;
    }
    location ~* \.(js|css|png|jpg|jpeg|gif|ico|svg|woff|woff2)$ {
        expires 1y;
        add_header Cache-Control "public, immutable";
    }
}
EOF

EXPOSE 8080

CMD ["nginx", "-g", "daemon off;"]
