#!/bin/bash
# Run all tests and generate unified report

echo "=========================================="
echo "Running NinePSharp Test Suite"
echo "=========================================="
echo ""

# Create temp directory for test results
TEMP_DIR=$(mktemp -d)
REPORT_FILE="test-report-$(date +%Y%m%d-%H%M%S).txt"

echo "Test results will be saved to: $REPORT_FILE"
echo ""

# Run C# Tests
echo "=========================================="
echo "Running C# Tests (NinePSharp.Tests)"
echo "=========================================="
dotnet test NinePSharp.Tests/NinePSharp.Tests.csproj --logger "trx;LogFileName=csharp-results.trx" --results-directory "$TEMP_DIR/csharp" 2>&1 | tee -a "$REPORT_FILE"

echo ""

# Run F# Parser Tests  
echo "=========================================="
echo "Running F# Tests (NinePSharp.Parser.Tests)"
echo "=========================================="
dotnet test NinePSharp.Parser.Tests/NinePSharp.Parser.Tests.fsproj --logger "trx;LogFileName=fs-results.trx" --results-directory "$TEMP_DIR/fs" 2>&1 | tee -a "$REPORT_FILE"

echo ""

# Summary
echo "=========================================="
echo "Test Summary"
echo "=========================================="
echo ""

# Extract summary from C# tests
echo "C# Tests:"
grep -E "Total tests:|Failed:|Passed:|Skipped:" "$TEMP_DIR/csharp/csharp-results.trx" 2>/dev/null || echo "  (results in $TEMP_DIR/csharp)"

echo ""

# Extract summary from F# tests
echo "F# Tests:"
grep -E "Total tests:|Failed:|Passed:|Skipped:" "$TEMP_DIR/fs/fs-results.trx" 2>/dev/null || echo "  (results in $TEMP_DIR/fs)"

echo ""
echo "Full reports saved to: $REPORT_FILE"
echo "TRX files in: $TEMP_DIR"

# Clean up temp files after displaying
# rm -rf "$TEMP_DIR"
