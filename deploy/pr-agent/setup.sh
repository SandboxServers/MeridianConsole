#!/bin/bash
# Quick setup script for PR-Agent

set -e

echo "🤖 Meridian Console PR-Agent Setup"
echo "======================================"
echo ""

# Check if Docker is installed
if ! command -v docker &> /dev/null; then
    echo "❌ Docker not found. Please install Docker first:"
    echo "   https://docs.docker.com/get-docker/"
    exit 1
fi

if ! command -v docker-compose &> /dev/null && ! docker compose version &> /dev/null; then
    echo "❌ Docker Compose not found. Please install Docker Compose:"
    echo "   https://docs.docker.com/compose/install/"
    exit 1
fi

echo "✅ Docker found"
echo ""

# Check if .env exists
if [ ! -f .env ]; then
    echo "📝 Creating .env file from template..."
    cp .env.example .env
    echo ""
    echo "⚠️  Please edit .env and add your API keys:"
    echo "   - GITHUB_TOKEN (get from: https://github.com/settings/tokens)"
    echo "   - OPENROUTER_API_KEY (get from: https://openrouter.ai/)"
    echo ""
    echo "Run 'nano .env' or 'vim .env' to edit."
    echo ""
    read -p "Press Enter after you've configured .env..."
fi

# Validate .env has required keys
if ! grep -q "GITHUB_TOKEN=ghp_" .env && ! grep -q "GITHUB_TOKEN=.*[a-zA-Z0-9]" .env; then
    echo "⚠️  Warning: GITHUB_TOKEN not configured in .env"
fi

if ! grep -q "OPENROUTER_API_KEY=sk-or-" .env && ! grep -q "OPENROUTER_API_KEY=.*[a-zA-Z0-9]" .env; then
    echo "⚠️  Warning: OPENROUTER_API_KEY not configured in .env"
fi

echo "🚀 Starting PR-Agent..."
docker compose up -d

echo ""
echo "⏳ Waiting for PR-Agent to be healthy..."
sleep 5

# Check health
if curl -sf http://localhost:3000/health > /dev/null 2>&1; then
    echo "✅ PR-Agent is running and healthy!"
else
    echo "⚠️  PR-Agent started but health check failed. Checking logs..."
    docker compose logs pr-agent
fi

echo ""
echo "======================================"
echo "📊 Status:"
docker compose ps
echo ""
echo "📝 Next steps:"
echo ""
echo "1. Expose to internet:"
echo "   Option A (easiest): ngrok http 3000"
echo "   Option B: Port forward 3000 on your router"
echo ""
echo "2. Set up GitHub webhook:"
echo "   - URL: https://your-domain-or-ngrok.com/webhook"
echo "   - Content type: application/json"
echo "   - Events: Pull requests"
echo ""
echo "3. Test with a PR!"
echo ""
echo "📖 Full docs: ./README.md"
echo "📋 Logs: docker compose logs -f pr-agent"
echo "🛑 Stop: docker compose down"
echo ""
