#!/usr/bin/env sh
# One-time setup of the local Garage container (see docker-compose.yml): assigns the single-node
# layout, imports the fixed development access key, creates the "ciir-uploads" bucket and grants
# the key access to it. Safe to re-run - each step is skipped when already done.
#
# Usage: .eng/docker/garage/init.sh [container-name]
set -eu

container="${1:-ciir-indexer-garage}"
bucket="ciir-uploads"
# Throwaway development credentials - they match the defaults in appsettings.json.
key_id="GK000000000000000000000000"
secret_key="0000000000000000000000000000000000000000000000000000000000000000"

garage() { docker exec "$container" /garage "$@"; }

node_id="$(garage node id -q | cut -d@ -f1)"

if garage layout show 2>/dev/null | grep -q "$(echo "$node_id" | cut -c1-16)"; then
  echo "layout: already assigned"
else
  garage layout assign -z dc1 -c 1G "$node_id"
  garage layout apply --version 1
fi

if garage key info "$key_id" >/dev/null 2>&1; then
  echo "key: already imported"
else
  garage key import --yes -n ciir-indexer-dev "$key_id" "$secret_key"
fi

if garage bucket info "$bucket" >/dev/null 2>&1; then
  echo "bucket: already created"
else
  garage bucket create "$bucket"
fi

garage bucket allow --read --write --owner "$bucket" --key "$key_id"
echo "Garage ready: bucket '$bucket', access key '$key_id'"
