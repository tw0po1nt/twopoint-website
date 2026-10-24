# Web Push setup

This checklist makes the VAPID key pair for the Web Push sender. The private key goes to Key Vault.
The public key goes to the backend config. Do these steps once for the deployed backend.

The local backend has its own key pair in `backend/Http/local.settings.json.template`. Copy the
`WebPush` section to `local.settings.json`. Change `Subject` to your `mailto:` address.

## Make the key pair

1. Make a P-256 key in a temporary folder:

   ```bash
   openssl ecparam -genkey -name prime256v1 -noout -out vapid.pem
   openssl ec -in vapid.pem -outform DER | xxd -p -c 200 > vapid.hex
   ```

2. Cut the two keys from the hex and encode each one in base64url with no padding:

   ```bash
   b64url() { xxd -r -p | base64 | tr '+/' '-_' | tr -d '=\n'; }
   H=$(cat vapid.hex)
   PRIVATE_KEY=$(echo "${H:14:64}" | b64url)
   PUBLIC_KEY=$(echo "${H#*034200}" | b64url)
   ```

   `PRIVATE_KEY` has 43 characters. `PUBLIC_KEY` has 87 characters and starts with `B`.

## Give the keys to the backend

1. Write the private key to Key Vault as the secret `web-push-vapid-private-key`:

   ```bash
   az keyvault secret set --vault-name <vault> --name web-push-vapid-private-key --value "$PRIVATE_KEY"
   ```

2. Check that the backend identity has the role `Key Vault Secrets User` on the vault.
3. Write the settings to the Function App:

   ```bash
   az functionapp config appsettings set -n func-twopoint-website-api -g <resource-group> --settings \
     "WebPush__Subject=mailto:<your address>" \
     "WebPush__PublicKey=$PUBLIC_KEY" \
     "WebPush__PrivateKeyFromKeyVault=web-push-vapid-private-key"
   ```

4. Write `PUBLIC_KEY` to `vapidPublicKey` in the `release` section of `frontend/Admin/config.json`.
   The admin sends it to the browser when the author turns on notifications. With an empty value,
   the Settings page says that the build cannot turn on notifications. The `dev` section has the
   key pair of the local backend.
5. Delete `vapid.pem` and `vapid.hex`.

A new key pair makes each registered device useless. Each device must subscribe again.
