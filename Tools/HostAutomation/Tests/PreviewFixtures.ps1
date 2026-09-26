# Pure PNG/filesystem fixtures; the production Issue binding is exercised only
# as a comparison parameter. No live GitHub or game project is accessed.
function Initialize-HostPreviewFixture {
    if ('Sashimi.Tests.PngFixture' -as [type]) { return }
    Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.IO.Compression;
using System.Buffers.Binary;
using System.Text;
namespace Sashimi.Tests {
    public static class PngFixture {
        static void Chunk(Stream output,string name,byte[] data) {
            byte[] length=new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(length,(uint)data.Length); output.Write(length);
            byte[] type=Encoding.ASCII.GetBytes(name); output.Write(type); output.Write(data);
            uint crc=0xffffffff;
            foreach(byte[] bytes in new[]{type,data}) foreach(byte b in bytes) { crc^=b; for(int k=0;k<8;k++) crc=(crc&1)==0 ? crc>>1 : (crc>>1)^0xedb88320; }
            BinaryPrimitives.WriteUInt32BigEndian(length,crc^0xffffffff); output.Write(length);
        }
        static int Paeth(int a,int b,int c) {
            int p=a+b-c; return Math.Abs(p-a)<=Math.Abs(p-b) && Math.Abs(p-a)<=Math.Abs(p-c) ? a : Math.Abs(p-b)<=Math.Abs(p-c) ? b : c;
        }
        public static byte[] Encode(int width,int height,int channels,byte[] pixels,int filter=0,string text=null,bool junk=false,int reportedWidth=0,int reportedHeight=0,int depth=8,int interlace=0) {
            using var output=new MemoryStream(); output.Write(new byte[]{137,80,78,71,13,10,26,10});
            byte[] header=new byte[13]; BinaryPrimitives.WriteUInt32BigEndian(header,(uint)(reportedWidth==0 ? width : reportedWidth)); BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4),(uint)(reportedHeight==0 ? height : reportedHeight));
            header[8]=(byte)depth; header[9]=(byte)(channels==3 ? 2 : 6); header[12]=(byte)interlace; Chunk(output,"IHDR",header);
            if(text!=null) Chunk(output,"tEXt",Encoding.ASCII.GetBytes("test\0"+text));
            using var compressed=new MemoryStream();
            using(var zlib=new ZLibStream(compressed,CompressionLevel.Optimal,true)) {
                int stride=width*channels;
                for(int y=0;y<height;y++) {
                    zlib.WriteByte((byte)filter);
                    for(int x=0;x<stride;x++) {
                        int i=y*stride+x, a=x>=channels ? pixels[i-channels] : 0, b=y>0 ? pixels[i-stride] : 0, c=y>0 && x>=channels ? pixels[i-stride-channels] : 0;
                        int predictor=filter==0 ? 0 : filter==1 ? a : filter==2 ? b : filter==3 ? (a+b)/2 : Paeth(a,b,c);
                        zlib.WriteByte(unchecked((byte)(pixels[i]-predictor)));
                    }
                }
            }
            if(junk) compressed.WriteByte(42);
            Chunk(output,"IDAT",compressed.ToArray()); Chunk(output,"IEND",Array.Empty<byte>()); return output.ToArray();
        }
    }
}
'@
}

function New-HostPreviewImage {
    param([int]$Width=100,[int]$Height=10,[int]$Channels=3,[int]$ChangedPixels=0,[int]$Delta=1,[int]$AlphaDelta=0,[int]$Filter=0,[AllowNull()][string]$Metadata)
    Initialize-HostPreviewFixture
    $pixels=[byte[]]::new($Width*$Height*$Channels)
    [Array]::Fill[byte]($pixels,100)
    for ($i=0;$i -lt $ChangedPixels;$i++) { for ($c=0;$c -lt 3;$c++) { $pixels[$i*$Channels+$c]=100+$Delta } }
    if ($Channels -eq 4) { $pixels[3]=100+$AlphaDelta }
    return ,[Sashimi.Tests.PngFixture]::Encode($Width,$Height,$Channels,$pixels,$Filter,$Metadata)
}

