#!/usr/bin/env bash
PROJECT_ROOT=$(pwd)
export SOURCE_DATE_EPOCH=$(date +%s)

# CONFIG defaults
CONFIG="Debug"
IS_RELEASE=false

if [[ " $* " == *" release "* ]]; then
    CONFIG="Release"
    IS_RELEASE=true
fi

build_target() {
    echo "🚀 Building UKSFTA-BIS ($CONFIG)..."
    # Build the entire solution
    dotnet build -c "$CONFIG"
    return $?
}

# 1. Build Logic
build_target
STATUS=$?

if [ $STATUS -eq 0 ]; then
    echo "✅ Build complete!"
    exit 0
else
    echo "❌ Build failed!"
    exit 1
fi
