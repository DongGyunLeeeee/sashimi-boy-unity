function Invoke-HostReviewerDriftRegression {
    $validationPath=Join-Path $hostRoot 'Invoke-SashimiUnityValidation.ps1'
    Set-Item Function:Get-HostReviewDriftContext (Get-HostTestFunctionScriptBlock $validationPath 'Get-HostReviewDriftContext')
    Set-Item Function:Get-HostReviewDriftAssessment (Get-HostTestFunctionScriptBlock $validationPath 'Get-HostReviewDriftAssessment')
    function Invoke-SashimiValidationProcess {
        param($Name,$Kind,$FilePath,$Arguments,$WorkingDirectory,$TimeoutSeconds,$Fixture,$FixtureGroup)
        $output=switch ($Name) {
            ReviewDriftStatus { $driftState.Status }
            ReviewDriftMetadata { $driftState.Metadata }
            ProtectedWorktreeStatus { $driftState.ProtectedStatus }
            default { throw "Unexpected drift fixture boundary: $Name" }
        }
        [pscustomobject]@{ Succeeded=$true; StdOut=$output; StdErr=''; ExitCode=0 }
    }
    $runRoot=Join-Path $script:temporaryRoot 'review-drift-owned'
    $run=New-SashimiRunWorkspace -RunRoot $runRoot
    $config=[pscustomobject]@{RunRoot=$runRoot}
    $normalizedProjectPath=$run.RepositoryPath
    $normalizedArtifactsPath=$run.ArtifactsPath
    $ReviewRunId=$run.RunId
    $result=[pscustomobject]@{DetectedUnityVersion='6000.4.0f1'}
    $gitExecutable='fixture-git'; $gitTimeout=10; $fixture=$null
    $ProtectedWorktrees=@(1..3 | ForEach-Object {
        $path=Join-Path $script:temporaryRoot "review-drift-protected-$_"
        [IO.Directory]::CreateDirectory($path) | Out-Null
        $path
    })
    $settingsPath=Join-Path $normalizedProjectPath 'ProjectSettings/ProjectSettings.asset'
    $original="PlayerSettings:`n  targetPixelDensity: 0`n  buildNumber: {}`n  iOSTargetOSVersionString: `n  tvOSTargetOSVersionString: `n  VisionOSTargetOSVersionString: `n  macOSTargetOSVersion: `n  productName: Fixture`n"
    $expected="PlayerSettings:`n  targetPixelDensity: 30`n  buildNumber:`n    Standalone: 0`n    VisionOS: 0`n    iPhone: 0`n    tvOS: 0`n  iOSTargetOSVersionString: 15.0`n  tvOSTargetOSVersionString: 15.0`n  VisionOSTargetOSVersionString: 1.0`n  macOSTargetOSVersion: 12.0`n  productName: Fixture`n"
    $driftState=@{Status=''; Metadata=''; ProtectedStatus=''}
    Write-SashimiUtf8File $settingsPath $original
    $before=Get-HostReviewDriftContext
    Write-SashimiUtf8File $settingsPath $expected
    $driftState.Status=' M ProjectSettings/ProjectSettings.asset'
    $allowed=Get-HostReviewDriftAssessment $before
    Assert-HostTest $allowed.Allowed 'Exact defaults in an owned Reviewer run were rejected.'
    Assert-HostTest ($allowed.WorkingFileSha256 -ceq (Get-FileHash $settingsPath -Algorithm SHA256).Hash.ToLowerInvariant()) 'Drift evidence is not bound to actual file bytes.'
    foreach ($case in @('extra-field','wrong-value','deleted-field','duplicate-key','newline','extra-file','staged','mode-change','marker-change','dirty-protected','wrong-run','wrong-root','wrong-version')) {
        Write-SashimiUtf8File $settingsPath $expected
        $driftState.Status=' M ProjectSettings/ProjectSettings.asset'; $driftState.Metadata=''; $driftState.ProtectedStatus=''
        $ReviewRunId=$run.RunId; $config.RunRoot=$runRoot; $result.DetectedUnityVersion='6000.4.0f1'
        $marker=[IO.File]::ReadAllText($run.MarkerPath)
        switch ($case) {
            extra-field { Write-SashimiUtf8File $settingsPath ($expected + "  extra: 1`n") }
            wrong-value { Write-SashimiUtf8File $settingsPath ($expected.Replace('productName: Fixture','productName: Changed')) }
            deleted-field { Write-SashimiUtf8File $settingsPath ($expected.Replace("  productName: Fixture`n",'')) }
            duplicate-key { Write-SashimiUtf8File $settingsPath ($expected + "  targetPixelDensity: 30`n") }
            newline { Write-SashimiUtf8File $settingsPath $expected.TrimEnd("`n") }
            extra-file { $driftState.Status += "`n?? Extra.cs" }
            staged { $driftState.Status='M  ProjectSettings/ProjectSettings.asset' }
            mode-change { $driftState.Metadata=' mode change 100644 => 100755 ProjectSettings/ProjectSettings.asset' }
            marker-change { Write-SashimiUtf8File $run.MarkerPath ($marker + ' ') }
            dirty-protected { $driftState.ProtectedStatus=' M User.cs' }
            wrong-run { $ReviewRunId='20260917T000000Z-' + ('9' * 32) }
            wrong-root { $config.RunRoot=Join-Path $script:temporaryRoot 'wrong-root' }
            wrong-version { $result.DetectedUnityVersion='6000.3.0f1' }
        }
        $refused=$false
        try { $refused=-not (Get-HostReviewDriftAssessment $before).Allowed } catch { $refused=$true }
        finally { Write-SashimiUtf8File $run.MarkerPath $marker }
        Assert-HostTest $refused "Reviewer drift gate accepted $case."
    }
}
