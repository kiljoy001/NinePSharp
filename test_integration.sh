#!/bin/bash

# 1. Clean
rm -f /tmp/root_debug.log /tmp/secret_debug.log /tmp/secret_error.log

# 2. Build
echo "Building server..."
dotnet build NinePSharp.Server/NinePSharp.Server.csproj -c Debug > /dev/null

# 3. Start
echo "Starting NinePSharp Server..."
BIN_DIR="./NinePSharp.Server/bin/Debug/net10.0"
# Ensure directory exists for logs
mkdir -p $BIN_DIR/vaults

# RUN WITH DOTNET COMMAND FOR RELIABILITY
dotnet $BIN_DIR/NinePSharp.Server.dll > server_integration.log 2>&1 &
SERVER_PID=$!

echo "Waiting for server (PID: $SERVER_PID)..."
sleep 10

ADDR="tcp!127.0.0.1!5641"
NINEP="/usr/local/bin/9p"

# Test 3: Provision
echo -n "Test 3 (Provision): "
echo "testpass:mykey:hidden-data" | $NINEP -a $ADDR write /secrets/provision

# Test 4: Unlock
echo -n "Test 4 (Unlock): "
echo "testpass:mykey" | $NINEP -a $ADDR write /secrets/unlock

# CHECK LOGS
echo "--- /tmp/root_debug.log ---"
cat /tmp/root_debug.log 2>/dev/null || echo "NOT FOUND"
echo "--- /tmp/secret_debug.log ---"
cat /tmp/secret_debug.log 2>/dev/null || echo "NOT FOUND"

VLT_COUNT=$(ls -1 $BIN_DIR/vaults/secret_*.vlt 2>/dev/null | wc -l)
if [ "$VLT_COUNT" -gt 0 ]; then 
    echo "PASS ($VLT_COUNT file created)"
else 
    echo "FAIL (No file in $BIN_DIR/vaults/)"
fi

# Test 5: Read back secret
echo -n "Test 5 (Read): "
READ_OUTPUT=$($NINEP -a $ADDR read /secrets/vault/mykey 2>/dev/null || true)
if [ "$READ_OUTPUT" = "hidden-data" ]; then
    echo "PASS"
else
    echo "FAIL (Expected 'hidden-data', got '$READ_OUTPUT')"
fi

# 4. Cleanup
kill $SERVER_PID
echo "Integration Tests Complete."
