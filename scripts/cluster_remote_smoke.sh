#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
NINEP_BIN="${NINEP_BIN:-/usr/local/bin/9p}"
REMOTE_SSH="${REMOTE_SSH:-scott@rentonsoftworks.coin}"
REMOTE_PORT="${REMOTE_PORT:-2222}"
REMOTE_DIR="${REMOTE_DIR:-~/ninepsharp_cluster_smoke}"
RUNTIME_ID="${RUNTIME_ID:-linux-x64}"
SELF_CONTAINED="${SELF_CONTAINED:-true}"
PUBLISH_SINGLE_FILE="${PUBLISH_SINGLE_FILE:-false}"
KEEP_ARTIFACTS="${KEEP_ARTIFACTS:-false}"

CLUSTER_SYSTEM="${CLUSTER_SYSTEM:-NinePCluster}"
LOCAL_NODE_HOST="${LOCAL_NODE_HOST:-127.0.0.1}"
LOCAL_NODE_PORT="${LOCAL_NODE_PORT:-8081}"
REMOTE_NODE_HOST="${REMOTE_NODE_HOST:-127.0.0.1}"
REMOTE_NODE_PORT="${REMOTE_NODE_PORT:-8082}"
LOCAL_SEED_HOST="${LOCAL_SEED_HOST:-$LOCAL_NODE_HOST}"
REMOTE_SEED_HOST="${REMOTE_SEED_HOST:-$REMOTE_NODE_HOST}"

LOCAL_BIND_HOST="${LOCAL_BIND_HOST:-0.0.0.0}"
REMOTE_BIND_HOST="${REMOTE_BIND_HOST:-0.0.0.0}"
LOCAL_BIND_PORT="${LOCAL_BIND_PORT:-$LOCAL_NODE_PORT}"
REMOTE_BIND_PORT="${REMOTE_BIND_PORT:-$REMOTE_NODE_PORT}"
CLUSTER_INTERFACE="${CLUSTER_INTERFACE:-}"
PREFER_IPV6="${PREFER_IPV6:-false}"

LOCAL_9P_ADDR="${LOCAL_9P_ADDR:-127.0.0.1}"
LOCAL_9P_PORT="${LOCAL_9P_PORT:-5641}"
REMOTE_9P_ADDR="${REMOTE_9P_ADDR:-0.0.0.0}"
REMOTE_9P_PORT="${REMOTE_9P_PORT:-5641}"

RPC_URL="${RPC_URL:-http://127.0.0.1:6662/}"
RPC_USER="${RPC_USER:-test}"
RPC_PASSWORD="${RPC_PASSWORD:-}"

if [[ -z "$RPC_PASSWORD" ]]; then
  echo "RPC_PASSWORD is required (for remote JSON-RPC backend)." >&2
  exit 1
fi

if [[ ! -x "$NINEP_BIN" ]]; then
  echo "9p binary not found at $NINEP_BIN" >&2
  exit 1
fi

if [[ "$LOCAL_SEED_HOST" == "127.0.0.1" || "$REMOTE_SEED_HOST" == "127.0.0.1" ]]; then
  echo "Warning: loopback seed host detected. For cross-machine cluster tests set LOCAL_SEED_HOST/REMOTE_SEED_HOST to reachable addresses (for example Yggdrasil IPs)." >&2
fi

seed_uri_host() {
  local host="$1"
  if [[ "$host" == *:* && "$host" != \[*\] ]]; then
    printf '[%s]' "$host"
  else
    printf '%s' "$host"
  fi
}

LOCAL_SEED_URI_HOST="$(seed_uri_host "$LOCAL_SEED_HOST")"
REMOTE_SEED_URI_HOST="$(seed_uri_host "$REMOTE_SEED_HOST")"

