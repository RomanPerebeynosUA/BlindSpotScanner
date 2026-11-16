# Claude Code personal setup — install & do checklist

Windows / PowerShell. Status reflects this laptop as of 2026-11-13.
`✅` = done here, `⬜` = still to do.

---

## 1. Base tools

| | Tool | Install |
|---|---|---|
| ✅ | Git | `winget install Git.Git` |
| ✅ | GitHub CLI | `winget install GitHub.cli` |
| ✅ | Node.js LTS | `winget install OpenJS.NodeJS.LTS` |
| ✅ | uv (Python tools) | `winget install astral-sh.uv` |
| ✅ | .NET 10 SDK | `winget install Microsoft.DotNet.SDK.10` |
| ✅ | Bun (needed by gstack) | `winget install Oven-sh.Bun` |
| ✅ | Claude Code | `npm i -g @anthropic-ai/claude-code`, then `claude` and log in with a personal account |
| ⬜ | Angular CLI | `npm i -g @angular/cli` (when starting the project) |
| — | Docker | Skipped: not needed (no Docker-based MCPs) |

## 2. Accounts and identity — ⬜ TODO

```powershell
git config --global user.name  "<your name>"
git config --global user.email "<your personal email>"
gh auth login          # GitHub.com → HTTPS → login with browser
dotnet dev-certs https --trust   # trust local HTTPS cert for the API
```

## 3. User settings — ✅

`~/.claude/settings.json`:
```json
{
  "env": { "MCP_TIMEOUT": "120000" },
  "alwaysThinkingEnabled": true,
  "effortLevel": "medium",
  "theme": "dark"
}
```

## 4. MCP servers (lean: only these two) — ✅

```powershell
# Serena: symbol-aware code navigation/editing
uv tool install git+https://github.com/oraios/serena
claude mcp add serena --scope user -- serena start-mcp-server --context claude-code --project-from-cwd --enable-web-dashboard false

# context7: up-to-date library docs
npm i -g @upstash/context7-mcp
claude mcp add context7 --scope user -- context7-mcp.cmd

claude mcp list        # both should show ✓ Connected
```

Tip: install once and launch the shim; never `uvx --from git+...` / `npx` on every start (slow → `-32001` timeouts).

## 5. Plugins & skill frameworks — ✅

**Superpowers** (process: brainstorm → plan → TDD → debug → review):
```powershell
claude plugin marketplace add obra/superpowers-marketplace
claude plugin install superpowers@superpowers-marketplace --scope user
claude plugin install superpowers-developing-for-claude-code@superpowers-marketplace --scope user
```

**gstack** (roles: review, QA, ship, design, browser). Run in Git Bash with `bun` on PATH:
```bash
git clone --single-branch --depth 1 https://github.com/garrytan/gstack.git ~/.claude/skills/gstack
cd ~/.claude/skills/gstack && ./setup --host claude --prefix --no-skill-routing
```
- `--prefix` → commands are `/gstack-review`, `/gstack-qa`, `/gstack-ship`… (no clashes with Superpowers/built-ins)
- `--no-skill-routing` → no per-prompt routing hook (saves tokens, avoids fighting Superpowers)
- Windows copies files: re-run `./setup` after updates (`/gstack-upgrade` does it)
- `/gstack-cso` needs Visual Studio 2022 C++ Build Tools (optional)

**GSD**: skipped. It overlaps with Superpowers. Only use it as a *replacement*, not alongside.

⬜ Optional: keep code local by turning off gstack's Codex outside reviews:
```bash
~/.claude/skills/gstack/bin/gstack-config set codex_reviews disabled
```

## 6. Instructions & memory — ✅

- `~/.claude/CLAUDE.md` (all projects): Serena for code, context7 for docs, edit without asking, confirm destructive actions, small commits, ask before push, short step-by-step replies.
- `<project>/CLAUDE.md`: stack, commands, TDD, layering, test conventions, self-review checklist, commit format.
- Project memory in `~/.claude/projects/<path-slug>/memory/` with a `MEMORY.md` index.

## 7. Permissions — ✅

`<project>/.claude/settings.local.json` (keep out of git): allow only read-only git (`status`, `diff`, `log`, `show`, `branch`) plus `dotnet restore/build/test/format`, `npm ci`, `npx ng build/test/lint`. Avoid broad wildcards (`git:*`, `rm:*`, `curl:*`, `powershell:*`).

## 8. Project repo — ✅ started

```powershell
git init -b main
dotnet new gitignore
# append: node_modules/ dist/ .angular/ coverage/ .claude/settings.local.json .mcp.json .serena/ .env *.local.json
```

---

## Remaining TODO (in order)

1. ⬜ Git identity + `gh auth login` (section 2)
2. ⬜ Optional: disable gstack Codex reviews (section 5)
3. ⬜ Restart `claude` so new hooks/plugin skills load; check `/mcp` and `/plugin`
4. ⬜ Optional: status line (`/statusline`)
5. ⬜ Write your first skill (`superpowers:writing-skills` or `/gstack-write-skill`)
6. ⬜ Start the app: first commit → `superpowers:brainstorming` → `npm i -g @angular/cli` → scaffold `src/Api`, `src/Api.Tests`, `src/web`

## Daily workflow

| Stage | Use |
|---|---|
| New feature idea | `superpowers:brainstorming` → `writing-plans` |
| Implement | `test-driven-development`, `executing-plans` |
| Bug | `systematic-debugging` |
| Review | `/gstack-review` (or `/code-review`) |
| Test in browser | `/gstack-qa` |
| Finish & PR | `verification-before-completion` → `/gstack-ship` |

## Never bring over from a work machine

Work `.mcp.json`, tokens, certificates, proxy env vars, internal marketplaces/plugins, repo-specific skills, allowlists with internal paths, session history, memories mentioning employers/tickets/tenants.
