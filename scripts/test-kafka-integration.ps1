$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$networkName = "docker_default"
$image = "mcr.microsoft.com/dotnet/sdk:10.0.301"
$project = "tests/BuildingBlocks/Conflux.Kafka.IntegrationTests/Conflux.Kafka.IntegrationTests.csproj"

$network = docker network ls --format "{{.Name}}" | Where-Object { $_ -eq $networkName }
if (-not $network) {
    throw "Docker network '$networkName' was not found. Start the Conflux Kafka stack first or update this script to the broker's Docker network."
}

$broker = docker ps --filter "name=^/docker-kafka-1$" --format "{{.Names}}"
if (-not $broker) {
    throw "Expected Kafka container 'docker-kafka-1' is not running. No containers were started or restarted by this script."
}

Write-Host "Running Kafka broker integration tests inside Docker network '$networkName'."
Write-Host "The tests use unique conflux.integration-* topics and do not publish to business-event topics."

docker run --rm `
    --network $networkName `
    --mount "type=bind,source=$repoRoot,target=/workspace" `
    --workdir /workspace `
    --env "CONFLUX_KAFKA_BOOTSTRAP_SERVERS=kafka:9092" `
    $image `
    dotnet test $project --logger "console;verbosity=normal"

if ($LASTEXITCODE -ne 0) {
    throw "Kafka broker integration tests failed with exit code $LASTEXITCODE."
}
