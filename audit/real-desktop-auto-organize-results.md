# Real Desktop Auto Organize Controlled Test

本记录在清理本轮测试工件之前保存。测试只使用本轮创建的 `TEST_*`、`CONTROL_*` 和 `_TEST_*` 项。

```text
TestDate=2026-08-26
Branch=develop
SourceCommit=6dd7d3b
BuildConfiguration=Release
BuildSucceeded=true
AutoOrganizeDefaultEnabled=false
TestRulePrefix=TEST_
TestRuleConflictAction=Skip
AutoCreateFrame=false
UACPromptDetected=false
```

## Classification results

| Source | Expected destination | Observed result | Latency observation |
|---|---|---|---|
| `TEST_document.docx` | `_TEST_文档` | moved to expected target | within 15 seconds |
| `TEST_slides.pptx` | `_TEST_PPT` | moved to expected target | within 15 seconds |
| `TEST_article.pdf` | `_TEST_PDF` | moved to expected target | within 15 seconds |
| `TEST_data.xlsx`, `TEST_data.csv`, `TEST_data.dta` | `_TEST_数据` | all moved to expected target | within 15 seconds |
| `TEST_picture.png`, `TEST_picture.jpg` | `_TEST_图片` | both moved to expected target | within 15 seconds |
| `TEST_archive.zip`, `TEST_archive.rar` | `_TEST_压缩包` | both moved to expected target | within 15 seconds |
| `TEST_installer.exe` | `_TEST_安装包` | moved to expected target; never executed | within 15 seconds |
| `TEST_unknown.xyz` | unchanged | remained on Desktop | skipped: no matching rule |
| `CONTROL_document.docx`, `CONTROL_picture.png` | unchanged | both remained on Desktop | prefix guard |
| `TEST_download.pdf.crdownload`, `TEST_archive.zip.part`, `TEST_temp.tmp`, `~$TEST_document.docx` | unchanged | all remained on Desktop | temporary/lock-file skip |
| `TEST_collision.docx` | `_TEST_文档` | source and pre-existing destination both preserved | collision skip |
| `TEST_active.csv` | `_TEST_数据` | remained on Desktop with complete 16-line content | bounded stability window expired; safe skip |
| `TEST_active2.csv` | `_TEST_数据` | moved after writing ended; complete 8-line content | deferred until stable |

The batch performance sample (`TEST_peak_document.docx`, `TEST_peak_slides.pptx`, `TEST_peak_article.pdf`, `TEST_peak_data.csv`, `TEST_peak_picture.png`, `TEST_peak_archive.zip`, `TEST_peak_installer.exe`) moved 7/7 to their expected targets.

## Safety results before cleanup

```text
NormalClassificationPassed=true
ControlFilesUntouched=true
OverwriteOccurred=false
CollisionBehavior=Skip; source and original destination preserved
TemporaryFilesSkipped=true
ActiveFileHandling=bounded stability checks: 2 seconds x maximum 5 checks; fail closed
MovedWhileWriting=false
WriteInterrupted=false
Deferred=true (TEST_active2.csv)
MovedAfterStable=true (TEST_active2.csv)

NonTestFilesMoved=false
NonTestFilesDeleted=false
NonTestFilesRenamed=false
NonTestFilesModified=false
NonTestFilesCreated=42 (external path: Desktop\\Temp\\Claude-Code-Desktop-Switcher-GUI; not touched by this test)

AutoOrganizeDisabledAfterTest=true (options restored; test process stopped)
GracefulTrayExit=NotAvailable (tray window was not exposed to Computer Use)
TestArtifactsCleaned=true

PosttestSnapshot=audit/real-desktop-posttest.csv
PosttestComparisonScope=all Desktop files outside C:\\Users\\71966\\Desktop\\DesktopHub
PosttestObservation=No external removal/move/rename/modify; 42 unrelated external additions were detected and preserved.
```

## Performance observation

CPU is normalized by logical processor count and is an approximation from `Get-Process.TotalProcessorTime`.

```text
StableStartWorkingSetMB=166.53
StableEndWorkingSetMB=166.47
StableStartPrivateMemoryMB=78.57
StableEndPrivateMemoryMB=78.35
StableCPUPercentApprox=0.006

IdleStartWorkingSetMB=168.59
IdleEndWorkingSetMB=168.45
IdleStartPrivateMemoryMB=79.21
IdleEndPrivateMemoryMB=79.68
IdleCPUPercentApprox=0.023

PeakWorkingSetMB=170.36
PeakPrivateMemoryMB=81.07
PeakCPUPercentApprox=0.093
PossibleMemoryLeak=false (no continuously rising trend in the sampled interval)
```
