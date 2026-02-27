#!/bin/bash
# Quick test of Stryker config without RAM disk
# This helps verify the config works before doing a full RAM disk run

CONFIG="stryker-config-simple.json"

echo "🧪 Testing Stryker configuration (no RAM disk)..."
echo "   Config: $CONFIG"
echo "   This will start Stryker but you can Ctrl+C after a few seconds"
echo "   We just want to verify it doesn't error on startup"
echo ""
echo "Starting in 3 seconds... (Ctrl+C to abort)"
sleep 3

dotnet stryker --config-file "$CONFIG"
