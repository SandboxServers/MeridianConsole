# Contributing

## Suggested flow
- Branch from `develop`
- Open PRs into `develop`
- Keep PRs small; include tests when reasonable

## Repo conventions
- Message contracts live in `src/Shared/Dhadgar.Contracts`
- Avoid breaking changes to published contracts; evolve additively
- Each service owns its data and migrations

## PR Review Bot

This repository includes **spirit-of-the-diff**, a free AI-powered code review bot.

### How to use it

When your PR is ready for review, request an AI-powered code review by commenting:

```
/spirit
```

The bot will analyze your changes and post a detailed review including:
- Estimated effort to review (1-5 scale)
- Security concerns (if any)
- Recommended focus areas
- Specific code suggestions

### When to use it

- **Before requesting human review** - Get quick feedback on potential issues
- **For complex changes** - Get a second opinion on architecture decisions
- **For security-sensitive code** - Catch common security pitfalls early
- **When stuck** - Get suggestions for improvement approaches

### Multiple review bots

This repo may have multiple review bots active:
- **spirit-of-the-diff** (`/spirit`) - Manual, deep-dive reviews (free tier)
- **CodeRabbit** - Automatic reviews on every commit
- **Qodo** (`/review`) - PR-Agent SaaS offering (if enabled)

Use whichever fits your workflow. They don't conflict and can all run on the same PR.

### Technical details

See [`docs/SPIRIT_OF_THE_DIFF_SETUP.md`](docs/SPIRIT_OF_THE_DIFF_SETUP.md) for setup documentation.
