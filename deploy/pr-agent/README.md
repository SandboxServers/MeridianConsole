# PR-Agent Setup for Meridian Console

Automated PR reviews with your CLAUDE.md architectural rules embedded.

## Architecture

```
GitHub PR → Webhook → Your Home Server (PR-Agent) → OpenRouter (DeepSeek Coder) → Review Posted to GitHub
```

## Prerequisites

- Docker and Docker Compose installed on home server / buddy's PC
- GitHub personal access token
- OpenRouter API key (free tier available)
- Port 3000 accessible (or ngrok for tunneling)

## Step 1: Get API Keys

### GitHub Token
1. Go to https://github.com/settings/tokens
2. Click "Generate new token (classic)"
3. Select scopes:
   - ✅ `repo` (all sub-scopes)
   - ✅ `write:discussion`
4. Generate and copy token
5. Save as `GITHUB_TOKEN`

### OpenRouter Key (Free)
1. Go to https://openrouter.ai/
2. Sign up (free account)
3. Go to Settings → API Keys
4. Create new API key
5. Save as `OPENROUTER_API_KEY`

**Free Models Available:**
- `deepseek/deepseek-coder` - Excellent for code review
- `meta-llama/llama-3.1-70b-instruct` - Great reasoning
- `qwen/qwen-2.5-coder-32b-instruct` - Purpose-built for code

## Step 2: Configure Environment

```bash
cd deploy/pr-agent

# Copy example env file
cp .env.example .env

# Edit with your keys
nano .env  # or vim, code, etc.
```

Update `.env` with your actual tokens:
```bash
GITHUB_TOKEN=ghp_xxxxxxxxxxxxxxxxxxxxxxxxxxxxx
OPENROUTER_API_KEY=sk-or-v1-xxxxxxxxxxxxxxxxxxxxxxx
```

## Step 3: Start PR-Agent

```bash
cd deploy/pr-agent

# Start the service
docker compose up -d

# Check logs
docker compose logs -f pr-agent

# Verify health
curl http://localhost:3000/health
```

## Step 4: Expose to Internet

Choose one option:

### Option A: ngrok (Easiest for Testing)

```bash
# Install ngrok: https://ngrok.com/download
ngrok http 3000

# Copy the HTTPS URL (looks like: https://abc123.ngrok.io)
# Use this URL for GitHub webhook
```

### Option B: Port Forwarding + DDNS (Production)

1. **Set up DDNS** (e.g., DuckDNS, No-IP):
   - Get a stable domain like `meridian-pr-agent.duckdns.org`

2. **Port forward on router**:
   - External port 3000 → Home server IP:3000
   - Enable TCP protocol

3. **Test externally**:
   ```bash
   curl http://your-domain.duckdns.org:3000/health
   ```

### Option C: Cloudflare Tunnel (Recommended for Production)

```bash
# Install cloudflared
# https://developers.cloudflare.com/cloudflare-one/connections/connect-apps/install-and-setup/

# Create tunnel
cloudflared tunnel create meridian-pr-agent

# Route traffic
cloudflared tunnel route dns meridian-pr-agent pr-agent.yourdomain.com

# Run tunnel
cloudflared tunnel run meridian-pr-agent
```

## Step 5: Configure GitHub Webhook

1. Go to your GitHub repo: `https://github.com/SandboxServers/MeridianConsole`

2. **Settings** → **Webhooks** → **Add webhook**

3. Configure:
   - **Payload URL**: `https://your-ngrok-or-domain.com/webhook`
   - **Content type**: `application/json`
   - **Secret**: (leave blank for now, or add to .env and config)
   - **SSL verification**: Enable
   - **Which events**:
     - ✅ Pull requests
     - ✅ Issue comments (optional)
     - ✅ Pull request reviews (optional)
   - **Active**: ✅ Checked

4. **Add webhook**

5. **Test**:
   - GitHub will send a test ping
   - Check "Recent Deliveries" tab
   - Should see 200 OK response

## Step 6: Test with a PR

1. Create a test branch:
   ```bash
   git checkout -b test/pr-agent-setup
   ```

2. Make a small change (e.g., add a comment to a file)

3. Commit and push:
   ```bash
   git add .
   git commit -m "Test PR-Agent setup"
   git push origin test/pr-agent-setup
   ```

4. Create PR on GitHub

