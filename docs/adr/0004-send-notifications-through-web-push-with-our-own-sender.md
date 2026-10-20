# Send notifications through Web Push with our own sender

The Android app gets each notification through Firebase Cloud Messaging (FCM). Firebase is the
last service that ties the blog to the Gmail account of the author. The admin replaces the Android
app.

We send each notification through standard Web Push (RFC 8030) with VAPID (RFC 8292). The backend
has its own sender, written with `System.Security.Cryptography`, and uses no Web Push library. The
change that adds the Web Push sender also removes each use of Firebase from the backend.

## Considered options

- **FCM web push.** The backend has the Firebase project, the `FirebaseAdmin` sender, and the
  service account today. FCM needs no new secret and costs nothing. It keeps the Firebase account.
- **`Lib.Net.Http.WebPush`.** It implements the `aes128gcm` coding and the `vapid` scheme, under
  MIT. It has few users and no release since 2025. Our sender is small, and the RFCs give test
  vectors for it.
- **`WebPush`.** It has only the `aesgcm` coding of the drafts before RFC 8291.
- **Email from Azure Communication Services.** It needs no permission prompt and no service worker.
  ACS Email retires on 2028-09-30.

## Consequences

- The backend owns the payload encryption (RFC 8291) and the VAPID JWT. Tests check them against
  the vectors in RFC 8291 and RFC 8188.
- The VAPID private key is a new secret in Key Vault.
- The backend removes `FirebaseAdmin`, the FCM send, the register endpoint of the Android app, and
  the `deviceRegistrations` table. After that, the author can delete the Firebase project.
- The Android app gets no notifications after the switch. It can moderate until it retires.
- On iOS and iPadOS, a notification shows only in a Home Screen web app. Thus the admin has a web
  app manifest.

Detail: [Choose how a new-comment alert reaches the author](https://github.com/tw0po1nt/twopoint-website-staging/issues/196).
