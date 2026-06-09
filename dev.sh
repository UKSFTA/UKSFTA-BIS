#!/bin/bash
PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$PROJECT_DIR"

command=$1

# Force Linux development
export DOTNET_CLI_TELEMETRY_OPTOUT=1

case $command in
    "test")
        echo "🧪 Running Fast Unit Tests for UKSFTA-BIS..."
        dotnet test -c Debug
        ;;
    "lint")
        echo "🧹 Linting & Formatting UKSFTA-BIS..."
        dotnet format
        ;;
    "watch")
        echo "👀 Watching for changes in UKSFTA-BIS..."
        dotnet watch test --c Debug
        ;;
    *)
        echo "Usage: ./dev.sh {test|lint|watch}"
        ;;
esac
