#!/usr/bin/env bash

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

usage() {
  cat <<'EOF'
Usage: backtest.sh <strategy>

Examples:
  ./backtest.sh Strategies/DiversifiedLeverage
  ./backtest.sh DiversifiedLeverage

The strategy argument can be:
  - a strategy directory path
  - a directory name under ./Strategies
EOF
}

if [[ $# -ne 1 || "$1" == "-h" || "$1" == "--help" ]]; then
  usage
  exit 0
fi

strategy_input="$1"
strategy_dir=""

if [[ -d "$strategy_input" ]]; then
  strategy_dir="$(cd "$strategy_input" && pwd)"
elif [[ -d "$repo_root/$strategy_input" ]]; then
  strategy_dir="$(cd "$repo_root/$strategy_input" && pwd)"
elif [[ -d "$repo_root/Strategies/$strategy_input" ]]; then
  strategy_dir="$(cd "$repo_root/Strategies/$strategy_input" && pwd)"
else
  echo "Error: could not find strategy directory for '$strategy_input'." >&2
  usage >&2
  exit 1
fi

csproj_path="$(find "$strategy_dir" -maxdepth 1 -name '*.csproj' | head -n 1 || true)"
if [[ -z "$csproj_path" ]]; then
  echo "Error: no .csproj file found in '$strategy_dir'." >&2
  exit 1
fi

project_name="$(basename "$csproj_path" .csproj)"
relative_csproj_path="${csproj_path#"$repo_root"/}"
relative_strategy_dir="${strategy_dir#"$repo_root"/}"
algorithm_dll="/LeanProject/${relative_strategy_dir}/bin/Debug/${project_name}.dll"
docker_image="quantconnect/lean:latest"

docker run --rm \
  --entrypoint bash \
  -v "$repo_root":/LeanProject \
  -v "$repo_root/data":/Lean/Launcher/bin/Debug/data \
  -v "$repo_root/storage":/Lean/Launcher/bin/Debug/storage \
  -w /Lean/Launcher/bin/Debug \
  "$docker_image" \
  -lc "dotnet build \"/LeanProject/${relative_csproj_path}\" -nologo -p:Configuration=Debug -p:Platform=AnyCPU -p:TargetFramework=net10.0 -p:OutputPath=\"/LeanProject/${relative_strategy_dir}/bin/Debug\" -p:GenerateAssemblyInfo=false -p:GenerateTargetFrameworkAttribute=false -p:AppendTargetFrameworkToOutputPath=false -p:AutomaticallyUseReferenceAssemblyPackages=false -p:CopyLocalLockFileAssemblies=true && dotnet QuantConnect.Lean.Launcher.dll --config /LeanProject/lean.json --algorithm-location \"$algorithm_dll\""