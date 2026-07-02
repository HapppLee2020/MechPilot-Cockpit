# CKP-005-06: Design tree / flat view / filter / selection regression checks
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$appJs = Join-Path $repoRoot 'src\SwAgentAddin\frontend\property-workbench\app.js'

if (-not (Test-Path $appJs)) {
    Write-Error "app.js not found: $appJs"
}

$size = (Get-Item $appJs).Length
Write-Host "app.js size: $size bytes"
if ($size -le 170KB) {
    Write-Error "FAIL: app.js size must be > 170KB (got $size bytes)"
}
Write-Host "PASS: app.js size > 170KB"

Write-Host "Running node -c ..."
node -c $appJs
if ($LASTEXITCODE -ne 0) {
    Write-Error "FAIL: node -c app.js"
}
Write-Host "PASS: node -c app.js"

$content = Get-Content -Raw -Encoding UTF8 $appJs

$requiredFunctions = @(
    'renderTreeFilterBar',
    'isNodePassingFilters',
    'isNodeVisible',
    'getNodeGroupKey',
    'toggleNodeGroupChecked',
    'handleNodeCheckToggle',
    'buildFlatView',
    'renderPropertyWorkbench',
    'getWorkspaceItems'
)

foreach ($fn in $requiredFunctions) {
    if ($content -notmatch ("function\s+" + [regex]::Escape($fn) + "\s*\(")) {
        Write-Error "FAIL: missing function $fn"
    }
    Write-Host "PASS: function $fn exists"
}

if ($content -match 'function\s+handleNodeCheckToggle[\s\S]*?setAllPartsChecked\s*\(') {
    Write-Error 'FAIL: handleNodeCheckToggle must not call setAllPartsChecked unconditionally'
}
Write-Host 'PASS: handleNodeCheckToggle does not call setAllPartsChecked'

if ($content -match "return\s+'name:'\s*\+\s*cleanFlat") {
    Write-Error 'FAIL: getNodeGroupKey must not use cleanName-only global key'
}
Write-Host 'PASS: getNodeGroupKey does not use name-only global key'

if ($content -match 'function\s+renderTreeFilterBar\s*\(\)\s*\{([\s\S]*?)\}\s*\r?\n\s*function\s+clearFilteredCheckedIds') {
    if ($Matches[1] -match 'clearFilteredCheckedIds\s*\(') {
        Write-Error 'FAIL: filter button handler must not call clearFilteredCheckedIds by default'
    }
} else {
    Write-Error 'FAIL: could not locate renderTreeFilterBar for filter handler audit'
}
Write-Host 'PASS: filter handler does not clear checkedNodeIds by default'

Write-Host 'CKP-005-06 regression: ALL PASS'