function New-HostPreviewState {
    param([byte[]]$Bytes,[string]$Path='Assets/_SashimiBoy/Art/Generated/Previews/Stage01/SalmonAssembly_Initial.png')
    $captures=[Collections.Generic.Dictionary[string,byte[]]]::new([StringComparer]::Ordinal)
    $captures.Add($Path,$Bytes)
    return [pscustomobject]@{
        Manifest=@([pscustomobject]@{Path=$Path;Kind='File';Length=$Bytes.Length;Sha256=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($Bytes)).ToLowerInvariant()})
        Captures=$captures
    }
}

function Compare-HostPreviewStates {
    param($Left,$Right,[int]$IssueNumber=20)
    Compare-SashimiGeneratedManifest -Before $Left.Manifest -After $Right.Manifest -BeforeCaptures $Left.Captures -AfterCaptures $Right.Captures -IssueNumber $IssueNumber
}

Invoke-HostTestCase 'PreviewRgbAndPixelFractionBoundariesAreExact' {
    $base=New-HostPreviewState (New-HostPreviewImage)
    foreach ($case in @(@{Pixels=1;Delta=1;Expected=$true},@{Pixels=2;Delta=1;Expected=$false},@{Pixels=1;Delta=2;Expected=$false})) {
        $other=New-HostPreviewState (New-HostPreviewImage -ChangedPixels $case.Pixels -Delta $case.Delta)
        $comparison=Compare-HostPreviewStates $base $other
        Assert-HostTest ($comparison.Passed -eq $case.Expected) 'RGB/pixel threshold did not enforce the exact limit.'
        Assert-HostTest ($comparison.Previews[0].Metrics.ChangedPixels -eq $case.Pixels) 'Changed channels were counted as separate pixels.'
    }
    $a=New-HostPreviewState (New-HostPreviewImage -Width 999 -Height 1)
    $b=New-HostPreviewState (New-HostPreviewImage -Width 999 -Height 1 -ChangedPixels 1)
    Assert-HostTest (-not (Compare-HostPreviewStates $a $b).Passed) 'Fraction rounding admitted more than 0.1 percent.'
    foreach ($filter in 0..4) {
        $other=New-HostPreviewState (New-HostPreviewImage -ChangedPixels 1 -Filter $filter)
        Assert-HostTest ((Compare-HostPreviewStates $base $other).Passed) "Valid PNG filter $filter did not decode to the original sample values."
    }
}

Invoke-HostTestCase 'PreviewAlphaDimensionsAndMetadataRemainExact' {
    $base=New-HostPreviewState (New-HostPreviewImage -Channels 4)
    $alpha=New-HostPreviewState (New-HostPreviewImage -Channels 4 -AlphaDelta 1)
    Assert-HostTest (-not (Compare-HostPreviewStates $base $alpha).Passed) 'Alpha drift was tolerated.'
    $rgb=New-HostPreviewState (New-HostPreviewImage -Channels 4 -ChangedPixels 1)
    Assert-HostTest ((Compare-HostPreviewStates $base $rgb).Passed) 'RGBA RGB samples were not compared independently from exact alpha.'
    $base=New-HostPreviewState (New-HostPreviewImage)
    foreach ($other in @(
        (New-HostPreviewState (New-HostPreviewImage -Width 1000 -Height 1)),
        (New-HostPreviewState (New-HostPreviewImage -Metadata 'changed')),
        (New-HostPreviewState (New-HostPreviewImage -Channels 4)))) {
        Assert-HostTest (-not (Compare-HostPreviewStates $base $other).Passed) 'A dimension/metadata/pixel format change was tolerated.'
    }
}

