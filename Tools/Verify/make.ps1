# Generates Verify.csproj: compiles every script under Assets\Scripts against the
# exact references Unity uses (read from Unity's own Assembly-CSharp-Editor.csproj
# plus the built package DLLs in Library\ScriptAssemblies) — a fast offline
# compile check that works whether or not the Unity editor is open.
# Re-run after adding packages or upgrading Unity.  Then run check.sh.
$proj = (Resolve-Path "$PSScriptRoot\..\..").Path
$src  = Get-Content "$proj\Assembly-CSharp-Editor.csproj" -Raw
$defs = [regex]::Match($src, '<DefineConstants>[^<]*</DefineConstants>').Value
$hints = [regex]::Matches($src, '<HintPath>([^<]*)</HintPath>') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique
$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine('<Project Sdk="Microsoft.NET.Sdk">')
[void]$sb.AppendLine('  <PropertyGroup><TargetFramework>netstandard2.1</TargetFramework><LangVersion>9.0</LangVersion><EnableDefaultItems>false</EnableDefaultItems><NoWarn>0169;0414;0649;0162;0219;CS8632;CS0618;CS0108;CS0114</NoWarn><OutputType>Library</OutputType><AssemblyName>VerifyAll</AssemblyName><GenerateAssemblyInfo>false</GenerateAssemblyInfo><NoStdLib>true</NoStdLib><DisableImplicitFrameworkReferences>true</DisableImplicitFrameworkReferences></PropertyGroup>')
[void]$sb.AppendLine("  <PropertyGroup>$defs</PropertyGroup>")
[void]$sb.AppendLine('  <ItemGroup>')
[void]$sb.AppendLine("    <Compile Include=""$proj\Assets\Scripts\**\*.cs"" />")
foreach ($h in $hints) {
  $n = [System.IO.Path]::GetFileNameWithoutExtension($h)
  [void]$sb.AppendLine("    <Reference Include=""$n""><HintPath>$h</HintPath><Private>false</Private></Reference>")
}
Get-ChildItem "$proj\Library\ScriptAssemblies\*.dll" | Where-Object { $_.BaseName -notlike 'Assembly-CSharp*' } | ForEach-Object {
  [void]$sb.AppendLine("    <Reference Include=""$($_.BaseName)""><HintPath>$($_.FullName)</HintPath><Private>false</Private></Reference>")
}
[void]$sb.AppendLine('  </ItemGroup>')
[void]$sb.AppendLine('</Project>')
Set-Content -Path "$PSScriptRoot\Verify.csproj" -Value $sb.ToString() -Encoding utf8
Write-Host "Wrote $PSScriptRoot\Verify.csproj"
