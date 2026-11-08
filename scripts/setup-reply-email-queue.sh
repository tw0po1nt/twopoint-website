#!/usr/bin/env bash
#
# Creates the Service Bus queue reply_email for the backend, and sets the site URL of the reply emails
# on the Function App. Run it before the merge that deploys Queues-ProcessReplyEmailMessage. A second
# run changes nothing.

set -euo pipefail

FUNCTION_APP="func-twopoint-website-api"
QUEUE_NAME="reply_email"
SITE_URI="https://twopoint.dev/"

say()  { printf '  %s\n' "$1"; }
fail() { printf '  ✗ %s\n' "$1" >&2; exit 1; }

az account show >/dev/null 2>&1 || fail "Sign in first with 'az login'."
say "Subscription: $(az account show --query name -o tsv)"

APP_RG=$(az functionapp list --query "[?name=='$FUNCTION_APP'].resourceGroup | [0]" -o tsv)
[[ -n "$APP_RG" ]] || fail "Cannot find $FUNCTION_APP in this subscription."

app_setting() {
  az functionapp config appsettings list -n "$FUNCTION_APP" -g "$APP_RG" \
    --query "[?name=='Azure__$1' || name=='Azure:$1'].value | [0]" -o tsv
}

SERVICE_BUS_URI=$(app_setting ServiceBusUri)
[[ -n "$SERVICE_BUS_URI" ]] || fail "$FUNCTION_APP has no setting Azure__ServiceBusUri."
SB_NAMESPACE=$(printf '%s' "$SERVICE_BUS_URI" | sed -E 's#^https?://([^.]+)\..*#\1#')
SB_ID=$(az servicebus namespace list --query "[?name=='$SB_NAMESPACE'].id | [0]" -o tsv)
SB_RG=$(az servicebus namespace list --query "[?name=='$SB_NAMESPACE'].resourceGroup | [0]" -o tsv)
[[ -n "$SB_ID" ]] || fail "Cannot find the Service Bus namespace '$SB_NAMESPACE'."
say "Service Bus namespace: $SB_NAMESPACE (resource group $SB_RG)"

if az servicebus queue show -g "$SB_RG" --namespace-name "$SB_NAMESPACE" -n "$QUEUE_NAME" >/dev/null 2>&1; then
  say "✓ The queue '$QUEUE_NAME' exists."
else
  az servicebus queue create -g "$SB_RG" --namespace-name "$SB_NAMESPACE" -n "$QUEUE_NAME" >/dev/null
  say "✓ Created the queue '$QUEUE_NAME'."
fi

CURRENT_SITE_URI=$(az functionapp config appsettings list -n "$FUNCTION_APP" -g "$APP_RG" \
  --query "[?name=='ReplyEmail__SiteUri' || name=='ReplyEmail:SiteUri'].value | [0]" -o tsv)
if [[ "$CURRENT_SITE_URI" == "$SITE_URI" ]]; then
  say "✓ The setting ReplyEmail__SiteUri is $SITE_URI."
else
  az functionapp config appsettings set -n "$FUNCTION_APP" -g "$APP_RG" \
    --settings "ReplyEmail__SiteUri=$SITE_URI" >/dev/null
  say "✓ Set ReplyEmail__SiteUri to $SITE_URI."
fi

IDENTITY_CLIENT_ID=$(app_setting ManagedIdentityClientId)
IDENTITY_PRINCIPAL_ID=""
if [[ -n "$IDENTITY_CLIENT_ID" ]]; then
  IDENTITY_PRINCIPAL_ID=$(az identity list --query "[?clientId=='$IDENTITY_CLIENT_ID'].principalId | [0]" -o tsv)
fi

if [[ -z "$IDENTITY_PRINCIPAL_ID" ]]; then
  say "⚠ Cannot find the backend identity, so the roles are not checked."
  exit 0
fi

for ROLE in "Azure Service Bus Data Sender" "Azure Service Bus Data Receiver"; do
  COUNT=$(az role assignment list --assignee "$IDENTITY_PRINCIPAL_ID" --scope "$SB_ID/queues/$QUEUE_NAME" \
    --include-inherited \
    --query "[?roleDefinitionName=='$ROLE' || roleDefinitionName=='Azure Service Bus Data Owner'] | length(@)" -o tsv)
  if [[ "${COUNT:-0}" -gt 0 ]]; then
    say "✓ The backend identity has '$ROLE' on '$QUEUE_NAME'."
  else
    say "⚠ The backend identity has no '$ROLE' on '$QUEUE_NAME'. Give it the role on $SB_NAMESPACE:"
    say "  az role assignment create --assignee-object-id $IDENTITY_PRINCIPAL_ID --assignee-principal-type ServicePrincipal --role \"$ROLE\" --scope $SB_ID"
  fi
done