Invoke-HostTestCase 'PreviewPolicyIsBoundToExactIssuePathsAndManifestShape' {
    $a=New-HostPreviewState (New-HostPreviewImage)
    $b=New-HostPreviewState (New-HostPreviewImage -ChangedPixels 1)
    Assert-HostTest (-not (Compare-HostPreviewStates $a $b 5281).Passed) 'Another Issue inherited preview tolerance.'
    foreach ($path in @('Assets/Other.png',($a.Manifest[0].Path+'.meta'),$a.Manifest[0].Path.ToLowerInvariant())) {
        $left=New-HostPreviewState (New-HostPreviewImage) $path
        $right=New-HostPreviewState (New-HostPreviewImage -ChangedPixels 1) $path
        Assert-HostTest (-not (Compare-HostPreviewStates $left $right).Passed) 'An unapproved exact path inherited tolerance.'
    }
    Assert-HostTest (-not (Compare-SashimiGeneratedManifest -Before $a.Manifest -After @() -IssueNumber 20).Passed) 'Preview deletion was tolerated.'
    Assert-HostTest (-not (Compare-SashimiGeneratedManifest -Before @() -After $a.Manifest -IssueNumber 20).Passed) 'Preview addition was tolerated.'
    $b.Manifest[0].Kind='Directory'
    Assert-HostTest (-not (Compare-HostPreviewStates $a $b).Passed) 'File-kind substitution was tolerated.'
}

Invoke-HostTestCase 'PreviewOriginalBytesMustMatchEvidenceAndQuotas' {
    $a=New-HostPreviewState (New-HostPreviewImage)
    $b=New-HostPreviewState (New-HostPreviewImage -ChangedPixels 1)
    $project=Join-Path $script:temporaryRoot 'preview-originals'
    $path=Join-Path $project $a.Manifest[0].Path
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path))
    [IO.File]::WriteAllBytes($path,$a.Captures[$a.Manifest[0].Path])
    $capture=Get-SashimiPreviewCaptures -ProjectRoot $project -Snapshot $a.Manifest -IssueNumber 20
    Assert-HostTest ($capture.Count -eq 1) 'Original preview was not captured.'
    [IO.File]::WriteAllBytes($path,$b.Captures[$b.Manifest[0].Path])
    Assert-HostThrows { Get-SashimiPreviewCaptures -ProjectRoot $project -Snapshot $a.Manifest -IssueNumber 20 } 'manifest'
    $b.Captures[$b.Manifest[0].Path]=$a.Captures[$a.Manifest[0].Path]
    Assert-HostTest (-not (Compare-HostPreviewStates $a $b).Passed) 'Replaced capture bytes were accepted against stale hashes.'
    $lock=[IO.File]::Open($path,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
    try { Assert-HostThrows { Get-SashimiPreviewCaptures -ProjectRoot $project -Snapshot $b.Manifest -IssueNumber 20 } 'process|used|access|액세스|사용' } finally { $lock.Dispose() }
    $oversized=$a.Manifest[0].PSObject.Copy(); $oversized.Length=25MB+1
    $stream=[IO.File]::OpenWrite($path); try { $stream.SetLength(25MB+1) } finally { $stream.Dispose() }
    Assert-HostThrows { Get-SashimiPreviewCaptures -ProjectRoot $project -Snapshot @($oversized) -IssueNumber 20 } 'quota'
}

Invoke-HostTestCase 'PreviewMalformedUnsupportedAndCompressedTailAreRejected' {
    Initialize-HostPreviewFixture
    $a=New-HostPreviewState (New-HostPreviewImage)
    $pixels=[byte[]]::new(3000); [Array]::Fill[byte]($pixels,100); $pixels[0]=101
    $crc=[byte[]](New-HostPreviewImage -ChangedPixels 1); $crc[29]=$crc[29] -bxor 1
    $truncated=[byte[]](New-HostPreviewImage -ChangedPixels 1); $truncated=$truncated[0..($truncated.Length-2)]
    foreach ($bytes in @(
        $crc,$truncated,
        [Sashimi.Tests.PngFixture]::Encode(100,10,3,$pixels,0,$null,$true),
        [Sashimi.Tests.PngFixture]::Encode(100,10,3,$pixels,0,$null,$false,16384,16384),
        [Sashimi.Tests.PngFixture]::Encode(100,10,3,$pixels,0,$null,$false,0,0,16),
        [Sashimi.Tests.PngFixture]::Encode(100,10,3,$pixels,0,$null,$false,0,0,8,1))) {
        Assert-HostTest (-not (Compare-HostPreviewStates $a (New-HostPreviewState $bytes)).Passed) 'An invalid, oversized, or unsupported PNG entered the tolerated result.'
    }
}

