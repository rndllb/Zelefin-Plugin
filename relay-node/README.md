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

`GET /healthz` returns `{ "ok": true, "apns": true, "fcm": true }` when at least one sender loaded. The process must bind `process.env.PORT`.
