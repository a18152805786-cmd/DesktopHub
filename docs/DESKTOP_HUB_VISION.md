# Desktop Hub vision

This file records a route only. It does not authorize feature development in the baseline phase.

## Phase 0 — upstream baseline

- Preserve the upstream history and license.
- Keep the original product identity during baseline verification.
- Establish private `origin` and public `upstream` remotes.
- Audit filesystem, system integration, network behavior, build requirements, and configuration storage.
- Freeze only after a safe runtime test is agreed and completed.

## Phase 1 — project entry management

Organize references to areas such as:

```text
论文研究
PPT
Agents
游戏
工具
```

## Phase 2 — project workspace

A project may describe references to folders and applications:

- folders, Word, PowerPoint, Excel, PDF, Stata;
- Obsidian, Zotero, browser profiles;
- Codex, Kimi, VS Code, and other agent tools.

## Phase 3 — workspace launch

A project card may launch a reviewed set of applications and paths, for example a research project directory together with Word, Stata, Obsidian, Zotero, browser, and an agent tool. Launch actions must be explicit, inspectable, and reversible.

## Phase 4 — agent integration

A future local interface may let an agent create project entries, add references, add shortcuts, or update project status. The default permission must remain:

> Agents may reference and launch; they may not delete or move real user files.

## Architecture principle

> Reference, not relocation.

The system should primarily manage paths, shortcuts, URIs, project metadata, and workspace configuration. Original files remain at their original locations, for example:

```text
C:\xxx\paper.docx
D:\xxx\data.dta
E:\xxx\project\
```

Configuration should be backup-friendly, exportable, human-readable, and transparent. JSON is the first preference; SQLite is acceptable only with a reliable JSON export.

## Boundary for this baseline

No UI redesign, branding change, AI/LLM/MCP/RAG feature, database, cloud sync, account system, automatic file organization, Agent Control, dependency modernization, WPF-to-WinUI migration, Electron/Flutter rewrite, or upstream feature deletion belongs in Phase 0.
