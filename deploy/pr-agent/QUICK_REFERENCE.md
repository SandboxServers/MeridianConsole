# PR-Agent Quick Reference

## Deployment Commands

```bash
# Start PR-Agent
cd deploy/pr-agent
docker compose up -d

# Stop PR-Agent
docker compose down

# Restart PR-Agent
docker compose restart pr-agent

# View logs
docker compose logs -f pr-agent

# Update to latest version
docker compose pull pr-agent
docker compose up -d pr-agent
```

## PR Commands (Comment on GitHub PR)

Trigger these by commenting on any PR:

```
/review         - Full architectural and code review
/describe       - Auto-generate PR description
/improve        - Get code improvement suggestions
/ask            - Ask questions about the code changes
```

## Configuration Files

- **`.pr_agent.toml`** (root) - Main configuration with CLAUDE.md rules
- **`deploy/pr-agent/.env`** - API keys (DO NOT COMMIT)
- **`deploy/pr-agent/docker-compose.yml`** - Docker setup

## Architectural Rules Enforced

### 🚫 Will REJECT PRs that:
- Add ProjectReference between services
- Access another service's DbContext
- Hardcode secrets or connection strings
- Have cross-service database queries
- Add Agent code without security review

### ✅ Will APPROVE PRs that:
- Use Contracts/Shared/Messaging/ServiceDefaults only
- Follow database-per-service pattern
- Communicate via HTTP or MassTransit
- Include tests for new features
- Use central package management

## Review Scoring

- **0-4**: Critical violations, DO NOT MERGE
- **5-6**: Minor issues, needs fixes
- **7-8**: Good, minor suggestions
- **9-10**: Excellent, follows all patterns

## Troubleshooting

### PR-Agent not responding to PRs

```bash
# Check if container is running
docker compose ps

# View recent logs
docker compose logs --tail=50 pr-agent

# Check GitHub webhook deliveries
# Go to: Settings → Webhooks → Your webhook → Recent Deliveries
```

### Webhook delivery failing

1. **Check ngrok is running** (if using ngrok):
   ```bash
   ngrok http 3000
   ```

2. **Test webhook URL directly**:
   ```bash
   curl https://your-ngrok-url.ngrok.io/health
   ```

3. **Check firewall** (if port forwarding):
   ```bash
   # Test from external network
   curl http://your-external-ip:3000/health
   ```

### Reviews are too strict

Edit `.pr_agent.toml` in repo root:
- Adjust scoring thresholds
- Modify `extra_instructions` section
- Change `num_code_suggestions`

### OpenRouter rate limit

Check usage: https://openrouter.ai/activity

Switch to different free model in `.pr_agent.toml`:
```toml
model = "openrouter/meta-llama/llama-3.1-70b-instruct"
```

## Monitoring

### Check review activity

```bash
# Count reviews today
docker compose logs pr-agent | grep "Review completed" | grep "$(date +%Y-%m-%d)" | wc -l

# View OpenRouter usage
# Visit: https://openrouter.ai/activity
```

### Resource usage

```bash
# Container stats
docker stats meridian-pr-agent

# Disk usage
docker system df
```

## Security

### Rotate GitHub token

1. Create new token: https://github.com/settings/tokens
2. Update `.env`:
   ```bash
   nano deploy/pr-agent/.env
   # Update GITHUB_TOKEN=...
   ```
3. Restart:
   ```bash
   docker compose restart pr-agent
   ```

### Add webhook secret (recommended for production)

1. Generate secret:
   ```bash
   openssl rand -hex 32
   ```

2. Add to `.env`:
   ```bash
   GITHUB_WEBHOOK_SECRET=your_secret_here
   ```

3. Add same secret to GitHub webhook settings

## Example Review Output

When you create a PR, PR-Agent will:

1. **Post initial comment** within 30 seconds
2. **Score the PR** (0-10) based on architectural compliance
3. **Add labels**:
   - `architecture-approved` (score 7+)
   - `needs-review` (score 5-6)
   - `architecture-violation` (score <5)
4. **Leave inline comments** on specific lines
5. **Suggest improvements** with code examples

## Custom Rules (from CLAUDE.md)

Your setup enforces these Meridian Console rules:

1. **Microservices Isolation**: No cross-service ProjectReferences
2. **Database-per-Service**: Each service owns its schema
3. **Communication Patterns**: HTTP or MassTransit only
4. **Security**: Agent code changes flagged for review
5. **Configuration**: No hardcoded secrets
6. **Testing**: Tests required for new features
7. **Package Management**: Central package versions only

## Cost Tracking

- **GitHub**: $0 (using personal token)
- **OpenRouter**: $0 (free tier models)
- **Infrastructure**: Electricity for home server (~$5/month)

**Total**: ~$5/month

## Support & Docs

- **PR-Agent**: https://pr-agent-docs.codium.ai/
- **OpenRouter**: https://openrouter.ai/docs
- **Your Setup**: `deploy/pr-agent/README.md`

## Quick Health Check

```bash
# Is PR-Agent running?
docker compose ps | grep pr-agent

# Is it healthy?
curl http://localhost:3000/health

# Recent logs
docker compose logs --tail=20 pr-agent

# GitHub webhook recent deliveries
# Browser: Settings → Webhooks → Your webhook → Recent Deliveries
```
