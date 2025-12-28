# Self-Review Setup Checklist

This checklist will help you configure the CodeReview service to review its own pull requests.

## Prerequisites

- [x] Docker Desktop with WSL2 backend running
- [x] RTX 4090 GPU accessible in Docker
- [x] Ollama with `deepseek-coder:33b` model loaded
- [ ] GitHub account with admin access to MeridianConsole repository

## Step 1: Create GitHub App

1. Go to https://github.com/settings/apps/new (or your org settings)
2. Fill in the form:
   - **Name**: `MeridianConsole-CodeReview` (or your choice)
   - **Homepage URL**: `https://github.com/YourUsername/MeridianConsole`
   - **Webhook URL**: `https://your-tunnel-url.com/webhook` (see Step 3)
   - **Webhook secret**: Generate a strong secret (save for later)
   - **Permissions**:
     - Repository permissions:
       - **Pull requests**: Read & Write
       - **Contents**: Read-only
       - **Metadata**: Read-only
   - **Subscribe to events**:
     - [x] Pull request
     - [x] Issue comment (for `/dhadgar` command)
   - **Where can this GitHub App be installed?**: Only on this account

3. Click **Create GitHub App**

4. **Generate Private Key**:
   - On the app settings page, scroll to "Private keys"
   - Click "Generate a private key"
   - Download the `.pem` file
   - Save it as `src/Dhadgar.CodeReview/secrets/github-app.pem`

5. **Note the App ID**:
   - At the top of the app settings page, note the "App ID"
   - Save for Step 2

## Step 2: Install GitHub App

1. On your GitHub App settings page, click "Install App" in the left sidebar
2. Click "Install" next to your account/organization
3. Choose "Only select repositories" → Select `MeridianConsole`
4. Click "Install"

5. **Note the Installation ID**:
   - After installation, look at the URL: `https://github.com/settings/installations/12345678`
   - The number at the end is your Installation ID
   - Save for Step 2

## Step 3: Configure Cloudflare Tunnel (Recommended)

**Option A: Use Cloudflare Tunnel (included in docker-compose)**

1. Create a Cloudflare account if you don't have one
2. Go to https://one.dash.cloudflare.com/
3. Navigate to **Access** → **Tunnels**
4. Click **Create a tunnel**
5. Name it `codereview-tunnel`
6. Copy the tunnel token
7. Save tunnel token for `.env` file (next step)

