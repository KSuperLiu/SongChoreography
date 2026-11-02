$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
python -m venv (Join-Path $projectRoot '.venv')
if ($LASTEXITCODE -ne 0) { throw '创建 Python 虚拟环境失败，请安装 Python 3.10 或更新版本。' }
$pythonPath = Join-Path $projectRoot '.venv\Scripts\python.exe'
& $pythonPath -m pip install -r (Join-Path $projectRoot 'Analyzer\requirements.txt')
if ($LASTEXITCODE -ne 0) { throw '安装分析依赖失败。' }
Write-Output "分析器 Python 路径：$pythonPath"
