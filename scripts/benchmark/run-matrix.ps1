param(
    [string]$BaseUrl = "http://localhost:8080",
    [string]$Path = "/health",
    [string]$OutputDirectory = "artifacts/load-tests"
)

$workloads = @(
    @{ Name = "smoke-1x"; Requests = 1000; Concurrency = 1 },
    @{ Name = "baseline-16x"; Requests = 10000; Concurrency = 16 },
    @{ Name = "medium-64x"; Requests = 50000; Concurrency = 64 },
    @{ Name = "high-256x"; Requests = 100000; Concurrency = 256 },
    @{ Name = "saturation-512x"; Requests = 250000; Concurrency = 512 }
)

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
foreach ($workload in $workloads) {
    Write-Host "Running $($workload.Name)..."
    dotnet run --project load-tests/Conflux.LoadTests -c Release --no-restore -- `
        --base-url=$BaseUrl `
        --path=$Path `
        --requests=$($workload.Requests) `
        --concurrency=$($workload.Concurrency) `
        --output-directory=$OutputDirectory `
        --output="$($workload.Name).json"
    if ($LASTEXITCODE -ne 0) { throw "Workload $($workload.Name) failed." }
}

$resultFiles = Get-ChildItem -Path $OutputDirectory -Filter *.json | ForEach-Object { $_.FullName }
python scripts/benchmark/generate_report.py @resultFiles --output artifacts/benchmark-report
