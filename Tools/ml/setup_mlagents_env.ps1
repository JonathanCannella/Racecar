# Create a Python virtual environment with the ML-Agents trainer (mlagents 1.1.0) for this project (Windows).
# mlagents 1.1.0 requires Python 3.10.1 - 3.10.12 (64-bit).
#
# Usage: Tools\ml\setup_mlagents_env.cmd            (or: powershell -ExecutionPolicy Bypass -File Tools\ml\setup_mlagents_env.ps1)
#   $env:PYTHON = "C:\path\to\python.exe"   use a specific Python 3.10 interpreter
#   $env:VENV   = "C:\path\to\venv"         venv location (default: <project>\.venv-mlagents)
#
# If you don't have Python 3.10: install 3.10.11 (the last 3.10 Windows installer) from python.org,
# or with conda: conda create -n mlagents python=3.10.12, activate it, and set $env:PYTHON = "python",
# or with uv: uv python install 3.10.12, then $env:PYTHON = (uv python find 3.10.12).
$ErrorActionPreference = "Stop"

$Project = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$Venv = if ($env:VENV) { $env:VENV } else { Join-Path $Project ".venv-mlagents" }

function Invoke-Checked($exe, [string[]]$arguments) {
    & $exe @arguments
    if ($LASTEXITCODE -ne 0) { throw "Failed: $exe $($arguments -join ' ')" }
}

# Find Python 3.10: $env:PYTHON, the py launcher, or python on PATH
$Py = $null; $PyArgs = @()
if ($env:PYTHON) { $Py = $env:PYTHON }
elseif (Get-Command py -ErrorAction SilentlyContinue) { $Py = "py"; $PyArgs = @("-3.10") }
else {
    # Skip the Microsoft Store alias (WindowsApps\python.exe): it only offers to install Python
    $found = Get-Command python -All -ErrorAction SilentlyContinue | Where-Object { $_.Source -notlike "*\WindowsApps\*" } | Select-Object -First 1
    if ($found) { $Py = $found.Source }
}
if (-not $Py) { throw "Python 3.10 not found. Install Python 3.10.11 (python.org, the last 3.10 Windows installer) or set `$env:PYTHON." }

# No double quotes in this snippet: Windows PowerShell 5.1 strips them from native command arguments
$check = 'import sys; v = sys.version_info; ok = v[:2] == (3, 10) and v[2] <= 12 and sys.maxsize > 2**32; print(sys.version.split()[0]); sys.exit(0 if ok else 1)'
$version = & $Py @PyArgs -c $check
if ($LASTEXITCODE -ne 0) { throw "mlagents 1.1.0 needs 64-bit Python 3.10.1-3.10.12, found $version ($Py $PyArgs)" }

Write-Host "Creating venv at $Venv using Python $version"
Invoke-Checked $Py ($PyArgs + @("-m", "venv", $Venv))
$VenvPy = Join-Path $Venv "Scripts\python.exe"
Invoke-Checked $VenvPy @("-m", "pip", "install", "--upgrade", "pip")
# ML-Agents 4.x constrains torch to <= 2.8
Invoke-Checked $VenvPy @("-m", "pip", "install", "torch>=2.1.1,<=2.8.0")
Invoke-Checked $VenvPy @("-m", "pip", "install", "mlagents==1.1.0")
Invoke-Checked (Join-Path $Venv "Scripts\mlagents-learn.exe") @("--help") | Out-Null

Write-Host "Done. Use Tools\ml\nntools.cmd (build / record / train), or activate with: $Venv\Scripts\Activate.ps1"