Invoke-HostTestCase 'PreviewComparisonIsPairwiseAndPreservesAsymmetricDeltas' {
    $a=New-HostPreviewState (New-HostPreviewImage)
    $b=New-HostPreviewState (New-HostPreviewImage -ChangedPixels 1)
    $c=New-HostPreviewState (New-HostPreviewImage -ChangedPixels 1 -Delta 2)
    Assert-HostTest ((Compare-HostPreviewStates $a $b).Passed -and (Compare-HostPreviewStates $b $c).Passed -and -not (Compare-HostPreviewStates $a $c).Passed) 'The nontransitive 100/101/102 counterexample was not rejected pairwise.'
    $comparisonParameters=@{
        Run1=[pscustomobject]@{Output=$b.Manifest;Source=$b.Manifest;Captures=$b.Captures}
        Run2=[pscustomobject]@{Output=$c.Manifest;Source=$c.Manifest;Captures=$c.Captures}
        Committed=[pscustomobject]@{Output=$a.Manifest;Captures=$a.Captures}
        IssueNumber=20; CompareSource=$true; CompareCommitted=$true
    }
    $reproducibility=Compare-SashimiGeneratorReproducibility @comparisonParameters
    Assert-HostTest (-not $reproducibility.Passed -and -not $reproducibility.CommittedPassed -and $reproducibility.Comparisons.Count -eq 4) 'Production orchestration skipped a required committed comparison.'
    Assert-HostTest ($reproducibility.Comparisons[0].Result.Passed -and -not $reproducibility.Comparisons[3].Result.Passed) 'The required committed-to-second-run comparison did not detect accumulated drift.'
    $validatorPath=Join-Path $hostRoot 'Invoke-SashimiUnityValidation.ps1'
    Set-Item Function:Get-SashimiGeneratorDelta (Get-HostTestFunctionScriptBlock $validatorPath 'Get-SashimiGeneratorDelta')
    $delta1=@(Get-SashimiGeneratorDelta -Before $a.Manifest -After $a.Manifest -AllowedPaths @($a.Manifest[0].Path))
    $delta2=@(Get-SashimiGeneratorDelta -Before $a.Manifest -After $b.Manifest -AllowedPaths @($a.Manifest[0].Path))
    Assert-HostTest ($delta1.Count -eq 0 -and $delta2.Count -eq 1 -and (Compare-HostPreviewStates $a $b).Passed) 'A one-run-only tiny delta was normalized away or wrongly rejected.'
    $comparisonParameters.Run1=[pscustomobject]@{Output=$a.Manifest;Source=$a.Manifest;Captures=$a.Captures}
    $comparisonParameters.Run2=[pscustomobject]@{Output=$b.Manifest;Source=$b.Manifest;Captures=$b.Captures}
    Assert-HostTest ((Compare-SashimiGeneratorReproducibility @comparisonParameters).Passed) 'Production orchestration rejected valid asymmetric delta membership.'
    $comparisonParameters.Run2.Source+= [pscustomobject]@{Path='Untested.asset';Kind='File';Length=1;Sha256='changed'}
    Assert-HostTest (-not (Compare-SashimiGeneratorReproducibility @comparisonParameters).Passed) 'Output comparison concealed a different complete source manifest.'
    Assert-HostThrows { Get-SashimiGeneratorDelta -Before $a.Manifest -After $b.Manifest -AllowedPaths @('Unrelated.asset') } 'undeclared'
    Assert-HostThrows { Assert-SashimiGeneratorManifestUnchanged -Before $a.Manifest -After $b.Manifest } 'after Run1'
    $extra=@($a.Manifest)+@([pscustomobject]@{Path='Assets/Editor/Existing.cs';Kind='File';Length=1;Sha256='changed'})
    Assert-HostThrows { Assert-SashimiGeneratorManifestUnchanged -Before $a.Manifest -After $extra } 'after Run1'
}

