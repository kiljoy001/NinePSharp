#!/bin/bash
# Dead simple RAM disk runner - no magic

set -e

RAMDISK="/dev/shm/ninepsharp-stryker"
PROJECT="/home/scott/Repo/NinePSharp"

echo "Copying project to RAM..."
rm -rf "$RAMDISK"
mkdir -p "$RAMDISK"
cp -r "$PROJECT" "$RAMDISK/"
cd "$RAMDISK/NinePSharp"

echo "Running Stryker from RAM..."
dotnet stryker

echo ""
echo "Copying results back..."
cp -r StrykerOutput "$PROJECT/"

echo "Cleaning up..."
cd /
rm -rf "$RAMDISK"

echo "Done. Results in $PROJECT/StrykerOutput"
