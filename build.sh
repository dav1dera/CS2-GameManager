#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"
dotnet restore
dotnet publish -c Release -o out
echo
echo "Built: $(pwd)/out/GameManager.dll"
