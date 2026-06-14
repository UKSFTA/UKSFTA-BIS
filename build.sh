#!/usr/bin/env bash
SOURCE_DATE_EPOCH=$(date +%s)
export SOURCE_DATE_EPOCH

# CONFIG defaults
CONFIG="Debug"

if [[ " $* " == *" release "* ]]; then
    CONFIG="Release"
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
