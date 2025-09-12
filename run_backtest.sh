#!/bin/bash

# QuantConnect Lean Backtest Runner
# Bypasses the problematic Lean CLI and runs backtests directly with Docker
# 
# Usage: ./run_backtest.sh [algorithm_name] [project_path]
# Example: ./run_backtest.sh TestAlgorithm TestProject

set -e  # Exit on any error

# Default values
ALGORITHM_NAME="${1:-TestDiversifiedLeverage.TestAlgorithm}"
PROJECT_PATH="${2:-TestProject}"

# Extract the project name from the path (last directory component)
PROJECT_NAME=$(basename "${PROJECT_PATH}")
ALGORITHM_DLL="${PROJECT_PATH}/bin/Debug/${PROJECT_NAME}.dll"

echo "Starting QuantConnect Lean Backtest"
echo "Algorithm: ${ALGORITHM_NAME}"
echo "Project: ${PROJECT_PATH}"
echo "DLL: ${ALGORITHM_DLL}"
echo "================================"

# Check if the project DLL exists
if [ ! -f "${ALGORITHM_DLL}" ]; then
    echo "DLL not found. Building project..."
    cd "${PROJECT_PATH}"
    dotnet build
    cd ..
    
    if [ ! -f "${ALGORITHM_DLL}" ]; then
        echo "Failed to build ${ALGORITHM_DLL}"
        exit 1
    fi
    echo "Build successful"
fi

# Run the backtest using Docker
echo "Running backtest in Docker container..."

# Make the DLL path relative to current working directory if it's absolute
if [[ "${ALGORITHM_DLL}" = /* ]]; then
    # Remove the current working directory prefix to make it relative
    RELATIVE_DLL_PATH="${ALGORITHM_DLL#$(pwd)/}"
else
    RELATIVE_DLL_PATH="${ALGORITHM_DLL}"
fi

echo "Using DLL path: /LeanProject/${RELATIVE_DLL_PATH}"

docker run --rm \
  -v "$(pwd)":/LeanProject \
  -v "$(pwd)/data":/Data \
  -w /LeanProject \
  --entrypoint="" \
  quantconnect/lean:latest \
  dotnet /Lean/Launcher/bin/Debug/QuantConnect.Lean.Launcher.dll \
  --config "/LeanProject/lean.json" \
  --algorithm-type-name "${ALGORITHM_NAME}" \
  --algorithm-language "CSharp" \
  --algorithm-location "/LeanProject/${RELATIVE_DLL_PATH}" \
  --data-folder "/Data" \
  --close-automatically "true" \
  --results-destination-folder "/LeanProject/${PROJECT_PATH}/backtests"

if [ $? -eq 0 ]; then
    echo ""
    echo "Backtest completed successfully!"
    echo "Results saved to: ${PROJECT_PATH}/backtests/"
    echo "Log file: ${PROJECT_PATH}/backtests/${ALGORITHM_NAME}-log.txt"
else
    echo ""
    echo "Backtest failed. Check the output above for errors."
    exit 1
fi