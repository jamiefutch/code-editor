#!/usr/bin/env bash
# run.sh — Build and run CodeEditor. Pass file paths as arguments to open them on startup.
# Usage: ./scripts/run.sh [file.cs file.js ...] [--read-only]
#   Without args, opens the editor with an empty document.

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"

# Sandbox-compatible NuGet cache (outside ~/.nuget which is read-only in DSH)
export NUGET_HTTP_CACHE_PATH="${NUGET_HTTP_CACHE_PATH:-/tmp/nuget-cache}"
export NUGET_PACKAGES="${NUGET_PACKAGES:-/tmp/nuget-packages}"

PROJECT="$ROOT_DIR/CodeEditor/CodeEditor.csproj"

# Build first
echo "=== Building ==="
bash "$SCRIPT_DIR/build.sh" --project "$PROJECT"

# Run with forwarded arguments (safe expansion for empty arg list)
exec dotnet run --project "$PROJECT" -- "$@"
