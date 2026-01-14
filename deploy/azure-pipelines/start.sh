#!/usr/bin/env bash
set -euo pipefail

# Step-down from root to azp user if running as root
if [ "$(id -u)" -eq 0 ]; then
  if [ -z "${AZP_AGENT_NAME:-}" ] && [ -S /var/run/docker.sock ] && [ -n "${HOSTNAME:-}" ]; then
    container_name="$(
      curl -fsS --unix-socket /var/run/docker.sock \
        "http://localhost/containers/${HOSTNAME}/json" 2>/dev/null \
        | jq -r '.Name' 2>/dev/null || true
    )"
    if [ -n "${container_name}" ] && [ "${container_name}" != "null" ]; then
      export AZP_AGENT_NAME="${container_name#/}"
    else
      export AZP_AGENT_NAME="${HOSTNAME}"
    fi
  fi

  if [ -z "${AZP_WORK:-}" ]; then
    base_work="${VSTS_WORK:-_work}"
    if [ "${base_work}" = "/azp/_work" ] || [ "${base_work}" = "_work" ]; then
      AZP_WORK="${base_work%/}/${AZP_AGENT_NAME}"
    else
      AZP_WORK="${base_work}"
    fi
    export AZP_WORK
  fi

  work_dir="${AZP_WORK}"
  if [[ "${work_dir}" != /* ]]; then
    work_dir="/azp/${work_dir}"
  fi

  mkdir -p "${work_dir}" /azp/_diag
  chown -R azp:azp "${work_dir}" /azp/_diag
  if command -v gosu >/dev/null 2>&1; then
    exec gosu azp "$0" "$@"
  elif command -v su-exec >/dev/null 2>&1; then
    exec su-exec azp "$0" "$@"
  else
    exec su -s /bin/bash azp -c "$0 $*"
  fi
fi

# Map legacy VSTS environment variables to AZP
if [ -z "${AZP_URL:-}" ] && [ -n "${VSTS_ACCOUNT:-}" ]; then
  if [[ "${VSTS_ACCOUNT}" =~ ^https?:// ]]; then
    AZP_URL="${VSTS_ACCOUNT}"
  else
    AZP_URL="https://dev.azure.com/${VSTS_ACCOUNT}"
  fi
fi

if [ -z "${AZP_TOKEN:-}" ] && [ -n "${VSTS_TOKEN:-}" ]; then
  AZP_TOKEN="${VSTS_TOKEN}"
fi

if [ -z "${AZP_POOL:-}" ] && [ -n "${VSTS_POOL:-}" ]; then
  AZP_POOL="${VSTS_POOL}"
fi

if [ -z "${AZP_WORK:-}" ] && [ -n "${VSTS_WORK:-}" ]; then
  AZP_WORK="${VSTS_WORK}"
fi

# Validate required configuration
if [ -z "${AZP_URL:-}" ]; then
  echo "AZP_URL (or VSTS_ACCOUNT) is required." >&2
  exit 1
fi

if [ -z "${AZP_TOKEN:-}" ]; then
  echo "AZP_TOKEN (or VSTS_TOKEN) is required." >&2
  exit 1
fi

# Set defaults
AZP_POOL="${AZP_POOL:-Default}"
AZP_WORK="${AZP_WORK:-_work}"
if [ -z "${AZP_AGENT_NAME:-}" ]; then
  if [ -S /var/run/docker.sock ] && [ -n "${HOSTNAME:-}" ]; then
    container_name="$(
      curl -fsS --unix-socket /var/run/docker.sock \
        "http://localhost/containers/${HOSTNAME}/json" 2>/dev/null \
        | jq -r '.Name' 2>/dev/null || true
    )"
    if [ -n "${container_name}" ] && [ "${container_name}" != "null" ]; then
      AZP_AGENT_NAME="${container_name#/}"
    else
      AZP_AGENT_NAME="${HOSTNAME}"
    fi
  else
    AZP_AGENT_NAME="$(hostname)"
  fi
fi

# State tracking
agent_pid=""
cleaned_up=0
shutdown_requested=0

# Cleanup function - deregister agent from Azure DevOps
cleanup() {
  if [ "${cleaned_up}" -eq 1 ]; then
    return
  fi
  cleaned_up=1

  echo "Cleaning up agent ${AZP_AGENT_NAME}..."

  # Stop error propagation during cleanup
  set +e

  # Signal agent to shutdown gracefully if running
  if [ -n "${agent_pid}" ] && kill -0 "${agent_pid}" 2>/dev/null; then
    echo "Sending SIGTERM to agent process (PID: ${agent_pid})..."
    kill -TERM "${agent_pid}" 2>/dev/null || true

    # Wait up to 30 seconds for graceful shutdown
    for i in {1..30}; do
      if ! kill -0 "${agent_pid}" 2>/dev/null; then
        echo "Agent process exited gracefully."
        break
      fi
      sleep 1
    done

    # Force kill if still running
    if kill -0 "${agent_pid}" 2>/dev/null; then
      echo "Agent process did not exit gracefully, force killing..."
      kill -KILL "${agent_pid}" 2>/dev/null || true
      wait "${agent_pid}" 2>/dev/null || true
    fi
  fi

  # Deregister agent from Azure DevOps
  echo "Deregistering agent from Azure DevOps..."
  if ./config.sh remove --unattended --auth pat --token "${AZP_TOKEN}" 2>&1 | tee /tmp/cleanup.log; then
    echo "Agent ${AZP_AGENT_NAME} successfully deregistered."
  else
    echo "Warning: Agent deregistration may have failed. Check /tmp/cleanup.log for details."
    cat /tmp/cleanup.log >&2
  fi
}

# Signal handlers
handle_signal() {
  echo "Received shutdown signal, initiating cleanup..."
  shutdown_requested=1
  cleanup
  exit 0
}

trap handle_signal SIGTERM SIGINT
trap cleanup EXIT

# Configure agent
echo "Configuring agent ${AZP_AGENT_NAME}..."
./config.sh --unattended \
  --agent "${AZP_AGENT_NAME}" \
  --url "${AZP_URL}" \
  --auth pat \
  --token "${AZP_TOKEN}" \
  --pool "${AZP_POOL}" \
  --work "${AZP_WORK}" \
  --replace

# Start agent in background
echo "Starting agent ${AZP_AGENT_NAME}..."
./run.sh "$@" &
agent_pid=$!

# Wait for agent process with proper signal handling
# Use a loop instead of bare `wait` to handle signals correctly
while kill -0 "${agent_pid}" 2>/dev/null; do
  # Check every second if the process is still alive
  sleep 1 &
  wait $! 2>/dev/null || true

  # If shutdown was requested, break out of loop
  if [ "${shutdown_requested}" -eq 1 ]; then
    break
  fi
done

# If we exited the loop naturally (agent died), run cleanup
if [ "${shutdown_requested}" -eq 0 ]; then
  echo "Agent process exited unexpectedly."
  cleanup
fi
