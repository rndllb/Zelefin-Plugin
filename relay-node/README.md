# Zelefin push (Node)

Hosted push sender for Zelefin. Jellyfin plugins POST to `https://push.zelefin.app/v1/send`. iOS tokens (`tokens`) go to Apple Push; Android tokens (`fcmTokens`) go to Firebase Cloud Messaging HTTP v1.

This is what runs in production. Do not put a `.p8` or a Firebase service-account JSON in git. Set environment variables on the host:

| Variable | Required | Meaning |
| --- | --- | --- |
| `APNS_KEY_ID` | for iOS | Key ID from Apple Developer → Keys |
| `APNS_TEAM_ID` | for iOS | Apple Team ID |
| `APNS_BUNDLE_ID` | no | Defaults to `app.zelefin.client` |
| `APNS_PRIVATE_KEY` | for iOS | PEM contents. `\n` is fine for newlines |
| `APNS_PRODUCTION` | no | `true` to try production APNs first. Sandbox is still tried |
| `FCM_SERVICE_ACCOUNT` | for Android | Firebase service-account JSON (raw or base64) |
| `FCM_SERVICE_ACCOUNT_FILE` | for Android | Path to that JSON, when the host mounts secrets as files |
| `FCM_PROJECT_ID` | no | Overrides `project_id` from the service account |
| `FCM_CHANNEL_ID` | no | Android notification channel. Defaults to `zelefin_alerts`, which the app creates |

Firebase service account: Firebase console → Project settings → Service accounts → Generate new private key. The Android app's `google-services.json` must come from the same Firebase project.

## Emby

Emby cannot run the Zelefin plugin, so the app registers each phone here and adds the returned address to the user's notifications in Emby (Webhooks).

| Route | Meaning |
| --- | --- |
| `POST /emby/v1/hooks` | `{ "platform": "ios" or "android", "token" }` → `{ "hookId", "url" }` |
| `PUT /emby/v1/hooks/:hookId` | Replace the phone's push token |
| `DELETE /emby/v1/hooks/:hookId` | Forget the phone |
| `POST /emby/v1/hook/:hookId` | Emby's webhook. JSON or multipart (`data` field). The event's `Title` becomes the alert, with the server name as its title |

## Silo

Silo's own push only reaches the Silo app, so Zelefin uses a personal webhook on the user's Silo profile instead. The routes match Emby's under `/silo/v1/`. Registration also takes an optional `label` (shown as the alert title), and `PUT /silo/v1/hooks/:hookId` takes the webhook's `signingSecret` once Silo has created it.

`POST /silo/v1/hook/:hookId` checks `X-Silo-Signature` (`t=…,v1=…`, HMAC-SHA256 of `"<t>.<body>"`) against the stored secret and refuses deliveries signed more than ten minutes ago. New episodes and request updates become alerts. Silo's native ids ride as `siloItemId` because they are not the Jellyfin-compatible ids the app browses with.

The hook id is random and is the only secret in the address. Registrations live in `~/.zelefin-push/emby-hooks.json` and `silo-hooks.json`, outside the app folder so a redeploy keeps them. Set `ZELEFIN_PUSH_DATA_DIR` to move it, and `PUBLIC_URL` if the service is not at `https://push.zelefin.app`. A phone gets at most 30 alerts a minute.

`GET /healthz` returns `{ "ok": true, "apns": true, "fcm": true }` when at least one sender loaded. The process must bind `process.env.PORT`.
