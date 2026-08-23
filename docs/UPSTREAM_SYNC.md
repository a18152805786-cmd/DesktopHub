# Upstream synchronization policy

This repository keeps the public project as `upstream` and the private GitHub repository as `origin`.

```text
origin   = https://github.com/a18152805786-cmd/DesktopHub.git   (PRIVATE)
upstream = https://github.com/limbo666/DesktopFramesPlus.git
```

Do not replace `origin` with the public repository. Do not put tokens, cookies, or credentials in Git config, commits, logs, or documentation.

## Inspect upstream first

```bash
git fetch upstream --tags
git status
git branch -a -vv
git log --oneline -10
git log HEAD..upstream/main --oneline
git diff HEAD...upstream/main
```

If upstream changes its default branch, replace `main` with the branch reported by the remote and update this document after review.

Do not use this as an unattended overwrite operation:

```bash
# Do not use blindly:
git pull upstream main
```

## Recommended integration flow

1. Fetch only: `git fetch upstream --tags`.
2. Review commit history, changed files, dependency changes, registry/filesystem/network behavior, and release notes.
3. Create a temporary integration branch from the current private development line:

   ```bash
   git switch develop
   git switch -c integration/upstream-YYYYMMDD
   ```

4. Merge or rebase the reviewed upstream commit explicitly.
5. Re-run the full-framework Release build and the agreed safe smoke test.
6. Inspect the diff and working tree, then merge the integration branch into `develop`.
7. Promote only a verified, stable result into `main`.

## Branch roles

- `main`: stable private Desktop Hub line only.
- `develop`: future custom work and reviewed integrations.
- `upstream/main`: read-only remote-tracking view of the public source.
- `integration/upstream-*`: disposable review branch for one upstream update.

The current clone has a complete local `main` branch and `upstream/main`. The recommended baseline tag was intentionally not created because safe runtime validation is blocked by mandatory startup side effects.

## Baseline tag rule

After a future user-approved safe launch validates the startup behavior, create a tag from the exact verified commit:

```bash
git tag -a upstream-baseline-YYYYMMDD -m "DesktopFramesPlus upstream baseline YYYYMMDD"
git push origin main --tags
```

Never create a baseline tag merely because the project compiles.
