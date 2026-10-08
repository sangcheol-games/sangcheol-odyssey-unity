# ChartPorter 가상환경 설치 (Windows PowerShell)
# 사용: Tools/ChartPorter 에서  powershell -ExecutionPolicy Bypass -File setup_env.ps1
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot
if (-not (Test-Path ".venv")) { python -m venv .venv }
.\.venv\Scripts\python.exe -m pip install --upgrade pip
if (Test-Path "requirements.lock.txt") {
    .\.venv\Scripts\python.exe -m pip install -r requirements.lock.txt
} else {
    .\.venv\Scripts\python.exe -m pip install -r requirements.txt
}
.\.venv\Scripts\python.exe -c "import librosa, numpy, soundfile, yaml; print('ok: librosa', librosa.__version__)"
