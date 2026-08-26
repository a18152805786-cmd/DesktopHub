param(
    [Parameter(Mandatory = $true)]
    [string]$AssemblyDirectory,
    [Parameter(Mandatory = $true)]
    [string]$DesktopPath
)

$ErrorActionPreference = 'Stop'
$tempRoot = Join-Path $env:TEMP ('DesktopHub-phase5-reference-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($tempRoot) | Out-Null

try {
    $jsonAssembly = [Reflection.Assembly]::LoadFrom((Join-Path $AssemblyDirectory 'Newtonsoft.Json.dll'))
    $assembly = [Reflection.Assembly]::LoadFrom((Join-Path $AssemblyDirectory 'Desktop Frames.dll'))
    $flags = [Reflection.BindingFlags]::Public -bor [Reflection.BindingFlags]::Static
    $instanceFlags = [Reflection.BindingFlags]::Public -bor [Reflection.BindingFlags]::Instance

    $jObjectType = $jsonAssembly.GetType('Newtonsoft.Json.Linq.JObject', $true)
    $parse = $jObjectType.GetMethod('Parse', [type[]]@([string]))
    $referenceType = $assembly.GetType('Desktop_Frames.ReferenceGrouping.ReferenceFrameItem', $true)
    $referenceService = $assembly.GetType('Desktop_Frames.ReferenceGrouping.ReferenceFrameItemService', $true)
    $groupingService = $assembly.GetType('Desktop_Frames.ReferenceGrouping.ReferenceGroupingService', $true)
    $classifier = $assembly.GetType('Desktop_Frames.ReferenceGrouping.ReferenceGroupingClassifier', $true)
    $frameDataManager = $assembly.GetType('Desktop_Frames.FrameDataManager', $true)
    $frameProviderType = $assembly.GetType('Desktop_Frames.Search.Providers.FrameSearchProvider', $true)
    $searchServiceType = $assembly.GetType('Desktop_Frames.Search.SearchService', $true)

    $folderPath = Join-Path $tempRoot 'HUNNU-REFERENCE_TEST_FOLDER'
    [IO.Directory]::CreateDirectory($folderPath) | Out-Null
    $urlPath = Join-Path $tempRoot 'REFERENCE_TEST_Hades.url'
    [IO.File]::WriteAllText($urlPath, "[InternetShortcut]`r`nURL=steam://rungameid/1145360`r`n")
    $shortcutPath = Join-Path $tempRoot 'REFERENCE_TEST_Codex.lnk'
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = "$env:SystemRoot\System32\notepad.exe"
    $shortcut.Arguments = 'REFERENCE_TEST_ARGUMENT'
    $shortcut.WorkingDirectory = $tempRoot
    $shortcut.Save()

    $frameJson = '{"Id":"phase5-test-frame","Title":"UI_PHASE5_TEST","ItemsType":"Data","Items":[],"TabsEnabled":"false","CurrentTab":0,"Tabs":[]}'
    $frame = $parse.Invoke($null, [object[]]@($frameJson))
    $frames = New-Object 'System.Collections.Generic.List[object]'
    $frames.Add($frame)
    $frameDataManager.GetProperty('FrameData').SetValue($null, $frames)
    $framesPath = Join-Path $tempRoot 'frames.json'
    $frameDataManager.GetProperty('JsonFilePath').SetValue($null, $framesPath)

    $add = $referenceService.GetMethod('TryAddDroppedReference', $flags)
    $added = @()
    foreach ($path in @($shortcutPath, $urlPath, $folderPath)) {
        $arguments = [object[]]::new(4)
        $arguments[0] = $frame
        $arguments[1] = [string]$path
        $arguments[2] = $null
        $arguments[3] = [string]::Empty
        $ok = [bool]$add.Invoke($null, $arguments)
        $added += [pscustomobject]@{ Path = $path; Added = $ok; Reason = [string]$arguments[3]; Item = $arguments[2] }
    }

    $items = $frame['Items']
    $persisted = $parse.Invoke($null, [object[]]@('{"wrapper":' + [IO.File]::ReadAllText($framesPath) + '}'))
    $persistedCount = @($persisted['wrapper'][0]['Items']).Count

    $manual = $added[0].Item
    $manualArguments = [object[]]::new(2)
    $manualArguments[0] = $manual
    $manualArguments[1] = 'manual-test-frame'
    $referenceService.GetMethod('MarkManualOverride', $flags).Invoke($null, $manualArguments)
    $manualPreserved = $manual['ManualFrameId'].ToString() -eq 'manual-test-frame'

    $remove = $referenceService.GetMethod('RemoveMembership', $flags)
    $sourceExistsBeforeRemove = [IO.File]::Exists($shortcutPath)
    $removeArguments = [object[]]::new(2)
    $removeArguments[0] = $items
    $removeArguments[1] = $manual
    $removed = [bool]$remove.Invoke($null, $removeArguments)
    $sourceExistsAfterRemove = [IO.File]::Exists($shortcutPath)

    $create = $referenceType.GetMethod('Create', $flags)
    $classify = $classifier.GetMethod('Classify', $flags)
    $shortcutReference = $create.Invoke($null, [object[]]@([string]$shortcutPath, [string]::Empty))
    $urlReference = $create.Invoke($null, [object[]]@([string]$urlPath, [string]::Empty))
    $folderReference = $create.Invoke($null, [object[]]@([string]$folderPath, [string]::Empty))
    $oneArgument = [object[]]::new(1)
    $oneArgument[0] = $shortcutReference
    $shortcutDecision = $classify.Invoke($null, $oneArgument)
    $oneArgument[0] = $urlReference
    $urlDecision = $classify.Invoke($null, $oneArgument)
    $oneArgument[0] = $folderReference
    $folderDecision = $classify.Invoke($null, $oneArgument)

    $previewObject = $groupingService.GetMethod('BuildDesktopPreview', $flags).Invoke($null, [object[]]@($DesktopPath))
    $previewRows = foreach ($entry in $previewObject) {
        [pscustomobject]@{
            Name = $entry.Item.DisplayName
            OriginalPath = $entry.Item.OriginalPath
            ReferenceType = $entry.Item.ReferenceType.ToString()
            TargetPath = $entry.Item.TargetPath
            Arguments = $entry.Item.Arguments
            Uri = $entry.Item.Uri
            Group = $entry.Decision.Group.ToString()
            Confidence = $entry.Decision.Confidence.ToString()
            Reason = $entry.Decision.Reason
            Action = $entry.Action
        }
    }

    $applyBlocked = $false
    try {
        $applyArguments = [object[]]::new(3)
        $applyArguments[0] = $frames.PSObject.BaseObject
        $applyArguments[1] = $previewObject.PSObject.BaseObject
        $applyArguments[2] = $false
        $groupingService.GetMethod('ApplyHighConfidenceToFrames', $flags).Invoke($null, $applyArguments) | Out-Null
    } catch {
        $inner = $_.Exception.InnerException
        $applyBlocked = ($inner -is [InvalidOperationException]) -or
            $_.Exception.Message.Contains('Per-icon Desktop visual ownership is unavailable')
    }

    $frameDataManager.GetProperty('FrameData').SetValue($null, $frames)
    $provider = [Activator]::CreateInstance($frameProviderType, $true)
    $searchResults = @($frameProviderType.GetMethod('GetResults').Invoke($provider, @()))
    $referenceSearchResult = @($searchResults | Where-Object { $_.OriginalPath -eq $urlPath -or $_.OriginalPath -eq $folderPath })

    $desktopShortcut = Join-Path $DesktopPath 'Codex++.lnk'
    $dedupCount = -1
    if ([IO.File]::Exists($desktopShortcut)) {
        $desktopReference = $create.Invoke($null, [object[]]@([string]$desktopShortcut, 'phase5-test-frame'))
        $toJson = $referenceType.GetMethod('ToJson', $instanceFlags)
        $desktopJson = $toJson.Invoke($desktopReference, [object[]]@(99))
        $items.Add($desktopJson)
        $searchService = [Activator]::CreateInstance($searchServiceType, $true)
        $searchServiceType.GetMethod('Refresh').Invoke($searchService, @())
        $allSearchItems = @($searchServiceType.GetProperty('Items').GetValue($searchService))
        $dedupCount = @($allSearchItems | Where-Object {
            $_.OriginalPath -eq $desktopShortcut -or $_.FullPathOrUri -eq $desktopShortcut
        }).Count
    }

    [pscustomobject]@{
        HarnessRoot = $tempRoot
        ShortcutReferenceAdded = $added[0].Added
        UrlReferenceAdded = $added[1].Added
        FolderReferenceAdded = $added[2].Added
        PersistedItemCount = $persistedCount
        OriginalShortcutStillExists = [IO.File]::Exists($shortcutPath)
        OriginalUrlStillExists = [IO.File]::Exists($urlPath)
        OriginalFolderStillExists = [IO.Directory]::Exists($folderPath)
        CopyDirectoryCreated = [IO.Directory]::Exists((Join-Path $tempRoot 'Shortcuts'))
        RemoveMembershipSucceeded = $removed
        RemovePreservedOriginal = $sourceExistsBeforeRemove -and $sourceExistsAfterRemove
        ManualOverridePreserved = $manualPreserved
        ShortcutClassification = $shortcutDecision.Group.ToString() + '/' + $shortcutDecision.Confidence.ToString()
        UrlClassification = $urlDecision.Group.ToString() + '/' + $urlDecision.Confidence.ToString()
        FolderClassification = $folderDecision.Group.ToString() + '/' + $folderDecision.Confidence.ToString()
        ProductionPreviewCount = @($previewRows).Count
        ProductionPreview = @($previewRows)
        ProductionApplyBlockedWithoutVisualOwnership = $applyBlocked
        FrameSearchReferenceCount = $referenceSearchResult.Count
        SearchCanonicalDedupCount = $dedupCount
    } | ConvertTo-Json -Depth 8
}
catch {
    Write-Error ("HarnessFailure={0}; ScriptLine={1}" -f $_.Exception.Message, $_.InvocationInfo.ScriptLineNumber)
    throw
}
finally {
    if (Test-Path -LiteralPath $tempRoot) {
        $resolvedTempRoot = [IO.Path]::GetFullPath($tempRoot)
        $resolvedTempBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
        $leaf = [IO.Path]::GetFileName($resolvedTempRoot)
        if ($resolvedTempRoot.StartsWith($resolvedTempBase, [StringComparison]::OrdinalIgnoreCase) -and
            $leaf.StartsWith('DesktopHub-phase5-reference-', [StringComparison]::Ordinal)) {
            Remove-Item -LiteralPath $resolvedTempRoot -Recurse -Force
        }
    }
}
