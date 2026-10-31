#!/usr/bin/env bash
# Copies the posts that `pull` downloaded into Azurite, so the local backend
# and the admin list them. Run `generator pull` first, and start Azurite
# with `--skipApiVersionCheck`.
set -euo pipefail

cd "$(git rev-parse --show-toplevel)"

posts=frontend/content/posts
export AZURE_STORAGE_CONNECTION_STRING="UseDevelopmentStorage=true"

for container in published series; do
  if ! compgen -G "$posts/$container/*" > /dev/null; then
    echo "seed-azurite: no file in $posts/$container. Run 'dotnet run --project frontend/Generator -- pull --account <name>' first." >&2
    exit 1
  fi

  az storage container create --name "$container" --output none
  az storage blob upload-batch --destination "$container" --source "$posts/$container" --overwrite --output none
  echo "seed-azurite: $container done"
done
