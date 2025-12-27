# Quick setup script for PR-Agent (Windows PowerShell)

Write-Host "🤖 Meridian Console PR-Agent Setup" -ForegroundColor Cyan
Write-Host "======================================" -ForegroundColor Cyan
Write-Host ""

# Check if Docker is installed
try {
    docker --version | Out-Null
    Write-Host "✅ Docker found" -ForegroundColor Green
} catch {
    Write-Host "❌ Docker not found. Please install Docker Desktop:" -ForegroundColor Red
    Write-Host "   https://docs.docker.com/desktop/install/windows-install/" -ForegroundColor Yellow
    exit 1
}

Write-Host ""

# Check if .env exists
if (-Not (Test-Path ".env")) {
    Write-Host "📝 Creating .env file from template..." -ForegroundColor Yellow
    Copy-Item .env.example .env
    Write-Host ""
    Write-Host "⚠️  Please edit .env and add your API keys:" -ForegroundColor Yellow
    Write-Host "   - GITHUB_TOKEN (get from: https://github.com/settings/tokens)" -ForegroundColor Yellow
    Write-Host "   - OPENROUTER_API_KEY (get from: https://openrouter.ai/)" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Opening .env in notepad..." -ForegroundColor Cyan
    notepad .env
    Write-Host ""
    Read-Host "Press Enter after you've configured .env"
}

# Validate .env has required keys
$envContent = Get-Content .env -Raw
if (-Not ($envContent -match "GITHUB_TOKEN=ghp_" -or $envContent -match "GITHUB_TOKEN=.*[a-zA-Z0-9]")) {
    Write-Host "⚠️  Warning: GITHUB_TOKEN not configured in .env" -ForegroundColor Yellow
}

if (-Not ($envContent -match "OPENROUTER_API_KEY=sk-or-" -or $envContent -match "OPENROUTER_API_KEY=.*[a-zA-Z0-9]")) {
    Write-Host "⚠️  Warning: OPENROUTER_API_KEY not configured in .env" -ForegroundColor Yellow
}

Write-Host "🚀 Starting PR-Agent..." -ForegroundColor Cyan
docker compose up -d

Write-Host ""
Write-Host "⏳ Waiting for PR-Agent to be healthy..." -ForegroundColor Cyan
Start-Sleep -Seconds 5

# Check health
try {
    $response = Invoke-WebRequest -Uri "http://localhost:3000/health" -UseBasicParsing -ErrorAction Stop
    Write-Host "✅ PR-Agent is running and healthy!" -ForegroundColor Green
} catch {
    Write-Host "⚠️  PR-Agent started but health check failed. Checking logs..." -ForegroundColor Yellow
    docker compose logs pr-agent
}

Write-Host ""
Write-Host "======================================" -ForegroundColor Cyan
Write-Host "📊 Status:" -ForegroundColor Cyan
docker compose ps

Write-Host ""
Write-Host "📝 Next steps:" -ForegroundColor Cyan
Write-Host ""
Write-Host "1. Expose to internet:" -ForegroundColor White
Write-Host "   Option A (easiest): Download ngrok and run: ngrok http 3000" -ForegroundColor Yellow
Write-Host "   Option B: Port forward 3000 on your router" -ForegroundColor Yellow
Write-Host ""
Write-Host "2. Set up GitHub webhook:" -ForegroundColor White
Write-Host "   - URL: https://your-domain-or-ngrok.com/webhook" -ForegroundColor Yellow
Write-Host "   - Content type: application/json" -ForegroundColor Yellow
Write-Host "   - Events: Pull requests" -ForegroundColor Yellow
Write-Host ""
Write-Host "3. Test with a PR!" -ForegroundColor White
Write-Host ""
Write-Host "📖 Full docs: .\README.md" -ForegroundColor Cyan
Write-Host "📋 Logs: docker compose logs -f pr-agent" -ForegroundColor Cyan
Write-Host "🛑 Stop: docker compose down" -ForegroundColor Cyan
Write-Host ""
