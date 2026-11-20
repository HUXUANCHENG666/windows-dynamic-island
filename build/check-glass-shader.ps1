$ErrorActionPreference = 'Stop'
# Compile exactly the embedded source used by D3DCompiler at runtime. No GPU needed.
$source = Get-Content src/DynamicIsland/Theme/GlassGpuRenderer.cs -Raw
$match = [regex]::Match($source, '(?s)const string ShaderSource = """\r?\n(.*?)\r?\n""";')
if (-not $match.Success) { throw 'Embedded glass HLSL was not found.' }
$fxc = Get-ChildItem "${env:ProgramFiles(x86)}/Windows Kits/10/bin/*/x64/fxc.exe" |
    Sort-Object FullName -Descending | Select-Object -First 1
if (-not $fxc) { throw 'Windows SDK fxc.exe is required for shader validation.' }
$temp = Join-Path ([IO.Path]::GetTempPath()) ([guid]::NewGuid().ToString())
New-Item $temp -ItemType Directory | Out-Null
try {
    $hlsl = Join-Path $temp GlassGpu.hlsl
    [IO.File]::WriteAllText($hlsl, $match.Groups[1].Value)
    foreach ($profile in @('vs_5_0', 'ps_5_0', 'vs_4_0', 'ps_4_0')) {
        $entry = if ($profile.StartsWith('vs')) { 'VSMain' } else { 'PSMain' }
        & $fxc.FullName /nologo /WX /Ges /O3 /T $profile /E $entry /Fo "$temp/$profile.cso" $hlsl
        if ($LASTEXITCODE -ne 0) { throw "Glass HLSL compilation failed: $profile" }
        Write-Host "Glass HLSL compiled with warnings as errors: $entry $profile"
    }
} finally { Remove-Item $temp -Recurse -Force }
