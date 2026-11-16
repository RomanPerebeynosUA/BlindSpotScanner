# Claude Code on a personal laptop (generic, no company info)

Everything here is public tooling and general tips. No employer names, URLs, tokens, repos, tickets, tenants or internal paths.
Commands are for Windows/PowerShell; adapt paths for macOS/Linux. Verify package names and plugin names against their GitHub pages before installing, since they change.

## 1. Install

```powershell
winget install Git.Git GitHub.cli OpenJS.NodeJS.LTS astral-sh.uv Docker.DockerDesktop
npm i -g @anthropic-ai/claude-code
claude            # log in with your personal Anthropic account
gh auth login
git config --global user.name  "<your name>"
git config --global user.email "<your personal email>"
```

Use a personal account and personal GitHub. Do not log in with work credentials on this machine.

## 2. User settings (`~/.claude/settings.json`)

```json
{
  "env": { "MCP_TIMEOUT": "120000" },
  "alwaysThinkingEnabled": true,
  "effortLevel": "medium"
}
```

- `effortLevel`: raise to `high` for hard design or debugging work, keep `medium` day to day.
- Do not set `skipDangerousModePermissionPrompt` unless you understand it skips safety prompts.

## 3. MCP servers

Project-level `.mcp.json` (put it in `.gitignore` if it contains tokens, or reference env vars instead of literal values):

```json
{
  "mcpServers": {
    "context7": { "command": "context7-mcp", "args": [] },
    "serena": {
      "command": "serena",
      "args": ["start-mcp-server", "--enable-web-dashboard", "false", "--project", "<project-name>"]
    },
    "github": {
      "command": "docker",
      "args": ["run", "-i", "--rm", "-e", "GITHUB_PERSONAL_ACCESS_TOKEN", "ghcr.io/github/github-mcp-server"],
      "env": { "GITHUB_PERSONAL_ACCESS_TOKEN": "<your fine-grained PAT>" }
    }
  }
}
```

| Server | What it gives you | Install |
|---|---|---|
| **context7** | Up-to-date library docs on demand | `npm i -g @upstash/context7-mcp` |
| **Serena** | Symbol-aware code navigation and edits via language servers | `uv tool install git+https://github.com/oraios/serena` |
| **GitHub** | PRs, issues, file contents | Docker image above; use a fine-grained PAT limited to the repos you need |
| **Atlassian** (optional) | Jira/Confluence | `uv tool install mcp-atlassian` |
| **SonarQube** (optional) | Quality gate and issues | `mcp/sonarqube` Docker image |

MCP tips that cost time to learn:
- **Install tools once, launch the shim.** Never start a server with `uvx --from git+...`; it re-fetches on every start and can exceed the ~60s initialize limit. Use `uv tool install ...` and call the installed command.
- **`-32001 Request timed out`** means the server did not answer `initialize` in about 60s. `MCP_TIMEOUT` does not raise that cap. Fix the slow start instead: pre-install, pre-pull Docker images, disable dashboards and update checks.
- Docker-based servers have the slowest cold starts; prefer native installs where an equivalent exists.
- Start Serena with `--enable-web-dashboard false` to skip the extra thread and version check.
- Check `/mcp` after each change. Logs are under `%LOCALAPPDATA%\claude-cli-nodejs\Cache\<project-slug>\`.
- Pass tokens through env vars and rotate them. Never commit `.mcp.json` with literal tokens.

## 4. Plugins and skills ("superpowers" etc.)

Inside `claude`:

```
/plugin marketplace add obra/superpowers-marketplace
/plugin install superpowers@superpowers-marketplace
/plugin install superpowers-developing-for-claude-code@superpowers-marketplace
/plugin marketplace add anthropics/claude-plugins-official
/plugin install postman@claude-plugins-official      # only if you work with APIs
```

- `superpowers`: structured skills for brainstorming, planning, TDD, debugging and code review. If the exact plugin name differs, run `/plugin` and browse the marketplace.
- `superpowers-developing-for-claude-code`: docs and workflows for writing your own plugins, hooks and skills.
- Enable only the plugins you use; each adds tool and skill descriptions to every session's context.

Optional workflow framework: **GSD (get-shit-done)** adds phase-based planning/execute/verify skills, agents, hooks and a statusline. It installs into `~/.claude`. Look up its current install command on its GitHub page. Skip it if you prefer plain sessions.

## 5. Persistent memory

Claude keeps memory files per project in `~/.claude/projects/<path-slug>/memory/` with a `MEMORY.md` index (one line per entry). Seed it with working preferences that are not obvious from code, for example:

- "Use Serena tools for all code reading and editing; tell me if Serena is unavailable before falling back." (include the reason, so edge cases are judged correctly)
- "Do not ask permission for routine file edits in this repo."
- "Prefer small focused commits; ask before pushing."

Save things that are surprising or non-obvious. Do not store code structure, secrets, or anything the repo already documents.

## 6. `CLAUDE.md` tips

Put project rules in a repo-root `CLAUDE.md` (checked in). What worked well:

- Build/test/run commands at the top.
- Test conventions: naming pattern, AAA comments, no branching in tests, one behavior per test.
- A TDD rule: failing test first, then minimal code.
- A self-review checklist before commit (re-read the diff, grep for stale names, run tests).
- Layering and naming rules, and a "no speculative complexity" rule so Claude does not add locks or abstractions without a real need.
- Commit message format and branch naming.
- Rules for handling automated review comments: verify before applying, reply on the thread only for false positives.
- Keep rules general. Do not name tickets, PRs or one-off cases in them.

## 7. Permissions hygiene (`.claude/settings.local.json`)

- Allow narrow, read-only commands first: `git status`, `git diff`, `git log`, build and test commands.
- Be wary of broad wildcards such as `Bash(rm:*)`, `Bash(curl:*)`, `Bash(git:*)`, `Bash(powershell:*)`. They approve far more than you probably intend.
- The `/fewer-permission-prompts` skill can propose an allowlist from your transcripts; review it before accepting.
- Prune one-off entries (paths to temp files, long inline scripts) now and then.
- Keep `settings.local.json` out of git.

## 8. Everyday habits

- Ask exploratory questions in plain language first; only say "go" when you want edits.
- Use plan mode for anything multi-file.
- Run long builds and tests in the background and keep working.
- Use subagents/forks for broad searches so raw output stays out of the main context.
- Commit only when you ask; review `git status` after any broad `git add`.
- Update or delete stale memories instead of letting them pile up.

## 9. Do not bring over from a work machine

Work `.mcp.json`, tokens, certificates and proxy env vars, internal marketplaces and plugins, repo-specific skills, settings allowlists containing internal paths, session history, and any memory files that mention employers, tickets or tenants. Rebuild only the generic parts above.
