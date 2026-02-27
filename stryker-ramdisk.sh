#!/bin/bash
# RAM Disk Mutation Testing - Optimized for 96GB System
# This script uses /dev/shm to run Stryker mutation testing in RAM
# Expected speedup: 10-20x faster than disk I/O

set -e

RAMDISK_SIZE="32G"  # Adjust based on project size (you have 96GB available)
RAMDISK_PATH="/dev/shm/stryker-workspace"
PROJECT_ROOT="$(cd "$(dirname "$0")" && pwd)"
STRYKER_OUTPUT="$PROJECT_ROOT/StrykerOutput"
CONFIG_FILE="${1:-stryker-config-full.json}"  # Default to full baseline config

echo "🚀 Setting up RAM disk mutation testing..."
echo "   RAM Disk: $RAMDISK_PATH"
echo "   Size: $RAMDISK_SIZE"
echo "   Cores: 12"
echo "   Config: $CONFIG_FILE"

# Create RAM disk workspace
mkdir -p "$RAMDISK_PATH"
echo "✓ RAM disk workspace created"

# Copy project to RAM disk (excluding build artifacts and other configs)
echo "📦 Copying project to RAM disk..."

# Check if config needs git (has 'since' feature)
NEEDS_GIT=false
if grep -q '"since"' "$PROJECT_ROOT/$CONFIG_FILE" 2>/dev/null; then
    NEEDS_GIT=true
    echo "   Config uses 'since' - will copy .git directory"
fi

# Exclude all stryker configs EXCEPT the one we're using
EXCLUDE_CONFIGS=""
for config in "$PROJECT_ROOT"/stryker-config*.json; do
    basename_config=$(basename "$config")
    if [ "$basename_config" != "$CONFIG_FILE" ]; then
        EXCLUDE_CONFIGS="$EXCLUDE_CONFIGS --exclude=$basename_config"
    fi
done

if [ "$NEEDS_GIT" = true ]; then
    # Copy WITH .git for since feature
    rsync -av --exclude='bin/' --exclude='obj/' --exclude='StrykerOutput/' \
          --exclude='TestResults/' $EXCLUDE_CONFIGS \
          "$PROJECT_ROOT/" "$RAMDISK_PATH/"
else
    # Copy WITHOUT .git for simple config
    rsync -av --exclude='bin/' --exclude='obj/' --exclude='StrykerOutput/' \
          --exclude='.git/' --exclude='TestResults/' $EXCLUDE_CONFIGS \
          "$PROJECT_ROOT/" "$RAMDISK_PATH/"
fi

echo "✓ Project copied to RAM (config: $CONFIG_FILE only)"

# Function to cleanup on exit
cleanup() {
    echo ""
    echo "🧹 Copying results back to disk..."
    if [ -d "$RAMDISK_PATH/StrykerOutput" ]; then
        rsync -av "$RAMDISK_PATH/StrykerOutput/" "$STRYKER_OUTPUT/"
        echo "✓ Results saved to $STRYKER_OUTPUT"
    fi

    echo "🗑️  Cleaning up RAM disk..."
    rm -rf "$RAMDISK_PATH"
    echo "✓ Cleanup complete"
}

trap cleanup EXIT INT TERM

# Run Stryker from RAM disk
cd "$RAMDISK_PATH"
echo ""
echo "🔬 Running mutation testing from RAM disk..."
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"

# Shift the config file arg if it was provided, pass remaining args to stryker
if [ $# -gt 0 ] && [[ "$1" == *.json ]]; then
    shift
fi

dotnet stryker --config-file "$CONFIG_FILE" "$@"

echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "✅ Mutation testing complete!"
