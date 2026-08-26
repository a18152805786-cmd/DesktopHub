# Startup Tips lifecycle audit (pre-Phase 2)

Audit date: 2026-08-26

## Current execution chain

```text
App startup
  -> ProfileManager.Initialize()
  -> SettingsManager.LoadSettings()
  -> Framemanager.LoadAndCreateFrames()
  -> FrameDataManager.Initialize()
  -> SettingsManager.LoadSettings() (reload-safe call)
  -> load Profiles/<active>/frames.json
  -> if missing/invalid/empty: InitializeDefaultFrame()
  -> otherwise: MigrateLegacyJson()
  -> CreateFrame() for each loaded frame
```

## Findings

```text
StartupTipsCreationMethod=Framemanager.InitializeDefaultFrame() creates a Data frame and a Note frame in one default JSON payload
StartupTipsCreationTrigger=frames.json is missing, cannot be parsed, or contains zero frames
StartupTipsCreatedWhen=Only during default profile/frame initialization; the Note frame is created alongside the default Data frame
StartupTipsFrameType=Note (ItemsType="Note")

StartupTipsTitleSource=Desktop_Frames.Localization.Strings.StartupTipsTitle (.resx via ResourceManager)
StartupTipsContentSource=Desktop_Frames.Localization.Strings.StartupTipsContent (.resx via ResourceManager)

StartupTipsPersisted=Yes, as an ordinary frame object in the active profile's frames.json; no dedicated identity or dismissal state exists
PersistenceFile=Profiles/<active-profile>/frames.json

StartupTipsCreatedOnlyForEmptyProfile=Yes for the existing default-initialization path; non-empty frames.json is loaded as-is
CreatedOnEveryLaunch=No, unless the file is empty/missing/invalid
CreatedAfterDelete=No under the current non-empty-profile path, but this is an incidental side effect rather than an explicit dismissed policy
CreatedAfterProfileSwitch=Only when the switched-to profile has missing/invalid/empty frames.json and default initialization runs
```

## Current implementation gap

The default Note frame has no stable built-in marker. Existing frames are loaded and rendered without a Startup Tips decision. The delete-frame menu removes a frame from `FrameDataManager.FrameData` and saves `frames.json`, but it does not persist that the user intentionally dismissed Startup Tips. This makes lifecycle behavior depend on the remaining frame list and cannot safely distinguish a built-in Tips Note from a user-created Note.

The Phase 2 implementation should add a small stable `SystemFrameId` marker to newly generated Tips, keep a global `StartupTipsDismissed` value in `ProfileOptions.json`, and use exact normalized title/content pairs for conservative legacy detection. Ordinary Notes and modified/ambiguous legacy Notes must remain untouched.

## Evidence locations

- `Code/Desktop Frames/FrameManager.cs`: `LoadAndCreateFrames()`, `InitializeDefaultFrame()`, delete-frame menu handler, and `MigrateLegacyJson()`.
- `Code/Desktop Frames/FrameDataManager.cs`: dynamic JSON load/save and backward-compatible property preservation.
- `Code/Desktop Frames/ProfileManager.cs`: application-level `ProfileOptions.json` load/save.
- `Code/Desktop Frames/Localization/Strings.resx`: English Startup Tips title/content.
- `Code/Desktop Frames/Localization/Strings.zh-Hans.resx`: Chinese Startup Tips title/content.
