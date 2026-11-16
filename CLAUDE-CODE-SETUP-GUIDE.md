# Claude Code Setup Guide

As of 2026-11-16

## Overview

This is a lean Claude Code setup on a Windows 11 laptop, used to build an Angular + ASP.NET Core (.NET 10) web app. It has two MCP servers, two skill frameworks and a few rules files. Everything here is public tooling: copy what fits your stack and skip the rest.

Commands are for PowerShell. Check each package name on its GitHub page before installing, since names change.

## Installed: base tools

| Tool | Why | Install |
| --- | --- | --- |
| Git | Version control | `winget install Git.Git` |
| GitHub CLI | PRs and issues from the terminal | `winget install GitHub.cli` |
| Node.js LTS | Angular, npm-based MCP servers | `winget install OpenJS.NodeJS.LTS` |
| uv | Installs Python tools such as Serena | `winget install astral-sh.uv` |
| .NET 10 SDK | Backend | `winget install Microsoft.DotNet.SDK.10` |
| Bun | Needed by gstack's setup | `winget install Oven-sh.Bun` |
| Claude Code | The CLI itself | `npm i -g @anthropic-ai/claude-code`, then run `claude` and log in with a personal account |

Docker was skipped on purpose: no MCP server in this setup needs it.

## Installed: Claude Code settings, MCP servers and plugins

**User settings** in `~/.claude/settings.json`:

```json
{
  "env": { "MCP_TIMEOUT": "120000" },
  "alwaysThinkingEnabled": true,
  "effortLevel": "medium",
  "theme": "dark"
}
```

Raise `effortLevel` to `high` for hard design or debugging work.

**MCP servers.** Only two are installed, to keep startup fast and context small.

| Server | What it gives you | Install and register |
| --- | --- | --- |
| Serena | Symbol-aware code navigation and editing | `uv tool install git+https://github.com/oraios/serena`, then `claude mcp add serena --scope user -- serena start-mcp-server --context claude-code --project-from-cwd --enable-web-dashboard false` |
| context7 | Up-to-date library and framework docs | `npm i -g @upstash/context7-mcp`, then `claude mcp add context7 --scope user -- context7-mcp.cmd` |

Run `claude mcp list` afterwards; both should show Connected.

**Plugins and skill frameworks.**

| Plugin | Role | Install |
| --- | --- | --- |
| Superpowers | Process: brainstorm, plan, TDD, debug, review | `claude plugin marketplace add obra/superpowers-marketplace`, then `claude plugin install superpowers@superpowers-marketplace --scope user` |
| Superpowers for Claude Code | Docs and workflows for writing your own skills, hooks and plugins | `claude plugin install superpowers-developing-for-claude-code@superpowers-marketplace --scope user` |
| gstack | Roles: code review, QA, ship, design, browser | In Git Bash: `git clone --single-branch --depth 1 https://github.com/garrytan/gstack.git ~/.claude/skills/gstack`, then `cd ~/.claude/skills/gstack && ./setup --host claude --prefix --no-skill-routing` |

gstack notes:

- `--prefix` names commands `/gstack-review`, `/gstack-qa` and so on, so they don't clash with Superpowers.
- `--no-skill-routing` skips a per-prompt routing hook, which saves tokens.
- On Windows the files are copied, so re-run `./setup` after updates (`/gstack-upgrade` does it).

GSD (get-shit-done) was skipped: it overlaps with Superpowers, so use one or the other.

## Installed: instructions, memory and permissions

**Global `~/.claude/CLAUDE.md`** (applies to every project):

- Use Serena for reading and editing code, and say so before falling back to plain file tools.
- Look up library docs with context7 instead of relying on memory.
- Make routine edits without asking; confirm destructive actions such as deletes and pushes.
- Small commits, commit only when asked, always ask before `git push`.
- Short answers, one step at a time, ending with next-step options.

**Project `CLAUDE.md`** (checked into the repo):

- Build, test, run and lint commands at the top.
- TDD: failing test first, then the minimal code to pass it.
- A "no speculative complexity" rule, so Claude doesn't add abstractions without a real need.
- Layering and naming rules for both backend and frontend.
- Test conventions: `Method_Scenario_ExpectedResult` names, Arrange/Act/Assert comments, no branching inside tests.
- A self-review checklist before each commit, plus the Conventional Commits format.

**Memory.** Claude keeps per-project notes in `~/.claude/projects/<path-slug>/memory/`, with a `MEMORY.md` index of one line per note. Seed it with preferences that aren't obvious from the code, and include the reason for each.

**Permissions** in `<project>/.claude/settings.local.json`, kept out of git:

- Allow only read-only git commands (`status`, `diff`, `log`, `show`, `branch`).
- Allow build and test commands: `dotnet restore/build/test/format`, `npm ci`, `npx ng build/test/lint`.
- Avoid broad wildcards such as `git:*`, `rm:*`, `curl:*` and `powershell:*`.

**`.gitignore`.** Start from `dotnet new gitignore`, then add `node_modules/`, `dist/`, `.angular/`, `coverage/`, `.claude/settings.local.json`, `.mcp.json`, `.serena/`, `.env` and `*.local.json`.

## Planned: still to install or configure

These are the remaining steps, in order.

1. Set the git identity: `git config --global user.name "<name>"` and `git config --global user.email "<personal email>"`.
2. Log in to GitHub: `gh auth login` (GitHub.com, HTTPS, browser login).
3. Trust the local HTTPS certificate for the API: `dotnet dev-certs https --trust`.
4. Optional: keep code local by turning off gstack's outside Codex reviews: `~/.claude/skills/gstack/bin/gstack-config set codex_reviews disabled`.
5. Restart `claude` so plugin skills load, then check `/mcp` and `/plugin`.
6. Optional: set up a status line with `/statusline`.
7. Write a first custom skill with `superpowers:writing-skills` or `/gstack-write-skill`.
8. Install the Angular CLI: `npm i -g @angular/cli`.
9. Make the first commit, brainstorm the app, then scaffold `src/Api`, `src/Api.Tests` and `src/web`.

**Daily workflow once the app starts:**

| Stage | Use |
| --- | --- |
| New feature idea | `superpowers:brainstorming`, then `writing-plans` |
| Implement | `test-driven-development`, `executing-plans` |
| Bug | `systematic-debugging` |
| Review | `/gstack-review` or `/code-review` |
| Test in the browser | `/gstack-qa` |
| Finish and open a PR | `verification-before-completion`, then `/gstack-ship` |

## Tips and what to avoid

- **Install MCP tools once and launch the installed command.** Starting a server with `uvx --from git+...` or `npx` re-downloads it on every start and can hit the roughly 60-second startup limit (`-32001 Request timed out`). `MCP_TIMEOUT` does not raise that limit.
- **Enable only the plugins you use.** Each one adds its skill descriptions to every session's context.
- **Keep tokens out of git.** Pass them through environment variables and never commit `.mcp.json` with literal values.
- **Use a personal account.** Don't log in with work credentials, and don't copy a work machine's `.mcp.json`, tokens, certificates, internal plugins or memories.
- **Ask before you let it edit.** Ask exploratory questions in plain language first, and use plan mode for multi-file changes.
- **Prune now and then.** Remove one-off permission entries and stale memory notes.
