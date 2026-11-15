# Checks the product sources for things the app must never do (CLAUDE.md "絶対に破ってはいけない制約").
#   pwsh tools/check-forbidden.ps1      -> exit code 0 when clean, 1 when something was found
# Scope: src/ only (tests may use temp folders, consoles etc.).
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$src = Join-Path $root "src"

$rules = @(
    @{ Name = "temp files";             Pattern = 'GetTempPath|GetTempFileName|%TEMP%|\bTEMP\b|\bTMP\b' },
    @{ Name = "AppData / user folders"; Pattern = 'SpecialFolder|ApplicationData|LocalApplicationData|AppData|IsolatedStorage' },
    @{ Name = "registry";               Pattern = 'Microsoft\.Win32\.Registry|RegistryKey|Registry\.(CurrentUser|LocalMachine)' },
    @{ Name = "network";                Pattern = 'System\.Net\b|HttpClient|WebClient|WebRequest|TcpClient|UdpClient|\bSocket\b|Dns\.|https?://(?!schemas\.(microsoft|openxmlformats)\.com)' },
    @{ Name = "logging / console";      Pattern = 'Debug\.Write|Trace\.Write|Console\.Write|EventLog|File\.AppendAll|ILogger' },
    @{ Name = "clipboard";              Pattern = '\bClipboard\.' },
    @{ Name = "external processes";     Pattern = 'Process\.Start' },
    @{ Name = "path in exception text"; Pattern = 'throw new [\w.]+\([^;]*\b(path|fileName|FullName|DllPath)\b' }
)

$found = 0
$files = Get-ChildItem $src -Recurse -Include *.cs, *.xaml | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }
foreach ($rule in $rules) {
    $hits = $files | Select-String -Pattern $rule.Pattern -CaseSensitive
    foreach ($h in $hits) {
        $rel = $h.Path.Substring($root.Length + 1)
        Write-Output ("[{0}] {1}:{2}: {3}" -f $rule.Name, $rel, $h.LineNumber, $h.Line.Trim())
        $found++
    }
}

# Third-party dependencies: only PDFium in the product projects.
$pkgs = Get-ChildItem $src -Recurse -Filter *.csproj | Select-String -Pattern '<PackageReference Include="([^"]+)"' -AllMatches
foreach ($p in $pkgs) {
    foreach ($m in $p.Matches) {
        $name = $m.Groups[1].Value
        if ($name -ne "bblanchon.PDFium.Win32") { Write-Output "[dependency] $($p.Path): $name"; $found++ }
    }
}
$ver = Get-ChildItem $src -Recurse -Filter *.csproj | Select-String -Pattern 'bblanchon\.PDFium\.Win32" Version="([^"]+)"'
foreach ($v in $ver) { if ($v.Matches[0].Groups[1].Value -ne "159.0.8152") { Write-Output "[dependency] PDFium version changed: $($v.Line.Trim())"; $found++ } }

if ($found -eq 0) { Write-Output "check-forbidden: OK (no findings)"; exit 0 }
Write-Output "check-forbidden: $found finding(s)"
exit 1