LOCAL_ADDR="tcp!${LOCAL_9P_ADDR}!${LOCAL_9P_PORT}"
LOCAL_SERVER_PID=""
LOCAL_PACKAGE_DIR="$(mktemp -d)"
REMOTE_CONFIG_FILE="$(mktemp)"
REMOTE_CLUSTER_FILE="$(mktemp)"

cleanup() {
  if [[ "$KEEP_ARTIFACTS" == "true" ]]; then
    echo "KEEP_ARTIFACTS=true: preserving local package at $LOCAL_PACKAGE_DIR and remote runtime at ${REMOTE_DIR}" >&2
    return
  fi
  if [[ -n "$LOCAL_SERVER_PID" ]]; then
    kill "$LOCAL_SERVER_PID" >/dev/null 2>&1 || true
  fi
  ssh -p "$REMOTE_PORT" "$REMOTE_SSH" "if [ -f ${REMOTE_DIR}/server.pid ]; then kill \$(cat ${REMOTE_DIR}/server.pid) >/dev/null 2>&1 || true; fi" >/dev/null 2>&1 || true
  rm -rf "$LOCAL_PACKAGE_DIR"
  rm -f "$REMOTE_CONFIG_FILE" "$REMOTE_CLUSTER_FILE"
}
trap cleanup EXIT

echo "Publishing NinePSharp.Server..."
dotnet publish "$ROOT_DIR/NinePSharp.Server/NinePSharp.Server.csproj" \
  -c Debug \
  -r "$RUNTIME_ID" \
  --self-contained "$SELF_CONTAINED" \
  -p:PublishSingleFile="$PUBLISH_SINGLE_FILE" \
  -o "$LOCAL_PACKAGE_DIR" >/dev/null

LOCAL_CONFIG_FILE="$LOCAL_PACKAGE_DIR/config.json"
LOCAL_CLUSTER_FILE="$LOCAL_PACKAGE_DIR/cluster.conf"

cat > "$LOCAL_CONFIG_FILE" <<JSON
{
  "Server": {
    "Endpoints": [
      { "Address": "${LOCAL_9P_ADDR}", "Port": ${LOCAL_9P_PORT}, "Protocol": "tcp" }
    ],
    "Database": {
      "MountPath": "/db",
      "ConnectionString": "Data Source=cluster-smoke.db",
      "ProviderName": "Microsoft.Data.Sqlite"
    }
  }
}
JSON

cat > "$LOCAL_CLUSTER_FILE" <<CONF
SystemName = ${CLUSTER_SYSTEM}
Hostname = ${LOCAL_NODE_HOST}
Port = ${LOCAL_NODE_PORT}
Role = backend
BindHostname = ${LOCAL_BIND_HOST}
BindPort = ${LOCAL_BIND_PORT}
Seed = akka.tcp://${CLUSTER_SYSTEM}@${LOCAL_SEED_URI_HOST}:${LOCAL_NODE_PORT}
Seed = akka.tcp://${CLUSTER_SYSTEM}@${REMOTE_SEED_URI_HOST}:${REMOTE_NODE_PORT}
CONF

if [[ -n "$CLUSTER_INTERFACE" ]]; then
  {
    echo "InterfaceName = ${CLUSTER_INTERFACE}"
    echo "PreferIPv6 = ${PREFER_IPV6}"
  } >> "$LOCAL_CLUSTER_FILE"
fi

cat > "$REMOTE_CONFIG_FILE" <<JSON
{
  "Server": {
    "Endpoints": [
      { "Address": "${REMOTE_9P_ADDR}", "Port": ${REMOTE_9P_PORT}, "Protocol": "tcp" }
    ],
    "JsonRpc": {
      "MountPath": "/emc",
      "EndpointUrl": "${RPC_URL}",
      "RpcUser": "${RPC_USER}",
      "RpcPassword": "${RPC_PASSWORD}",
      "VaultKey": "emc-rpc",
      "Endpoints": [
        {
          "Name": "nameshow",
          "Method": "name_show",
          "Writable": true,
          "Description": "Write an NVS key and read the result."
        }
      ]
    }
  }
}
JSON

