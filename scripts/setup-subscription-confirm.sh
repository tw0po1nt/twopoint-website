#!/usr/bin/env bash
#
# Sets the URL of the confirm page on the Function App. Each confirmation email links to that page.
# Run it before the merge that deploys the confirmation. Without the setting, each subscribe request
# gets 500. A second run changes nothing.

set -euo pipefail

FUNCTION_APP="func-twopoint-website-api"
CONFIRM_URI="https://twopoint.dev/subscribe/confirm/"

say()  { printf '  %s\n' "$1"; }
fail() { printf '  ✗ %s\n' "$1" >&2; exit 1; }

az account show >/dev/null 2>&1 || fail "Sign in first with 'az login'."
say "Subscription: $(az account show --query name -o tsv)"

APP_RG=$(az functionapp list --query "[?name=='$FUNCTION_APP'].resourceGroup | [0]" -o tsv)
[[ -n "$APP_RG" ]] || fail "Cannot find $FUNCTION_APP in this subscription."

CURRENT_URI=$(az functionapp config appsettings list -n "$FUNCTION_APP" -g "$APP_RG" \
  --query "[?name=='Subscription__ConfirmUri' || name=='Subscription:ConfirmUri'].value | [0]" -o tsv)
if [[ "$CURRENT_URI" == "$CONFIRM_URI" ]]; then
  say "✓ The setting Subscription__ConfirmUri is $CONFIRM_URI."
else
  az functionapp config appsettings set -n "$FUNCTION_APP" -g "$APP_RG" \
    --settings "Subscription__ConfirmUri=$CONFIRM_URI" >/dev/null
  say "✓ Set Subscription__ConfirmUri to $CONFIRM_URI."
fi
