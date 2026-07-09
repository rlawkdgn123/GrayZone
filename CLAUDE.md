# CLAUDE.md

## GrayZone Bootstrap (Claude Code)

This file is the short project-root bootstrap for Claude Code sessions in the GrayZone Unity project. It mirrors `AGENTS.md` (the Codex bootstrap). Keep detailed harness, log, and automation policy in the private document harness instead of expanding this file.

## Canonical Paths

- Unity project root: `C:\Users\user\GrayZone`
- Private document root: `C:\Users\user\GrayZone_privateDoc-main`
- Path registry: `C:\Users\user\GrayZone_privateDoc-main\Junghyun\AI-Agent\Local\Harness\PATH_REGISTRY.md`
- Claude harness: `C:\Users\user\GrayZone_privateDoc-main\Junghyun\AI-Agent\Local\Harness\Claude\CLAUDE.md`
- Claude worklog: `C:\Users\user\GrayZone_privateDoc-main\Junghyun\AI-Agent\Local\Log\Claude\CLAUDE_WORKLOG.md`
- Codex harness: `C:\Users\user\GrayZone_privateDoc-main\Junghyun\AI-Agent\Local\Harness\Codex\AGENTS.md`
- Unity-Cli harness: `C:\Users\user\GrayZone_privateDoc-main\Junghyun\AI-Agent\Local\Harness\Unity-Cli\CLI_SESSION_HARNESS.md`
- Automation harness: `C:\Users\user\GrayZone_privateDoc-main\Junghyun\AI-Agent\Local\Harness\Automation\AI_DAILY_SUMMARY_AUTOMATION.md`

## Startup Flow

- Answer the user in Korean by default unless they ask otherwise.
- Before relying on local harness, log, launcher, Obsidian, or SVN paths, read the path registry.
- For normal code edits, inspect only the repository files needed for the request.
- For GrayZone workflow details, read the Claude harness.
- For every GrayZone-related Claude turn, question, audit, planning-only task, or code change, follow the Claude harness per-turn worklog rule before the final answer, appending to the Claude worklog.
- For Unity Editor, scene, prefab, serialized reference, compile, console, or Play Mode validation, read the Unity-Cli harness and prefer Unity-facing tools.
- For Claude session summaries, daily summaries, transcript recovery, or AI daily summary output, read the Automation harness.

## Unity Tools

Prefer `unity-scanner` before manually reading large Unity YAML files.

Use `unity-cli` when Unity Editor truth is needed:

```powershell
unity-cli --project C:\Users\user\GrayZone status
unity-cli --project C:\Users\user\GrayZone editor refresh --compile
unity-cli --project C:\Users\user\GrayZone console --type error,warning --stacktrace user
```

If `unity-cli` or `unity-scanner` is not found in PowerShell, temporarily add:

```powershell
$env:Path = "$env:LOCALAPPDATA\unity-cli;$env:LOCALAPPDATA\unity-scanner;$env:Path"
```

If `unity-cli` reports `no Unity instances found`, report that Unity Editor validation is unavailable and continue with static checks.

## Guardrails

- Do not edit generated Unity folders unless explicitly asked: `Library/`, `Temp/`, `obj/`, `Logs/`, `UserSettings/`.
- Prefer project edits under `Assets/1.Scripts/`, `Assets/0.Scenes/`, `Assets/2.Prefabs/`, `Packages/manifest.json`, and `ProjectSettings/`.
- Do not edit `Assets/3.Resources` or `Assets/4.ThirdParty` unless the task specifically requires it.
- Do not read, scan, summarize, or update `Deprecated` folders unless the user explicitly asks for them.
