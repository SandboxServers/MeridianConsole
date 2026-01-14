#!/usr/bin/env bash
set -euo pipefail

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
  exec gosu azp "$0" "$@"
fi

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

if [ -z "${AZP_URL:-}" ]; then
  echo "AZP_URL (or VSTS_ACCOUNT) is required." >&2
  exit 1
fi

if [ -z "${AZP_TOKEN:-}" ]; then
  echo "AZP_TOKEN (or VSTS_TOKEN) is required." >&2
  exit 1
fi

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

agent_pid=""
cleaned_up=0

cleanup() {
  if [ "${cleaned_up}" -eq 1 ]; then
    return
  fi
  cleaned_up=1
  set +e
  if [ -n "${agent_pid}" ] && kill -0 "${agent_pid}" 2>/dev/null; then
    kill -TERM "${agent_pid}" 2>/dev/null || true
    wait "${agent_pid}" 2>/dev/null || true
  fi
  ./config.sh remove --unattended --auth pat --token "${AZP_TOKEN}" >/dev/null 2>&1 || true
}

trap cleanup EXIT SIGINT SIGTERM

./config.sh --unattended \
  --agent "${AZP_AGENT_NAME}" \
  --url "${AZP_URL}" \
  --auth pat \
  --token "${AZP_TOKEN}" \
  --pool "${AZP_POOL}" \
  --work "${AZP_WORK}" \
  --replace

./run.sh "$@" &
agent_pid=$!
wait "${agent_pid}"
