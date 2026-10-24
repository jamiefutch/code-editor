#!/usr/bin/env bash
# build.sh — Build the CodeEditor project with sandbox-compatible NuGet settings.
# Usage: ./scripts/build.sh [--project <path>] [--configuration Debug|Release]
#   Without args, builds CodeEditor.csproj in Debug mode.

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"

# Sandbox-compatible NuGet cache (outside ~/.nuget which is read-only in DSH)
export NUGET_HTTP_CACHE_PATH="${NUGET_HTTP_CACHE_PATH:-/tmp/nuget-cache}"
export NUGET_PACKAGES="${NUGET_PACKAGES:-/tmp/nuget-packages}"

PROJECT_PATH="$ROOT_DIR/CodeEditor/CodeEditor.csproj"
CONFIG="Debug"

# Parse arguments
while [ $# -gt 0 ]; do
  case "$1" in
    --project)
      PROJECT_PATH="$2"
      shift 2
      ;;
    --configuration)
      CONFIG="$2"
      shift 2
      ;;
    Debug|Release)
      CONFIG="$1"
      shift
      ;;
    *)
      echo "Unknown argument: $1" >&2
      exit 1
      ;;
  esac
done

echo "=== CodeEditor Build ==="
echo "Root:     $ROOT_DIR"
echo "Project:  $PROJECT_PATH"
echo "Config:   $CONFIG"
echo "NuGet pkg: $NUGET_PACKAGES"
echo ""

dotnet restore "$PROJECT_PATH" && \
dotnet build "$PROJECT_PATH" -c "$CONFIG"
