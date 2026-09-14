# Zelefin notifications

Install the plugin from Catalog using this repository, turn on the events you want, then sign in to Zelefin and allow alerts. You do not need an Apple Developer account.

```
https://raw.githubusercontent.com/rndllb/Zelefin-Plugin/main/manifest.json
```

The plugin posts device tokens to Zelefin’s push service (`https://push.zelefin.app/v1/send`). Apple credentials stay with the app publisher. They are not pasted into this dashboard and they are not baked into the plugin DLL.

1. Dashboard → Plugins → Zelefin → Notifications. Leave the event toggles on (they default on).
2. Open Zelefin, sign in, allow notifications.
3. Click **Send a test alert to my iPhone**.

After that you only touch the notification toggles.

Built-in events:

- **Item added** — every Zelefin user. Movies send immediately. Episodes from the same season that arrive within the grouping window become one alert.
- **Session started** — admins, excluding the user who just connected.
- **Playback started** — admins, excluding whoever started playback.
- **User locked out** — admins and the locked user.

Zelefin registers a device token after sign-in. Alerts leave this server through the publisher relay.

If this Jellyfin already has an Apple Push key saved from an earlier plugin version, that local key is still used. New installs do not see those fields.

## Custom webhook

`POST /Zelefin/notification`

Headers:

```
Content-Type: application/json
Authorization: MediaBrowser Token="{apiKey}"
```

Create an API key under Dashboard → API Keys.

```json
[
  {
    "title": "string",
    "subtitle": "string",
    "body": "string",
    "userId": "jellyfin-user-guid",
    "username": "jellyfin-username",
    "isAdmin": false,
    "itemId": "optional-item-guid",
    "seriesId": "optional-series-guid"
  }
]
```

`title` and `body` are required unless `title` is omitted and `body` is set. Empty targeting sends to every registered device. `isAdmin` also sends to administrator devices. `itemId` opens that title in Zelefin.

### Notify everyone

```json
[
  {
    "title": "Library updated",
    "body": "New movies are ready"
  }
]
```

### Jellyfin webhook plugin

Add a generic destination. URL is the endpoint above. Example for Item Added if you prefer the webhook plugin over the built-in event:

```json
[
  {
    {{#if_equals ItemType 'Movie'}}
      "title": "{{{Name}}} ({{Year}}) added",
      "body": "Watch movie now",
      "itemId": "{{ItemId}}"
    {{/if_equals}}
    {{#if_equals ItemType 'Episode'}}
      "title": "{{{SeriesName}}} S{{SeasonNumber00}}E{{EpisodeNumber00}} added",
      "body": "Watch episode '{{{Name}}}' now",
      "itemId": "{{ItemId}}"
    {{/if_equals}}
  }
]
```

### Seerr

Settings → Notifications → Webhook. Enable the agent. Webhook URL is the endpoint above.

Issues (admins only):

```json
[
  {
    "title": "{{event}}",
    "body": "{{subject}}: {{message}}",
    "isAdmin": true
  }
]
```

Media available (everyone):

```json
[
  {
    "title": "{{subject}}",
    "body": "{{message}}"
  }
]
```