Invoke-HostTestCase 'PreviewReviewerBoundaryChecksContentStatusAndEvidence' {
    $reviewerPath=Join-Path $hostRoot 'Invoke-SashimiReviewerRun.ps1'
    Set-Item Function:Assert-ReviewerGitSnapshotUnchanged (Get-HostTestFunctionScriptBlock $reviewerPath 'Assert-ReviewerGitSnapshotUnchanged')
    $DryRun=$false
    function Get-ReviewerGitSnapshot { param($PreviewIssueNumber) return $reviewAfter }
    function New-PreviewReviewerSnapshot($state,$status) {
        $value=[ordered]@{Status=$status;Manifest=$state.Manifest;PreviewCaptures=$state.Captures;VisibleContentSha256=$state.Manifest[0].Sha256;WithoutSettingsSha256=$state.Manifest[0].Sha256;VisibleFileCount=1}
        foreach ($field in @('Head','Ref','Refs','Origin','PushOrigin','Hooks','LocalConfig','IndexFlags','ControlFiles')) { $value[$field]='unchanged' }
        [pscustomobject]$value
    }
    $a=New-HostPreviewState (New-HostPreviewImage)
    $b=New-HostPreviewState (New-HostPreviewImage -ChangedPixels 1)
    $path=$a.Manifest[0].Path
    $before=New-PreviewReviewerSnapshot $a ''
    $reviewAfter=New-PreviewReviewerSnapshot $b " M $path`n"
    $validation=[pscustomobject]@{
        Determinism=[pscustomobject]@{Required=$true;Passed=$true;Run1Snapshot=$b.Manifest;Comparisons=@('Run1-Run2 outputs','Run1-Run2 complete source','Committed-Run1','Committed-Run2' | ForEach-Object { [pscustomobject]@{Pair=$_;Result=[pscustomobject]@{Passed=$true}} })}
        Stages=[pscustomobject]@{GeneratorRun1=[pscustomobject]@{Success=$true;Planned=$false};GeneratorRun2=[pscustomobject]@{Success=$true;Planned=$false}}
    }
    $good=Assert-ReviewerGitSnapshotUnchanged -Before $before -Boundary 'Unity validation' -IssueNumber 20 -ValidationResult $validation
    Assert-HostTest $good.Passed 'Reviewer rejected the exact approved preview-only Unity drift.'
    Assert-HostTest ((ConvertTo-SashimiJson $good).Length -lt 4096) 'Comparison evidence unexpectedly contains private PNG captures.'
    foreach ($case in @('channel-two','staged','extra-status','git-control','other-file','codex','wrong-issue','missing-evidence','planned','missing-pair','late-edit','late-restore')) {
        $reviewAfter=New-PreviewReviewerSnapshot $b " M $path`n"
        $evidence=$validation | ConvertTo-Json -Depth 20 | ConvertFrom-Json
        $boundary='Unity validation'; $number=20
        switch ($case) {
            channel-two { $bad=New-HostPreviewState (New-HostPreviewImage -ChangedPixels 1 -Delta 2); $reviewAfter=New-PreviewReviewerSnapshot $bad " M $path`n"; $evidence.Determinism.Run1Snapshot=$bad.Manifest }
            staged { $reviewAfter.Status="M  $path" }
            extra-status { $reviewAfter.Status+=" M Extra.cs`n" }
            git-control { $reviewAfter.ControlFiles='changed' }
            other-file { $reviewAfter.Manifest+= [pscustomobject]@{Path='Extra.cs';Kind='File';Length=1;Sha256='changed'} }
            codex { $boundary='Codex' }
            wrong-issue { $number=5281 }
            missing-evidence { $evidence.Determinism.Passed=$false }
            planned { $evidence.Stages.GeneratorRun2.Planned=$true }
            missing-pair { $evidence.Determinism.Comparisons=@($evidence.Determinism.Comparisons | Where-Object Pair -cne 'Committed-Run2') }
            late-edit { $evidence.Determinism.Run1Snapshot=$a.Manifest }
            late-restore { $reviewAfter=New-PreviewReviewerSnapshot $a '' }
        }
        Assert-HostThrows { Assert-ReviewerGitSnapshotUnchanged -Before $before -Boundary $boundary -IssueNumber $number -ValidationResult $evidence } 'Reviewer|preview'
    }
}