cat > "$REMOTE_CLUSTER_FILE" <<CONF
SystemName = ${CLUSTER_SYSTEM}
Hostname = ${REMOTE_NODE_HOST}
Port = ${REMOTE_NODE_PORT}
Role = backend
BindHostname = ${REMOTE_BIND_HOST}
BindPort = ${REMOTE_BIND_PORT}
Seed = akka.tcp://${CLUSTER_SYSTEM}@${LOCAL_SEED_URI_HOST}:${LOCAL_NODE_PORT}
Seed = akka.tcp://${CLUSTER_SYSTEM}@${REMOTE_SEED_URI_HOST}:${REMOTE_NODE_PORT}
CONF

if [[ -n "$CLUSTER_INTERFACE" ]]; then
  {
    echo "InterfaceName = ${CLUSTER_INTERFACE}"
    echo "PreferIPv6 = ${PREFER_IPV6}"
  } >> "$REMOTE_CLUSTER_FILE"
fi

echo "Syncing package and config to remote ${REMOTE_SSH}:${REMOTE_DIR}..."
ssh -p "$REMOTE_PORT" "$REMOTE_SSH" "mkdir -p ${REMOTE_DIR}; if [ -f ${REMOTE_DIR}/server.pid ]; then kill \$(cat ${REMOTE_DIR}/server.pid) >/dev/null 2>&1 || true; fi; pkill -x NinePSharp.Server >/dev/null 2>&1 || true; pkill -x NinePSharp.Serv >/dev/null 2>&1 || true"
scp -P "$REMOTE_PORT" -r "$LOCAL_PACKAGE_DIR/." "${REMOTE_SSH}:${REMOTE_DIR}/"
scp -P "$REMOTE_PORT" "$REMOTE_CONFIG_FILE" "${REMOTE_SSH}:${REMOTE_DIR}/config.json"
scp -P "$REMOTE_PORT" "$REMOTE_CLUSTER_FILE" "${REMOTE_SSH}:${REMOTE_DIR}/cluster.conf"

run_server_cmd="./NinePSharp.Server"
if [[ "$SELF_CONTAINED" != "true" ]]; then
  run_server_cmd="dotnet NinePSharp.Server.dll"
fi

echo "Starting remote server..."
ssh -n -p "$REMOTE_PORT" "$REMOTE_SSH" "cd ${REMOTE_DIR}; if [ -f server.pid ]; then kill \$(cat server.pid) >/dev/null 2>&1 || true; fi; nohup ${run_server_cmd} > server.log 2>&1 < /dev/null & printf '%s\n' \$! > server.pid"

echo "Starting local server..."
if [[ "$SELF_CONTAINED" == "true" ]]; then
  "$LOCAL_PACKAGE_DIR/NinePSharp.Server" > "$LOCAL_PACKAGE_DIR/server.log" 2>&1 &
else
  dotnet "$LOCAL_PACKAGE_DIR/NinePSharp.Server.dll" > "$LOCAL_PACKAGE_DIR/server.log" 2>&1 &
fi
LOCAL_SERVER_PID=$!

echo "Waiting for cluster convergence..."
sleep 8

echo "Checking local mounts through 9P..."
if ! "$NINEP_BIN" -a "$LOCAL_ADDR" stat /db >/dev/null 2>&1; then
  echo "FAIL: local /db mount missing." >&2
  exit 1
fi

FOUND_REMOTE=0
for _ in $(seq 1 15); do
  if "$NINEP_BIN" -a "$LOCAL_ADDR" stat /emc >/dev/null 2>&1; then
    FOUND_REMOTE=1
    break
  fi
  sleep 2
done

if [[ "$FOUND_REMOTE" -ne 1 ]]; then
  echo "FAIL: remote /emc mount did not appear through federation." >&2
  exit 1
fi

echo "PASS: cluster federation exposes /db (local) and /emc (remote) on local node."