5. **Within 30 seconds**, PR-Agent should:
   - Post a review comment
   - Add labels
   - Score the PR
   - Suggest improvements

## Configuration

Your PR-Agent is configured with Meridian Console architectural rules from `CLAUDE.md`:

### What It Checks

✅ **Architecture**:
- No cross-service ProjectReferences
- Database-per-service pattern
- Proper inter-service communication (HTTP or MassTransit only)

✅ **Security**:
- No hardcoded secrets
- Agent code changes flagged for security review
- Proper authentication patterns

✅ **Code Quality**:
- Async/await patterns
- Null safety
- Package management (Central Package Management)

✅ **Testing**:
- Tests for new features
- Integration tests for endpoints

### Review Commands

You can trigger reviews manually by commenting on PRs:

```
/review         - Full code review
/describe       - Generate PR description
/improve        - Suggest code improvements
/ask            - Ask questions about the code
/update_changelog - Update changelog (if enabled)
```

## Troubleshooting

### PR-Agent not responding

```bash
# Check logs
docker compose logs -f pr-agent

# Restart service
docker compose restart pr-agent

# Check health
curl http://localhost:3000/health
```

### GitHub webhook delivery failed

1. Go to repo Settings → Webhooks
2. Click on your webhook
3. Check "Recent Deliveries" tab
4. Look for errors in response

Common issues:
- Home server offline
- Firewall blocking port 3000
- ngrok tunnel expired (restart ngrok)
- Wrong payload URL

### OpenRouter rate limits

Free tier limits:
- Check usage at https://openrouter.ai/activity
- Switch models in `.pr_agent.toml` if hitting limits
- Consider local Ollama fallback (see below)

### Reviews are too strict / too lenient

Edit `../../.pr_agent.toml`:
- Adjust `extra_instructions` to tune strictness
- Change scoring thresholds
- Enable/disable specific checks

## Advanced: Local Ollama Fallback

To run 100% local (no OpenRouter):

1. **Uncomment Ollama service** in `docker-compose.yml`

2. **Start Ollama**:
   ```bash
   docker compose up -d ollama
   ```

3. **Pull model**:
   ```bash
   docker exec -it meridian-ollama ollama pull deepseek-coder:33b
   ```

4. **Update `.pr_agent.toml`**:
   ```toml
   [config]
   model = "ollama/deepseek-coder:33b"
   model_host = "http://ollama:11434"
   ```

5. **Restart PR-Agent**:
   ```bash
   docker compose restart pr-agent
   ```

**Hardware Requirements**:
- DeepSeek Coder 33B: 24GB RAM
- DeepSeek Coder 6.7B: 8GB RAM (smaller but still good)
- CodeLlama 13B: 16GB RAM

## Monitoring

### Check review stats

```bash
# View logs
docker compose logs -f pr-agent | grep "Review completed"

# Check OpenRouter usage
# https://openrouter.ai/activity
```

### Update PR-Agent

```bash
cd deploy/pr-agent

# Pull latest image
docker compose pull pr-agent

# Restart with new image
docker compose up -d pr-agent
```

## Costs

- **GitHub**: Free (using personal access token)
- **OpenRouter**: Free tier includes:
  - DeepSeek Coder: Unlimited free
  - Llama 3.1 70B: Rate-limited free tier
  - Check limits: https://openrouter.ai/models
- **Infrastructure**: Your home server (electricity only)

**Estimated cost**: $0/month for typical usage

## Security Notes

1. **Never commit `.env` file** - Already in `.gitignore`
2. **Rotate tokens periodically** - GitHub tokens should be rotated every 90 days
3. **Use GitHub App** for production (more secure than personal tokens)
4. **Enable webhook secret** in production to verify payloads

## Support

- PR-Agent Docs: https://pr-agent-docs.codium.ai/
- OpenRouter Docs: https://openrouter.ai/docs
- Issues: Check `docker compose logs -f pr-agent`

## Next Steps

1. ✅ Get API keys
2. ✅ Configure `.env`
3. ✅ Start Docker Compose
4. ✅ Set up internet exposure (ngrok/port forward)
5. ✅ Configure GitHub webhook
6. ✅ Test with a PR

Once working, consider:
- Setting up Cloudflare Tunnel for production
- Enabling local Ollama for offline capability
- Tuning review strictness in `.pr_agent.toml`
- Adding webhook secret for security
