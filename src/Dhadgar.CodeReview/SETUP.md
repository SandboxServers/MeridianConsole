# Dhadgar.CodeReview - Quick Setup Guide

## ✅ Completed Implementation

The service is now fully implemented with:
- ✅ ASP.NET Core webhook endpoint
- ✅ GitHub App integration (JWT authentication, webhook verification)
- ✅ Ollama LLM integration for code review
- ✅ Review orchestration logic
- ✅ SQLite database for review history
- ✅ Comprehensive logging with Serilog

## Next Steps to Get Running

### 1. Install Ollama & Pull Model

```bash
# Install Ollama (choose one):
# Option A - WSL2/Linux:
curl https://ollama.ai/install.sh | sh

# Option B - Windows:
# Download from https://ollama.com/download

# Pull the DeepSeek Coder model (this will take a while - it's large!)
ollama pull deepseek-coder:33b

# Verify it's running
ollama list
```

### 2. Create GitHub App

1. Go to https://github.com/settings/apps/new
2. Fill in:
   - **GitHub App name**: `MeridianConsole-AI-Reviewer` (or whatever you want)
   - **Homepage URL**: `https://github.com/SandboxServers/MeridianConsole`
   - **Webhook URL**: `https://TEMP-URL.ngrok.io/webhook` (we'll update this later)
   - **Webhook secret**: Generate a random secret (save it!)

3. Set **Permissions**:
   - **Repository permissions**:
     - Pull requests: **Read & write**
     - Contents: **Read-only**
     - Metadata: **Read-only**

4. Subscribe to **events**:
   - ☑️ Pull request
   - ☑️ Pull request review comment
   - ☑️ Issue comment (for /review command)

5. Click **Create GitHub App**

6. **Generate a private key**:
   - Scroll down to "Private keys"
   - Click "Generate a private key"
   - Save the `.pem` file that downloads

7. **Install the app**:
   - Click "Install App" in the left sidebar
   - Choose "Only select repositories" or "All repositories"
   - Click Install
   - **Note the Installation ID** from the URL (it's the number after `/installations/`)

### 3. Configure the Service

```bash
# Navigate to the service directory
cd src/Dhadgar.CodeReview

# Create secrets directory
mkdir secrets

# Copy your GitHub App private key
cp ~/Downloads/your-app-name.*.private-key.pem secrets/github-app.pem

# Initialize user secrets
dotnet user-secrets init

# Set your GitHub App credentials
dotnet user-secrets set "GitHub:AppId" "YOUR_APP_ID"
dotnet user-secrets set "GitHub:InstallationId" "YOUR_INSTALLATION_ID"
dotnet user-secrets set "GitHub:WebhookSecret" "YOUR_WEBHOOK_SECRET"
```

**Where to find these values:**
- **App ID**: GitHub App settings page, top section
- **Installation ID**: From the URL when you installed the app (`/installations/INSTALLATION_ID`)
- **Webhook Secret**: The random secret you generated in step 2

### 4. Run the Service

```bash
# Make sure you're in the service directory
cd src/Dhadgar.CodeReview

# Run the service
dotnet run
```

You should see output like:
```
info: Dhadgar.CodeReview[0]
      Starting Dhadgar.CodeReview service...
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://localhost:5000
```

### 5. Expose with ngrok

In a **new terminal**:

```bash
ngrok http 5000
```

This will give you a URL like `https://abc123.ngrok.io`.

**Update your GitHub App webhook URL:**
1. Go back to your GitHub App settings
2. Update "Webhook URL" to `https://abc123.ngrok.io/webhook`
3. Save changes

### 6. Test It!

1. Open a pull request in your repository
2. Add a comment: `/review`
3. Watch the logs in your terminal - you should see:
   - Webhook received
   - PR diff fetched
   - LLM generating review
   - Review posted to GitHub

The first review will take longer (~30-60s) because the model needs to load into VRAM. Subsequent reviews will be much faster (~10-30s).

## Troubleshooting

### "Model not found"
```bash
ollama pull deepseek-coder:33b
```

### "GPU not detected"
```bash
# Check if NVIDIA drivers are working
nvidia-smi

# Test Ollama GPU access
ollama run deepseek-coder:33b "Hello"
```

### "Webhook signature invalid"
- Make sure the webhook secret in GitHub App settings matches what you set in user secrets
- Check the logs for the exact error

### "Cannot find private key"
- Make sure the `.pem` file is in `src/Dhadgar.CodeReview/secrets/github-app.pem`
- Check file permissions (should be readable)

## Configuration Options

Edit `appsettings.json` to customize:

```json
{
  "Ollama": {
    "Model": "deepseek-coder:33b",  // Or try deepseek-coder:7b for faster reviews
    "TimeoutSeconds": 300  // Increase if reviews timeout
  },
  "Review": {
    "MaxDiffSize": 50000,  // Skip PRs larger than this
    "MaxFilesPerReview": 20,  // Skip PRs with too many files
    "EnableAutoReview": false  // Set to true to review ALL new commits automatically
  }
}
```

## Running 24/7 (Optional)

For production use, you can:

1. **Windows Service**: Use NSSM or sc.exe to run as a service
2. **Keep ngrok running**: Get a paid ngrok account for a static URL
3. **Use a reverse proxy**: Set up Caddy or nginx with a real domain

But for testing, the current setup is perfect!

## What's Next?

- Try it on a real PR!
- Tweak the prompt in [OllamaService.cs](Services/OllamaService.cs) to improve reviews
- Adjust model size based on speed vs. quality tradeoff
- Add more trigger conditions if needed

## Need Help?

Check the logs in:
- Console output (when running `dotnet run`)
- `logs/codereview-YYYYMMDD.log` files

The service is completely independent from the main Dhadgar platform, so you can experiment freely!
