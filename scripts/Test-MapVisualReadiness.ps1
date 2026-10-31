param([Parameter(Mandatory=$true)][string]$CandidateDirectory)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
if ($PSVersionTable.PSEdition -ne 'Desktop') { throw 'Use Windows PowerShell 5.1 for net472 checks.' }
$game='C:/Program Files (x86)/Steam/steamapps/common/Mount & Blade II Bannerlord'
$script:readinessDependencyRoots=@(
    $CandidateDirectory,
    (Join-Path $game 'bin/Win64_Shipping_Client'),
    (Join-Path $game 'Modules/Native/bin/Win64_Shipping_Client'),
    (Join-Path $game 'Modules/SandBox/bin/Win64_Shipping_Client'),
    (Join-Path $game 'Modules/Multiplayer/bin/Win64_Shipping_Client')
)
$resolver=[ResolveEventHandler]{param($sender,$request)
    $name=([Reflection.AssemblyName]$request.Name).Name+'.dll'
    foreach($root in $script:readinessDependencyRoots) {
        $path=Join-Path $root $name
        if(Test-Path -LiteralPath $path) { return [Reflection.Assembly]::LoadFrom($path) }
    }
    return $null
}
[AppDomain]::CurrentDomain.add_AssemblyResolve($resolver)
function Read-MethodCalls([Reflection.MethodBase]$method) {
    $opcodes=@{}
    foreach($field in [Reflection.Emit.OpCodes].GetFields([Reflection.BindingFlags]'Public,Static')) {
        $opcode=$field.GetValue($null)
        $opcodes[([int]$opcode.Value -band 65535)]=$opcode
    }
    $bytes=$method.GetMethodBody().GetILAsByteArray()
    $position=0
    while($position -lt $bytes.Length) {
        $code=[int]$bytes[$position++]; if($code -eq 254){$code=65024 -bor [int]$bytes[$position++]}
        if(-not $opcodes.ContainsKey($code)){throw 'Unknown IL opcode'}
        $opcode=$opcodes[$code]
        $size=switch($opcode.OperandType.ToString()) {
            'InlineNone' {0}
            'ShortInlineBrTarget' {1}
            'ShortInlineI' {1}
            'ShortInlineVar' {1}
            'InlineVar' {2}
            'InlineI8' {8}
            'InlineR' {8}
            'InlineSwitch' {4+4*[BitConverter]::ToInt32($bytes,$position)}
            default {4}
        }
        if($size -lt 0 -or $position+$size -gt $bytes.Length){throw 'Invalid IL operand boundary'}
        if($opcode.OperandType.ToString() -eq 'InlineMethod') {
            $called=$method.Module.ResolveMethod([BitConverter]::ToInt32($bytes,$position))
            [pscustomobject]@{Type=$called.DeclaringType.FullName;Name=$called.Name;Offset=$position-1}
        }
        $position += $size
    }
}
try {
    $assembly=[Reflection.Assembly]::LoadFrom((Join-Path $CandidateDirectory 'CoopSpectator.dll'))
    $viewType=$assembly.GetType('CoopSpectator.UI.CoopCampaignMapPrototypeMissionView',$true)
    $flags=[Reflection.BindingFlags]'NonPublic,Instance'
    $probe=$viewType.GetMethod('UpdateRenderReadiness',$flags)
    $ready=$viewType.GetField('_mapSceneReadyToRender',$flags)
    # No constructor, scene, camera, network or renderer is initialized. This
    # executes the real adapter's null-layer return that used to retain true.
    $view=[Runtime.Serialization.FormatterServices]::GetUninitializedObject($viewType)
    $ready.SetValue($view,$true)
    $null=$probe.Invoke($view,[object[]]@([single]0.016))
    if($ready.GetValue($view)) { throw 'Real view retained historical readiness after scene loss.' }
    $viewType.GetField('_renderStateProbeFailed',$flags).SetValue($view,$true)
    $ready.SetValue($view,$true)
    $null=$probe.Invoke($view,[object[]]@([single]0))
    if($ready.GetValue($view)) { throw 'Failed probing retained readiness.' }
    $select=$assembly.GetType('CoopSpectator.Infrastructure.CoopCampaignMapVisualTickPolicy',$true).
        GetMethod('Select',[Reflection.BindingFlags]'NonPublic,Static')
    for($mask=0;$mask -lt 32;$mask++) {
        $argsForPolicy=[object[]]@((($mask -band 1) -ne 0),(($mask -band 2) -ne 0),
            (($mask -band 4) -ne 0),(($mask -band 8) -ne 0),(($mask -band 16) -ne 0))
        $result=$select.Invoke($null,$argsForPolicy).ToString()
        if(($result -eq 'UpdateVisuals') -ne ($mask -eq 15)) { throw ('Unsafe compiled admission: '+$mask) }
    }
    foreach($methodName in @('HasActiveReplicaView','OnMissionScreenTick')) {
        $method=$viewType.GetMethod($methodName,[Reflection.BindingFlags]'Public,NonPublic,Instance')
        $viewCalls=@(Read-MethodCalls $method)
        if(@($viewCalls | Where-Object {$_.Name -eq 'get_IsFocusLost'}).Count -ne 0) {
            throw ('OS focus must not veto background rendering: '+$methodName)
        }
    }
    'PASS real compiled readiness revocation, all 32 admission combinations, and no OS-focus rendering veto; no native scene constructed. Native background rendering and crash-free lifetime remain unproven.'
} finally {
    [AppDomain]::CurrentDomain.remove_AssemblyResolve($resolver)
}
