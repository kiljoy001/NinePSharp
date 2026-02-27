#!/bin/bash
# Differential Mutation Testing - Only test what changed
# This is the ULTIMATE optimization: 2-day job → 2-hour job
# Tests only the code that AI just generated in your branch

set -e

PROJECT_ROOT="$(cd "$(dirname "$0")" && pwd)"
BASE_BRANCH="${1:-HEAD}"
USE_RAMDISK="${USE_RAMDISK:-true}"

echo "🎯 Differential Mutation Testing (Git-based)"
echo "   Testing changes since: $BASE_BRANCH"
echo "   Current branch: $(git branch --show-current)"
echo ""

# Show git status
echo "📊 Git status:"
git status --short | head -10
echo ""

# The 'since' feature in stryker-config.json automatically handles git comparison
# It will only mutate files that changed since the last baseline
# No need to manually filter files - Stryker does it for us!

echo "🔬 Running incremental mutation testing..."
echo "   Stryker will automatically detect changed files using git"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"

if [ "$USE_RAMDISK" = "true" ] && [ -d "/dev/shm" ]; then
    echo "🚀 Using RAM disk for maximum speed..."
    "$PROJECT_ROOT/stryker-ramdisk.sh" stryker-config.json
else
    dotnet stryker --config-file stryker-config.json
fi

echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"

echo ""
echo "✅ Differential mutation testing complete!"
echo "   Results in: StrykerOutput/reports/"
