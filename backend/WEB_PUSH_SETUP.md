# Web Push setup

The Web Push sender signs each push with a VAPID key pair. Do this setup once for the deployed
backend.

## The deployed backend

Run the wizard from the repo root:

```bash
scripts/setup-web-push.sh
```

The wizard asks before each change. It does these steps:

1. It finds the Function App, the Key Vault, and the backend identity.
2. It makes a P-256 key pair in a temporary folder. If the Function App already has a public key,
   it asks first. A new key pair makes each registered device useless, and each device must turn
   on notifications again.
3. It writes the private key to Key Vault as the secret `web-push-vapid-private-key`.
4. It gives the backend identity read access to the secret, if the identity does not have it.
5. It writes `WebPush__Subject`, `WebPush__PublicKey`, and `WebPush__PrivateKeyFromKeyVault` to
   the Function App. The subject is a `mailto:` address.
6. It writes the public key to `vapidPublicKey` in the `release` section of
   `frontend/Admin/config.json`.

Then commit `frontend/Admin/config.json` and merge it. The admin sends the public key to the
browser when the author turns on notifications. With an empty value, the Settings page says that
the build cannot turn on notifications.

## The local backend

The local backend has its own key pair in `backend/Http/local.settings.json.template`. Copy the
`WebPush` section to `local.settings.json`. Change `Subject` to your `mailto:` address. The `dev`
section of `frontend/Admin/config.json` has the public key of this pair.

## The new_comment queue

`Blog-Posts-PostComment` puts one message on the Service Bus queue `new_comment` for each new
comment. The function `Queues-ProcessNewCommentMessage` reads the queue and sends the push. A merge
deploys the function, so create the queue first:

```bash
az servicebus queue create -g <resource-group> --namespace-name <namespace> -n new_comment
```

The backend identity needs no new role. It already sends to the namespace and reads the other
queues. The Function App setting `TwoPointWebsiteServiceBus` already names the namespace.
