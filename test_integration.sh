#!/bin/bash

# 1. Start the server in the background
echo "Starting NinePSharp Server..."
dotnet run --project NinePSharp.Server > server_test.log 2>&1 &
SERVER_PID=$!

# Give it a moment to boot
sleep 5

# 2. Setup
ADDR="tcp!127.0.0.1!5641"
NINEP="/usr/local/bin/9p"

echo "Running tests against $ADDR..."

# Test 1: Standard 9P2000 negotiation
echo -n "Test 1 (9P2000 - List Root): "
$NINEP -a $ADDR ls / > /dev/null
if [ $? -eq 0 ]; then echo "PASS"; else echo "FAIL"; fi

# Test 2: Verify Backends are visible
echo -n "Test 2 (List Mounts): "
BACKENDS=$($NINEP -a $ADDR ls /)
if echo "$BACKENDS" | grep -q "secrets"; then echo "PASS"; else echo "FAIL (Got: $BACKENDS)"; fi

# Test 3: Functional Secret Test
echo -n "Test 3 (Provision & Unlock Secret): "
echo "testpass:mykey:hidden-data" | $NINEP -a $ADDR write /secrets/provision > /dev/null 2>&1
echo "testpass" | $NINEP -a $ADDR write /secrets/unlock > /dev/null 2>&1
RESULT=$($NINEP -a $ADDR cat /secrets/vault/mykey 2>/dev/null)
if [ "$RESULT" == "hidden-data" ]; then echo "PASS"; else echo "FAIL (Got: $RESULT)"; fi

# 3. Cleanup
echo "Stopping Server..."
kill $SERVER_PID
rm -rf vaults/
echo "Integration Tests Complete."
