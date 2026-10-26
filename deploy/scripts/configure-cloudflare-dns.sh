#!/usr/bin/env bash
# Creates or updates the Cloudflare DNS records for one Meridian Console environment.
#
# Usage: configure-cloudflare-dns.sh <dev|staging|prod> <ipv4>
# Env:   CLOUDFLARE_API_TOKEN  token with DNS:Edit on the zone
#        CLOUDFLARE_ZONE_ID    zone ID of meridianconsole.com
#
# Run by .github/workflows/infra.yml. Exits non-zero if any record fails.
set -euo pipefail

ENVIRONMENT="${1:?usage: $0 <dev|staging|prod> <ipv4>}"
IP="${2:?usage: $0 <dev|staging|prod> <ipv4>}"
: "${CLOUDFLARE_API_TOKEN:?CLOUDFLARE_API_TOKEN is not set}"
: "${CLOUDFLARE_ZONE_ID:?CLOUDFLARE_ZONE_ID is not set}"

DOMAIN=meridianconsole.com
API="https://api.cloudflare.com/client/v4/zones/${CLOUDFLARE_ZONE_ID}/dns_records"

octet='(25[0-5]|2[0-4][0-9]|1[0-9]{2}|[1-9]?[0-9])'
if ! [[ "$IP" =~ ^$octet\.$octet\.$octet\.$octet$ ]]; then
  echo "::error::'$IP' is not an IPv4 address" >&2
  exit 1
fi

cf() {
  curl -sS --retry 3 -H "Authorization: Bearer ${CLOUDFLARE_API_TOKEN}" \
    -H 'Content-Type: application/json' "$@"
}

failures=0

# configure_record <type> <name> <content>  (always proxied through Cloudflare)
configure_record() {
  local type="$1" name="$2" content="$3" body existing result
  body=$(jq -nc --arg type "$type" --arg name "$name" --arg content "$content" \
    '{type: $type, name: $name, content: $content, ttl: 1, proxied: true}')

  existing=$(cf -G "$API" --data-urlencode "type=$type" --data-urlencode "name=$name" \
    | jq -r '.result[0].id // empty')

  if [ -n "$existing" ]; then
    result=$(cf -X PUT "$API/$existing" --data "$body")
    action=Updated
  else
    result=$(cf -X POST "$API" --data "$body")
    action=Created
  fi

  if [ "$(jq -r '.success' <<< "$result")" = true ]; then
    echo "$action $type $name -> $content"
  else
    echo "::error::$type $name: $(jq -r '.errors[0].message // "unknown error"' <<< "$result")"
    failures=$((failures + 1))
  fi
}

case "$ENVIRONMENT" in
  dev | staging)
    apex="$ENVIRONMENT.$DOMAIN"
    configure_record A "$apex" "$IP"
    configure_record CNAME "*.$apex" "$apex"
    configure_record CNAME "api.$apex" "$apex"
    ;;
  prod)
    configure_record A "$DOMAIN" "$IP"
    configure_record CNAME "www.$DOMAIN" "$DOMAIN"
    configure_record CNAME "*.$DOMAIN" "$DOMAIN"
    configure_record CNAME "api.$DOMAIN" "$DOMAIN"
    ;;
  *)
    echo "::error::Unknown environment '$ENVIRONMENT' (expected dev, staging or prod)" >&2
    exit 1
    ;;
esac

if [ "$failures" -gt 0 ]; then
  echo "::error::$failures DNS record(s) failed for $ENVIRONMENT"
  exit 1
fi
echo "DNS configured for $ENVIRONMENT."
