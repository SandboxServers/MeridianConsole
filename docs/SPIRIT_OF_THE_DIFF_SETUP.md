# spirit-of-the-diff Setup Guide

Complete documentation for setting up a free AI-powered PR review bot using PR-Agent, OpenRouter, and Mistral Devstral.

## Overview

**spirit-of-the-diff** is a custom GitHub App bot that provides AI-powered code reviews using:
- **PR-Agent** (Qodo's open-source PR review tool)
- **OpenRouter** (API gateway for LLM providers)
- **Mistral Devstral 2512** (free, code-optimized model)
- **GitHub Actions** (workflow orchestration)
- **GitHub App** (custom bot identity)

**Cost:** $0.00 (uses free tier models)

---

## Prerequisites

1. **GitHub repository** with Actions enabled
2. **OpenRouter account** (free): https://openrouter.ai
3. **GitHub App** creation permissions (org or personal account)

---

## Part 1: OpenRouter API Key Setup

### 1.1 Create OpenRouter Account

1. Visit https://openrouter.ai
2. Sign up with GitHub or email
3. Navigate to **Keys** section
4. Click **Create Key**
5. Name it (e.g., "spirit-of-the-diff")
6. Copy the API key (starts with `sk-or-v1-...`)

### 1.2 Important Notes

- Free tier has no token limits, only rate limits (600 requests/minute)
- Free models available: `mistralai/devstral-2512:free`, `mistralai/mistral-7b-instruct:free`
- Usage tracked at: https://openrouter.ai/activity

---

## Part 2: GitHub App Creation

### 2.1 Create the App

1. Go to GitHub Settings → Developer Settings → GitHub Apps
2. Click **New GitHub App**
3. Fill in details:

**App Name:** `spirit-of-the-diff` (must be unique across GitHub)

**Homepage URL:** Your repo or org URL

**Webhook:**
- Uncheck "Active" (we don't need webhooks)

**Permissions:**
- Repository permissions:
  - **Contents:** Read and write
  - **Pull requests:** Read and write
  - **Issues:** Read and write

**Where can this GitHub App be installed:**
- Select **"Any account"** (allows installation in organizations)

4. Click **Create GitHub App**

### 2.2 Note App ID

After creation, you'll see:
- **App ID:** (e.g., `2550753`) - copy this!

### 2.3 Generate Private Key

1. Scroll to **Private keys** section
2. Click **Generate a private key**
3. A `.pem` file will download automatically
4. Keep this file secure (it's the bot's authentication credential)

### 2.4 Install the App

1. Click **Install App** in the left sidebar
2. Select your organization or account
3. Choose **"Only select repositories"** or **"All repositories"**
4. Select your target repository
5. Click **Install**

---

## Part 3: GitHub Secrets Configuration

### 3.1 Add Secrets to Repository

Navigate to: `Settings → Secrets and variables → Actions → New repository secret`

Add these three secrets:

**Secret 1: OPENROUTER_API_KEY**
```
Name: OPENROUTER_API_KEY
Secret: sk-or-v1-... (your OpenRouter API key from Part 1)
```

**Secret 2: SPIRIT_APP_ID**
```
Name: SPIRIT_APP_ID
Secret: 2550753 (your App ID from Part 2.2)
```

**Secret 3: SPIRIT_PRIVATE_KEY**
```
Name: SPIRIT_PRIVATE_KEY
Secret: (paste entire contents of the .pem file, including BEGIN/END lines)
```

Example `.pem` content:
```
-----BEGIN RSA PRIVATE KEY-----
MIIEpAIBAAKCAQEA...
(many lines)
...
-----END RSA PRIVATE KEY-----
```

---

## Part 4: GitHub Actions Workflow

### 4.1 Create Workflow File

Create `.github/workflows/pr-agent-review.yml`:

```yaml
name: PR-Agent Review (Manual)

on:
  issue_comment:
    types: [created]

permissions:
  contents: write
  pull-requests: write
  issues: write

jobs:
  pr-agent:
    runs-on: ubuntu-latest
    # Only run when someone comments with /spirit command on a PR
    if: ${{ github.event.issue.pull_request && startsWith(github.event.comment.body, '/spirit') }}
    steps:
      - name: Generate token for spirit-of-the-diff
        id: generate_token
        uses: actions/create-github-app-token@v1
        with:
          app-id: ${{ secrets.SPIRIT_APP_ID }}
          private-key: ${{ secrets.SPIRIT_PRIVATE_KEY }}

      - name: PR-Agent action
        uses: qodo-ai/pr-agent@main
        env:
          # OpenRouter configuration (PR-Agent reads OPENROUTER.KEY and sets OPENROUTER_API_KEY internally)
          OPENROUTER.KEY: ${{ secrets.OPENROUTER_API_KEY }}
          # Mistral Devstral - Code-optimized, FREE (LiteLLM requires openrouter/ prefix)
          config.model: "openrouter/mistralai/devstral-2512:free"
          config.fallback_models: '["openrouter/mistralai/mistral-7b-instruct:free"]'
          config.custom_model_max_tokens: "262144"
          # Use spirit-of-the-diff bot token
          GITHUB_TOKEN: ${{ steps.generate_token.outputs.token }}
          # Optional: customize behavior
          PR_REVIEWER.AUTO_REVIEW: "false"
          PR_REVIEWER.REQUIRE_TESTS_REVIEW: "false"
```

### 4.2 Key Configuration Explained

**Trigger:**
```yaml
on:
  issue_comment:
    types: [created]
if: ${{ github.event.issue.pull_request && startsWith(github.event.comment.body, '/spirit') }}
```
- Triggers when ANY comment is created
- Filters to only PR comments starting with `/spirit`
- Change `/spirit` to any command you want (e.g., `/review`, `/check`, `/analyze`)

**Environment Variables:**

| Variable | Value | Purpose |
|----------|-------|---------|
| `OPENROUTER.KEY` | API key secret | PR-Agent's custom config format |
| `config.model` | `openrouter/mistralai/devstral-2512:free` | Model selection with provider prefix |
| `config.fallback_models` | Array of backup models | Fallback if primary fails |
| `config.custom_model_max_tokens` | `262144` | Model's actual context window (256k) |
| `GITHUB_TOKEN` | Bot's generated token | Allows posting as spirit-of-the-diff[bot] |

**Critical Details:**

1. **Model prefix:** Must use `openrouter/` prefix for LiteLLM routing
2. **API key name:** Must be `OPENROUTER.KEY` (with dot), not `OPENROUTER_API_KEY`
3. **Token limit:** Must specify `config.custom_model_max_tokens` for non-standard models
4. **Workflow location:** Must be in default branch (`main`) to trigger on PR comments

---

## Part 5: Testing the Bot

### 5.1 Merge Workflow to Main

**IMPORTANT:** The workflow file must be in the `main` branch (or default branch) to work.

```bash
git checkout main
git pull
# Make sure .github/workflows/pr-agent-review.yml is in main
git push origin main
```

### 5.2 Create Test PR

```bash
git checkout -b test-spirit-bot
# Make some code changes
git add .
git commit -m "test: verify spirit-of-the-diff bot"
git push -u origin test-spirit-bot
gh pr create --title "Test spirit-of-the-diff" --body "Testing the bot"
```

### 5.3 Invoke the Bot

1. Go to the PR on GitHub
2. Add a comment: `/spirit`
3. Watch for:
   - Workflow run in Actions tab
   - Bot posts "Preparing review..."
   - Bot posts full review with findings

### 5.4 Verify Success

**Check GitHub Actions:**
```bash
gh run list --workflow pr-agent-review.yml --limit 5
```

**Check OpenRouter usage:**
- Visit https://openrouter.ai/activity
- Look for recent requests to `Devstral 2 2512 (free)`
- Cost should be $0

**Expected review format:**
- Estimated effort (1-5 scale)
- Key issues to review (bugs, code smells)
- Security concerns
- Posted by: `spirit-of-the-diff[bot]`

---

## Part 6: Customization Options

### 6.1 Change Command Trigger

Edit workflow file `.github/workflows/pr-agent-review.yml`:

```yaml
# Change this line:
if: ${{ github.event.issue.pull_request && startsWith(github.event.comment.body, '/spirit') }}

# Examples:
if: ${{ github.event.issue.pull_request && startsWith(github.event.comment.body, '/ai-review') }}
if: ${{ github.event.issue.pull_request && startsWith(github.event.comment.body, '/analyze') }}
if: ${{ github.event.issue.pull_request && startsWith(github.event.comment.body, '/devstral') }}
```

### 6.2 Switch to Different Free Model

Available free models on OpenRouter:

```yaml
# Mistral Devstral (code-optimized, best for code reviews)
config.model: "openrouter/mistralai/devstral-2512:free"

# Mistral 7B Instruct (general purpose)
config.model: "openrouter/mistralai/mistral-7b-instruct:free"

# Check OpenRouter for other free models
```

### 6.3 Adjust Review Strictness

```yaml
# Require test coverage review
PR_REVIEWER.REQUIRE_TESTS_REVIEW: "true"

# Enable automatic approval for trivial changes
config.enable_auto_approval: "true"
config.auto_approve_for_low_review_effort: "1"

# Change max findings count
PR_REVIEWER.NUM_MAX_FINDINGS: "5"

# Add custom instructions
PR_REVIEWER.EXTRA_INSTRUCTIONS: "Focus on security vulnerabilities and performance issues"
```

### 6.4 Enable Auto-Review (Not Recommended)

To make the bot review automatically without `/spirit` command:

```yaml
on:
  pull_request:
    types: [opened, synchronize, reopened]

# Remove the 'if' condition
# Change this:
PR_REVIEWER.AUTO_REVIEW: "false"
# To:
PR_REVIEWER.AUTO_REVIEW: "true"
```

**Warning:** This will cause conflicts if you have other PR bots (like Qodo SaaS).

---

## Part 7: Troubleshooting

### 7.1 Common Issues

**Issue: Workflow doesn't trigger**
- Ensure workflow file is in `main` branch, not PR branch
- `issue_comment` workflows only run from default branch
- Check if command matches exactly (case-sensitive)

**Issue: Authentication errors**
```
AuthenticationError: OpenrouterException - No cookie auth credentials found
```
- Verify `OPENROUTER.KEY` (with dot) is used, not `OPENROUTER_API_KEY`
- Check API key is valid in OpenRouter dashboard
- Ensure secret is named correctly in GitHub

**Issue: Model not found**
```
Model mistralai/devstral-2512:free is not defined in MAX_TOKENS
```
- Add `config.custom_model_max_tokens: "262144"`
- Ensure `openrouter/` prefix is included

**Issue: Bot doesn't post review**
- Check workflow logs: `gh run view [run-id] --log`
- Verify GitHub App has correct permissions
- Ensure `GITHUB_TOKEN` is using bot token, not default

**Issue: Review quality is poor**
- Try different model (Devstral is code-optimized)
- Increase `PR_REVIEWER.NUM_MAX_FINDINGS`
- Add `PR_REVIEWER.EXTRA_INSTRUCTIONS` with specific guidance

### 7.2 Debugging Commands

```bash
# List recent workflow runs
gh run list --workflow pr-agent-review.yml --limit 10

# View specific run logs
gh run view [run-id] --log

# Check for errors
gh run view [run-id] --log | grep -i error

# Check OpenRouter usage
# Visit: https://openrouter.ai/activity

# Test GitHub App permissions
gh api /repos/OWNER/REPO/installation

# View PR comments
gh pr view [PR-NUMBER] --json comments
```

---

## Part 8: Advanced Configuration

### 8.1 PR-Agent Configuration Files

Create `.pr_agent.toml` in repo root for advanced settings:

```toml
[config]
model = "openrouter/mistralai/devstral-2512:free"
fallback_models = ["openrouter/mistralai/mistral-7b-instruct:free"]
custom_model_max_tokens = 262144

[pr_reviewer]
# Focus areas
require_tests_review = false
require_security_review = true
require_estimate_effort_to_review = true

# Finding limits
num_max_findings = 3
enable_review_labels_security = true
enable_review_labels_effort = true

# Custom prompts
extra_instructions = """
Focus on:
- Security vulnerabilities (SQL injection, XSS, etc.)
- Performance bottlenecks
- Code maintainability
- Test coverage gaps
"""

[github_action_config]
# Disable auto-actions
auto_review = false
auto_describe = false
auto_improve = false
```

**Note:** TOML files can be tricky. If you get parsing errors, use environment variables in workflow instead.

### 8.2 Multiple Bots Configuration

To run multiple bots without conflicts:

**Bot 1: spirit-of-the-diff** (manual trigger)
```yaml
if: ${{ github.event.issue.pull_request && startsWith(github.event.comment.body, '/spirit') }}
```

**Bot 2: Qodo SaaS** (automatic)
```yaml
on:
  pull_request:
    types: [opened, synchronize]
```

**Bot 3: CodeRabbit** (automatic)
```yaml
on:
  pull_request:
    types: [opened, synchronize]
```

They can all coexist if manual bots use different commands.

---

## Part 9: Cost & Performance

### 9.1 Expected Usage

**Typical PR review:**
- Prompt tokens: ~1,500-2,500 (PR diff + instructions)
- Completion tokens: ~200-500 (review findings)
- Total: ~2,000-3,000 tokens per review
- Speed: ~80-90 tokens/second
- Time: ~5-10 seconds per review

**Free tier limits:**
- Rate limit: 600 requests/minute (generous)
- No token limits on free models
- No monthly caps

### 9.2 Cost Comparison

| Provider | Model | Cost per Review | Notes |
|----------|-------|-----------------|-------|
| OpenRouter (Free) | Mistral Devstral | $0.00 | What we use |
| OpenAI | GPT-4o | ~$0.01-0.02 | Paid tier |
| Anthropic | Claude 3.5 Sonnet | ~$0.015-0.03 | Paid tier |
| Gemini | Gemini 1.5 Flash | $0.00-0.001 | Quota limits |

**Estimated annual cost:**
- 1,000 PRs/year × $0.00 = **$0.00/year**

---

## Part 10: Best Practices

### 10.1 Security

✅ **Do:**
- Store API keys in GitHub Secrets (never commit)
- Use GitHub App with minimal required permissions
- Rotate private keys periodically
- Review OpenRouter activity regularly

❌ **Don't:**
- Commit `.pem` files to repo
- Share API keys in issues/PRs
- Grant unnecessary permissions to GitHub App
- Use same API key across multiple bots

### 10.2 Usage Patterns

**Recommended:**
- Manual trigger (`/spirit`) to avoid spam
- Use for complex/risky PRs
- Complement with other tools (tests, linters)
- Review bot findings before acting

**Avoid:**
- Auto-review on every PR (creates noise)
- Relying solely on AI reviews
- Ignoring bot suggestions without investigation
- Multiple bots reviewing same PRs automatically

### 10.3 Maintenance

**Monthly:**
- Check OpenRouter usage dashboard
- Review bot feedback quality
- Update workflow if PR-Agent releases new features

**Quarterly:**
- Rotate GitHub App private key
- Review GitHub App permissions
- Check for new free models on OpenRouter

**Annually:**
- Re-evaluate bot value vs. effort
- Consider upgrading to paid models if budget allows
- Update documentation with lessons learned

---

## Part 11: Example Output

### 11.1 Successful Review

```markdown
## PR Reviewer Guide 🔍

Here are some key observations to aid the review process:

⏱️ **Estimated effort to review**: 2 🔵🔵⚪⚪⚪
🔒 **No security concerns identified**

⚡ **Recommended focus areas for review**

**Possible Bug**
The theory test `Hello_message_matches_expected_values` includes a
case-sensitive comparison with a lowercase test case that will
likely fail against the actual message.
[View code](link)

**Code Smell**
The test `Hello_message_has_expected_length` uses a hardcoded
magic number (26) for the expected length, making it fragile.
[View code](link)
```

---

## Part 12: Migration Guide

### 12.1 From Other Bots to spirit-of-the-diff

**From CodeRabbit:**
- Disable auto-review in CodeRabbit settings
- Keep CodeRabbit for automatic reviews
- Use spirit-of-the-diff for manual deep-dives

**From Qodo SaaS:**
- Keep Qodo for automatic reviews
- Use different command (`/spirit` vs `/review`)
- Both can run on same PRs

**From GitHub Copilot:**
- Copilot is IDE-integrated (different use case)
- spirit-of-the-diff is CI/CD integrated
- Complementary tools

### 12.2 Upgrading to Paid Models

If you want better quality and have budget:

```yaml
# Option 1: OpenAI GPT-4o (best quality)
config.model: "openai/gpt-4o"
# Add secret: OPENAI_KEY

# Option 2: Anthropic Claude 3.5 Sonnet (great reasoning)
config.model: "anthropic/claude-3-5-sonnet-20241022"
# Add secret: ANTHROPIC_KEY

# Option 3: Keep OpenRouter, use paid models
config.model: "openrouter/anthropic/claude-3.5-sonnet"
# Same OPENROUTER.KEY, but costs ~$0.015 per review
```

---

## Appendix A: Configuration Reference

### Complete Environment Variables

```yaml
# === Authentication ===
OPENROUTER.KEY: ${{ secrets.OPENROUTER_API_KEY }}
GITHUB_TOKEN: ${{ steps.generate_token.outputs.token }}

# === Model Configuration ===
config.model: "openrouter/mistralai/devstral-2512:free"
config.fallback_models: '["openrouter/mistralai/mistral-7b-instruct:free"]'
config.custom_model_max_tokens: "262144"

# === PR Reviewer Settings ===
PR_REVIEWER.AUTO_REVIEW: "false"
PR_REVIEWER.REQUIRE_TESTS_REVIEW: "false"
PR_REVIEWER.REQUIRE_SECURITY_REVIEW: "true"
PR_REVIEWER.NUM_MAX_FINDINGS: "3"
PR_REVIEWER.EXTRA_INSTRUCTIONS: "Custom prompt here"

# === GitHub Action Config ===
github_action_config.auto_review: "false"
github_action_config.auto_describe: "false"
github_action_config.auto_improve: "false"
```

---

## Appendix B: Workflow Troubleshooting Matrix

| Symptom | Likely Cause | Fix |
|---------|--------------|-----|
| Workflow doesn't trigger | Not in main branch | Merge workflow to main |
| Auth error "No cookie" | Wrong env var name | Use `OPENROUTER.KEY` not `OPENROUTER_API_KEY` |
| "Model not defined" | Missing token config | Add `config.custom_model_max_tokens` |
| "Provider not found" | Missing prefix | Add `openrouter/` to model name |
| Bot doesn't post | Wrong GitHub token | Use bot token not default `GITHUB_TOKEN` |
| Review is empty | Model failed silently | Check workflow logs for errors |
| Rate limit hit | Too many requests | Check OpenRouter dashboard |

---

## Appendix C: Resources

**Official Documentation:**
- PR-Agent: https://github.com/qodo-ai/pr-agent
- OpenRouter: https://openrouter.ai/docs
- GitHub Apps: https://docs.github.com/en/apps
- GitHub Actions: https://docs.github.com/en/actions

**Community:**
- PR-Agent Issues: https://github.com/qodo-ai/pr-agent/issues
- OpenRouter Discord: https://discord.gg/openrouter

**Monitoring:**
- OpenRouter Activity: https://openrouter.ai/activity
- GitHub Actions Runs: `gh run list --workflow pr-agent-review.yml`

---

## Appendix D: Changelog

### 2025-12-28 - Initial Setup

**Configuration iterations:**
1. ❌ `OPENAI.MODEL` → ✅ `config.model`
2. ❌ `32000` tokens → ✅ `262144` tokens
3. ❌ Missing provider prefix → ✅ `openrouter/`
4. ❌ `OPENAI_KEY` → ✅ `OPENROUTER_API_KEY` → ✅ `OPENROUTER.KEY`

**Lessons learned:**
- PR-Agent uses custom settings format (`.KEY` not `_KEY`)
- LiteLLM requires provider prefixes for routing
- Custom models need `config.custom_model_max_tokens`
- Workflow must be in default branch for `issue_comment` trigger

**Cost:**
- Total setup time: ~2 hours (with debugging)
- Total cost: $0.00
- First successful review: 1,876 prompt + 216 completion tokens

---

**Document Version:** 1.0
**Last Updated:** 2025-12-28
**Maintained By:** Generated via Claude Code
