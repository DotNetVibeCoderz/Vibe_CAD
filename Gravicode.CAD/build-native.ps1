# Builds the Rust engine and copies the native library into Gravicode.CAD.Native/runtimes/<rid>/native.
# Requires cargo (https://rustup.rs). Made by Gravicode Studios, led by Kang Fadhil.
param([string]$Target = "")
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
Push-Location "$root/native/gravicode-cad-engine"
try {
    $args = @("build", "--release")
    if ($Target) { $args += @("--target", $Target) }
    cargo @args
    cargo test --release
    $outDir = if ($Target) { "target/$Target/release" } else { "target/release" }
    $rid, $file = if ($IsMacOS) { "osx-$([Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString().ToLower())", "libgravicode_cad_engine.dylib" }
                  elseif ($IsLinux) { "linux-$([Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString().ToLower())", "libgravicode_cad_engine.so" }
                  else { "win-x64", "gravicode_cad_engine.dll" }
    $dest = "$root/src/Gravicode.CAD.Native/runtimes/$rid/native"
    New-Item -ItemType Directory -Force $dest | Out-Null
    Copy-Item "$outDir/$file" $dest -Force
    Write-Host "Copied $file -> $dest"
}
finally { Pop-Location }
