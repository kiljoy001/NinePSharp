#!/bin/bash
# Quick verification that Stryker config is valid

echo "🔍 Verifying Stryker Configuration..."
echo ""

CONFIG="${1:-stryker-config-simple.json}"

if [ ! -f "$CONFIG" ]; then
    echo "❌ Config file not found: $CONFIG"
    exit 1
fi

echo "📄 Config file: $CONFIG"
echo ""

# Check for invalid keys
echo "Checking for invalid JavaScript Stryker keys..."
if grep -q '"test-runner"\|"timeout-ms"\|"coverageAnalysis"\|"mutationLevel"' "$CONFIG"; then
    echo "❌ Found invalid JavaScript Stryker keys"
    grep -n '"test-runner"\|"timeout-ms"\|"coverageAnalysis"\|"mutationLevel"' "$CONFIG"
    exit 1
else
    echo "✅ No invalid JavaScript keys found"
fi
echo ""

# Check for invalid mutation types
echo "Checking for invalid mutation types..."
if grep -q '"StringLiteral"\|"InterpolatedString"\|"RegexChange"\|"BinaryOperator"\|"EqualityOperator"\|"BlockStatement"' "$CONFIG"; then
    echo "❌ Found invalid mutation type names"
    grep -n '"StringLiteral"\|"InterpolatedString"\|"RegexChange"\|"BinaryOperator"\|"EqualityOperator"\|"BlockStatement"' "$CONFIG"
    exit 1
else
    echo "✅ No invalid mutation types found"
fi
echo ""

# Check for since + baseline conflict
HAS_SINCE=$(grep -c '"since"' "$CONFIG" || echo 0)
HAS_BASELINE=$(grep -c '"baseline"' "$CONFIG" || echo 0)

if [ "$HAS_SINCE" -gt 0 ] && [ "$HAS_BASELINE" -gt 0 ]; then
    echo "❌ Config has both 'since' and 'baseline' (mutually exclusive)"
    exit 1
elif [ "$HAS_SINCE" -gt 0 ]; then
    echo "ℹ️  Config uses 'since' (git-based incremental)"
elif [ "$HAS_BASELINE" -gt 0 ]; then
    echo "ℹ️  Config uses 'baseline' (trend tracking)"
    # Check for version
    if ! grep -q '"version"' "$CONFIG"; then
        echo "⚠️  Warning: baseline requires project version"
    fi
else
    echo "✅ Simple config (no since/baseline)"
fi
echo ""

# Validate JSON syntax
echo "Checking JSON syntax..."
if command -v jq &> /dev/null; then
    if jq empty "$CONFIG" 2>/dev/null; then
        echo "✅ Valid JSON syntax"
    else
        echo "❌ Invalid JSON syntax"
        jq empty "$CONFIG"
        exit 1
    fi
else
    echo "⚠️  jq not installed, skipping JSON validation"
fi
echo ""

echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "✅ Configuration appears valid!"
echo ""
echo "Ready to run:"
echo "  ./stryker-ramdisk.sh $CONFIG"
echo ""
echo "Or without RAM disk:"
echo "  dotnet stryker --config-file $CONFIG"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