Invoke-HostTestCase 'PreviewCaptureRefusesReparseAncestors' {
    $state=New-HostPreviewState (New-HostPreviewImage)
    $root=Join-Path $script:temporaryRoot 'preview-reparse'
    $target=Join-Path $script:temporaryRoot 'preview-reparse-target'
    [void][IO.Directory]::CreateDirectory($root)
    $path=Join-Path $target $state.Manifest[0].Path.Substring('Assets/'.Length)
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path))
    [IO.File]::WriteAllBytes($path,$state.Captures[$state.Manifest[0].Path])
    $link=Join-Path $root 'Assets'
    [void](New-Item -ItemType Junction -Path $link -Target $target)
    try { Assert-HostThrows { Get-SashimiPreviewCaptures -ProjectRoot $root -Snapshot $state.Manifest -IssueNumber 20 } 'reparse' }
    finally { [IO.Directory]::Delete($link) }
}

Invoke-HostTestCase 'PreviewUnityOrchestrationPinsOriginalAndRejectsLateWrites' {
    $validatorPath=Join-Path $hostRoot 'Invoke-SashimiUnityValidation.ps1'
    foreach ($name in @('ConvertTo-SashimiProjectRelativePath','Get-SashimiDeterminismSnapshot','Get-SashimiGeneratorSourceManifest','New-SashimiGeneratorBaseline','Get-SashimiGeneratorDelta')) {
        Set-Item ("Function:$name") (Get-HostTestFunctionScriptBlock $validatorPath $name)
    }
    # Execute the actual production stage orchestration against real temp PNGs
    # and manifests; replace only Unity/Git processes and artifact publication.
    $tokens=$null; $parseErrors=$null
    $ast=[Management.Automation.Language.Parser]::ParseFile($validatorPath,[ref]$tokens,[ref]$parseErrors)
    $blocks=@($ast.FindAll({param($node) $node -is [Management.Automation.Language.IfStatementAst] -and $node.Clauses[0].Item1.Extent.Text -ceq '$preUnityScopePassed'},$true))
    Assert-HostTest ($parseErrors.Count -eq 0 -and $blocks.Count -eq 1) 'Cannot locate the production Unity stage orchestration.'
    $pipeline=[scriptblock]::Create($blocks[0].Extent.Text)
    function Assert-SashimiValidationNotCancelled {}
    function Get-SashimiUnityGitControlSnapshot { return [pscustomobject]@{Control='unchanged'} }
    function Add-SashimiValidationCheck {}
    function Add-SashimiValidationFailure { param($Code) $recordedFailures.Add($Code) }
    function Write-SashimiBoundedUnityTextArtifact {}
    function Read-SashimiComponentInventory { return [pscustomobject]@{Passed=$true} }
    function Invoke-SashimiUnityValidationStage {
        param($Name,$ProjectRoot)
        $destination=Join-Path $ProjectRoot $previewPath
        if ($Name -ceq 'CompileImport' -and $scenario -ceq 'import-drift') { [IO.File]::WriteAllBytes($destination,$one) }
        if ($Name -ceq 'GeneratorRun1') { [IO.File]::WriteAllBytes($destination,$one) }
        if ($Name -ceq 'GeneratorRun2') {
            [IO.File]::WriteAllBytes($destination,$(if ($scenario -ceq 'import-drift') {$two} else {$one}))
            if ($scenario -ceq 'cross-workspace-write') { [IO.File]::WriteAllText((Join-Path $normalizedProjectPath 'Existing.cs'),'changed by Run2') }
        }
        if ($Name -ceq 'PlayMode' -and $scenario -ceq 'late-preview') { [IO.File]::WriteAllBytes($destination,$two) }
        if ($Name -ceq 'PlayMode' -and $scenario -ceq 'late-restore') { [IO.File]::WriteAllBytes($destination,$zero) }
        return [pscustomobject]@{Success=$true;Planned=$false}
    }
    $zero=New-HostPreviewImage; $one=New-HostPreviewImage -ChangedPixels 1; $two=New-HostPreviewImage -ChangedPixels 1 -Delta 2
    $previewPath='Assets/_SashimiBoy/Art/Generated/Previews/Stage01/SalmonAssembly_Initial.png'
    foreach ($scenario in @('valid','import-drift','cross-workspace-write','late-preview','late-restore')) {
        $caseRoot=Join-Path $script:temporaryRoot "preview-pipeline-$scenario"
        $normalizedProjectPath=Join-Path $caseRoot 'Repository'
        $normalizedArtifactsPath=Join-Path $caseRoot 'Artifacts'
        $script:unityArtifactStateRoot=Join-Path $caseRoot 'State'
        [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName((Join-Path $normalizedProjectPath $previewPath)))
        [IO.File]::WriteAllBytes((Join-Path $normalizedProjectPath $previewPath),$zero)
        [IO.File]::WriteAllText((Join-Path $normalizedProjectPath 'Existing.cs'),'original')
        $preUnityScopePassed=$true; $fixture=$null; $validationDefinition=[pscustomobject]@{Id='pure-local-preview'}
        $ReviewRunId='pure-local-review'; $IssueNumber=20
        $determinismPaths=@($previewPath); $screenshotPaths=@(); $previewPaths=@($previewPath)
        $unityExecutable='mock-unity'; $unityTimeout=1; $generatorTimeout=1
        foreach ($argumentName in @('compileArguments','generatorRun1Arguments','generatorRun2Arguments','inventoryArguments','editArguments','playArguments')) { Set-Variable $argumentName @() }
        foreach ($logName in @('compileLog','compileRawLog','generatorRun1Log','generatorRun1RawLog','generatorRun2Log','generatorRun2RawLog','inventoryLog','inventoryRawLog','editLog','editRawLog','editXml','editRawXml','playLog','playRawLog','playXml','playRawXml')) { Set-Variable $logName 'unused-process-fixture' }
        $stages=[ordered]@{CompileImport=$null;GeneratorRun1=$null;GeneratorRun2=$null;ComponentInventory=$null;EditMode=$null;PlayMode=$null}
        $result=[ordered]@{Determinism=[ordered]@{Run1Snapshot=@();Run2Snapshot=@();Passed=$null;Comparisons=@()};ComponentInventory=$null}
        $recordedFailures=[Collections.Generic.List[string]]::new()
        if ($scenario -ceq 'cross-workspace-write') { Assert-HostThrows { & $pipeline } 'after Run1' }
        elseif ($scenario -in @('late-preview','late-restore')) { Assert-HostThrows { & $pipeline } 'manifest' }
        else {
            & $pipeline
            Assert-HostTest ($result.Determinism.Passed -eq ($scenario -ceq 'valid')) "Production stage orchestration returned an incorrect result for $scenario."
            if ($scenario -ceq 'import-drift') {
                Assert-HostTest ($recordedFailures -ccontains 'GeneratorDeliverableMismatch') 'Import-time drift lost the original committed comparison.'
                Assert-HostTest ($result.Determinism.Comparisons[0].Result.Passed -and -not $result.Determinism.Comparisons[3].Result.Passed) 'Import-time counterexample did not exercise the committed-to-Run2 gate.'
            }
        }
    }
}