8. Configure tunnel route:
   - **Public hostname**: `codereview.yourdomain.com` (or use Cloudflare's provided subdomain)
   - **Service**: `http://codereview:8080`
   - **Type**: HTTP

9. Update GitHub App webhook URL to `https://codereview.yourdomain.com/webhook`

**Option B: Use ngrok (alternative)**

```bash
# Install ngrok
scoop install ngrok

# Expose port 8080
ngrok http 8080

# Copy the HTTPS URL (e.g., https://abc123.ngrok.io)
# Update GitHub App webhook URL to https://abc123.ngrok.io/webhook
```

**Note**: ngrok URLs change on restart unless you have a paid plan. Cloudflare Tunnel is more stable.

## Step 4: Configure Secrets

1. Create `.env` file in `src/Dhadgar.CodeReview/`:

```bash
# GitHub App Configuration
GITHUB_APP_ID=123456
GITHUB_INSTALLATION_ID=12345678
GITHUB_WEBHOOK_SECRET=your-webhook-secret-from-step-1

# Cloudflare Tunnel Token (if using Cloudflare)
CLOUDFLARE_TUNNEL_TOKEN=your-tunnel-token-from-step-3
```

2. Add `.env` to `.gitignore` (already done)

3. Create `secrets/` directory and add private key:

```bash
# From repository root
mkdir -p src/Dhadgar.CodeReview/secrets
# Copy your downloaded .pem file to:
# src/Dhadgar.CodeReview/secrets/github-app.pem
```

4. Verify file structure:
```
src/Dhadgar.CodeReview/
├── .env                          # Gitignored
├── secrets/
│   └── github-app.pem           # Gitignored
├── docker-compose.yml
└── ...
```

## Step 5: Deploy Service

From repository root:

```bash
# Rebuild Docker image (includes bundled agents)
docker build -f src/Dhadgar.CodeReview/Dockerfile -t dhadgar/codereview:latest .

# Deploy all services
cd src/Dhadgar.CodeReview
docker compose up -d

# Verify all containers are running
docker compose ps

# Check logs
docker compose logs -f codereview
```

Expected output:
```
codereview-service      | [INF] Starting Dhadgar.CodeReview service...
codereview-service      | [INF] Environment: Production
codereview-service      | [INF] Now listening on: http://[::]:8080
```

## Step 6: Verify Configuration

1. **Check service health**:
   ```bash
   curl http://localhost:8080/healthz
   ```
   Expected: `{"service":"Dhadgar.CodeReview","status":"ok",...}`

2. **Check agent discovery**:
   ```bash
   docker exec codereview-service ls -la /app/agents/
   ```
   Expected: 15 `.md` files

3. **Check Ollama model**:
   ```bash
   docker exec codereview-ollama ollama list
   ```
   Expected: `deepseek-coder:33b` listed

4. **Test webhook endpoint** (if using Cloudflare):
   ```bash
   curl https://codereview.yourdomain.com/webhook
   ```
   Expected: 405 Method Not Allowed (webhook only accepts POST)

5. **Check GitHub App webhook deliveries**:
   - Go to GitHub App settings → Advanced → Recent Deliveries
   - Click "Redeliver" on test delivery
   - Should show 200 OK response

## Step 7: Test Self-Review

1. **Create a test branch**:
   ```bash
   git checkout -b test/codereview-self-test
   ```

2. **Make a trivial change**:
   ```bash
   # Add a comment to a file
   echo "// Test comment" >> src/Dhadgar.CodeReview/Program.cs
   git add .
   git commit -m "Test: Trigger CodeReview self-review"
   git push origin test/codereview-self-test
   ```

3. **Create PR on GitHub**

4. **Trigger review**:
   - **Automatic** (if `EnableAutoReview=true` in docker-compose.yml)
   - **Manual**: Comment `/dhadgar` on the PR

5. **Monitor logs**:
   ```bash
   docker compose logs -f codereview
   ```

   Expected log flow:
   ```
   [INF] Received webhook: pull_request (action: opened)
   [INF] Fetching PR #XX from SandboxServers/MeridianConsole
   [INF] Generating review with LLM (deepseek-coder:33b)...
   [INF] Consulting Council of Greybeards...
   [INF] Consulting security-architect...
   [INF] Consulting database-schema-architect...
   [INF] Posting review to GitHub...
   ```

6. **Check PR for review comments**

## Troubleshooting

### Webhook not receiving events
- Verify tunnel is running: `docker compose ps cloudflared`
- Check tunnel status in Cloudflare dashboard
- Verify GitHub App webhook URL matches tunnel URL
- Check GitHub App webhook deliveries for errors

### "Could not parse agent response"
- Agent's LLM response didn't match expected JSON format
- Check logs for raw response
- May need to refine agent prompt or increase temperature

### Ollama timeout
- Large PRs may exceed 300s timeout
- Increase `Ollama__TimeoutSeconds` in docker-compose.yml
- Check GPU is being used: `docker exec codereview-ollama nvidia-smi`

### Review too large for GitHub
- Service automatically splits reviews
- Check logs for "Review is too large" warnings
- Reviews posted as multiple comments

### Missing agents
- Verify agents copied to container: `docker exec codereview-service ls /app/agents/`
- If missing, rebuild Docker image
- Check Dockerfile COPY commands

## Expected Review Output

For a self-review PR, you should see:

```markdown
## General Code Review

This PR adds GPU-accelerated code review capabilities using DeepSeek Coder 33B...

## Council of Greybeards

Expert opinions from specialized domain agents:

### 🧙 Security Architect

🚨 **CRITICAL FINDINGS**:
1. GitHub webhook signature verification needs testing (line 42 in WebhookController.cs)
2. Ensure HMAC comparison is timing-attack resistant

**2 specific concern(s) raised** (see inline comments below)

### 🧙 Database Schema Architect

The SQLite schema design looks appropriate for this use case...

**1 specific concern(s) raised** (see inline comments below)

### 🧙 DotNet 10 Researcher

Good use of nullable reference types and async patterns...

### Consulted (No Comments)

- **Messaging Engineer**: No messaging-related changes
- **Microservices Architect**: Service boundaries are appropriate
- ...
```

## Production Deployment Notes

For long-term production use:

1. **Use Cloudflare Tunnel** (not ngrok) for stable webhook URL
2. **Enable auto-review sparingly**: Set `EnableAutoReview=false` and use `/review` command
3. **Monitor GPU memory**: Large PRs with 15 agents can use significant VRAM
4. **Set up log rotation**: Logs grow quickly with detailed reviews
5. **Consider rate limiting**: GitHub API has limits (5000 requests/hour)
6. **Backup SQLite database**: `docker compose exec codereview cp /app/data/codereview.db /app/data/backup.db`

## Cleanup

To remove test PR and resources:

```bash
# Stop services
docker compose down

# Remove test branch
git branch -D test/codereview-self-test
git push origin --delete test/codereview-self-test

# Close/delete test PR on GitHub
```

---

**Ready to review itself?** Follow the checklist and create a PR! 🚀
