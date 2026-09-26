import crypto from "node:crypto";
import fs from "node:fs";
import http2 from "node:http2";
import express from "express";

const PORT = Number(process.env.PORT) || 3000;
const BUNDLE_ID = (process.env.APNS_BUNDLE_ID || "app.zelefin.client").trim();

function credentials() {
  const keyId = (process.env.APNS_KEY_ID || "").trim();
  const teamId = (process.env.APNS_TEAM_ID || "").trim();
  const pem = (process.env.APNS_PRIVATE_KEY || "").replace(/\\n/g, "\n");
  if (!keyId || !teamId || !pem.includes("BEGIN")) {
    return null;
  }
  return {
    keyId,
    teamId,
    bundleId: BUNDLE_ID,
    pem,
    preferProduction: String(process.env.APNS_PRODUCTION || "").toLowerCase() === "true",
  };
}

function b64url(data) {
  return Buffer.from(data)
    .toString("base64")
    .replace(/=+$/g, "")
    .replace(/\+/g, "-")
    .replace(/\//g, "_");
}

function jwtFor(creds) {
  const header = b64url(JSON.stringify({ alg: "ES256", kid: creds.keyId }));
  const iat = Math.floor(Date.now() / 1000);
  const payload = b64url(JSON.stringify({ iss: creds.teamId, iat }));
  const unsigned = `${header}.${payload}`;
  const key = crypto.createPrivateKey(creds.pem);
  const signature = crypto.sign("sha256", Buffer.from(unsigned), {
    key,
    dsaEncoding: "ieee-p1363",
  });
  return `${unsigned}.${b64url(signature)}`;
}

function payloadFor(message) {
  const alert = { title: message.title, body: message.body };
  if (message.subtitle) {
    alert.subtitle = message.subtitle;
  }
  return JSON.stringify({
    aps: { sound: "default", alert },
    itemId: message.itemId || null,
    seriesId: message.seriesId || null,
    type: message.type || null,
  });
}

function hosts(preferProduction) {
  return preferProduction ? [true, false] : [false, true];
}

function isBadDeviceToken(status, body) {
  return status === 400 && String(body).includes("BadDeviceToken");
}

function tokenIsDead(status, body) {
  return status === 410 || String(body).includes("Unregistered");
}

function sendOne(host, creds, token, body, authorization) {
  return new Promise((resolve, reject) => {
    const client = http2.connect(`https://${host}`);
    client.on("error", reject);
    const pathToken = String(token).trim().replace(/ /g, "");
    const request = client.request({
      ":method": "POST",
      ":path": `/3/device/${pathToken}`,
      authorization: `bearer ${authorization}`,
      "apns-topic": creds.bundleId,
      "apns-push-type": "alert",
      "apns-priority": "10",
      "content-type": "application/json",
    });
    let data = "";
    request.setEncoding("utf8");
    request.on("response", (headers) => {
      request.on("data", (chunk) => {
        data += chunk;
      });
      request.on("end", () => {
        client.close();
        const status = Number(headers[":status"] || 0);
        resolve({ status, body: data, ok: status >= 200 && status < 300 });
      });
    });
    request.on("error", (error) => {
      client.close();
      reject(error);
    });
    request.end(body);
  });
}

const FCM_SCOPE = "https://www.googleapis.com/auth/firebase.messaging";
const FCM_CHANNEL_ID = (process.env.FCM_CHANNEL_ID || "zelefin_alerts").trim();

function fcmAccount() {
  let raw = (process.env.FCM_SERVICE_ACCOUNT || "").trim();
  const file = (process.env.FCM_SERVICE_ACCOUNT_FILE || "").trim();
  if (!raw && file) {
    try {
      raw = fs.readFileSync(file, "utf8");
    } catch {
      return null;
    }
  }
  if (!raw) {
    return null;
  }
  try {
    const json = JSON.parse(raw.startsWith("{") ? raw : Buffer.from(raw, "base64").toString("utf8"));
    const privateKey = String(json.private_key || "").replace(/\\n/g, "\n");
    if (!json.client_email || !json.project_id || !privateKey.includes("BEGIN")) {
      return null;
    }
    return {
      clientEmail: json.client_email,
      projectId: (process.env.FCM_PROJECT_ID || json.project_id).trim(),
      privateKey,
      tokenUri: json.token_uri || "https://oauth2.googleapis.com/token",
    };
  } catch {
    return null;
  }
}

let fcmAccessToken = { value: "", expiresAt: 0, clientEmail: "" };

async function fcmAuthorization(account) {
  const now = Math.floor(Date.now() / 1000);
  if (fcmAccessToken.value && fcmAccessToken.clientEmail === account.clientEmail && fcmAccessToken.expiresAt - 120 > now) {
    return fcmAccessToken.value;
  }
  const header = b64url(JSON.stringify({ alg: "RS256", typ: "JWT" }));
  const claims = b64url(JSON.stringify({
    iss: account.clientEmail,
    scope: FCM_SCOPE,
    aud: account.tokenUri,
    iat: now,
    exp: now + 3600,
  }));
  const unsigned = `${header}.${claims}`;
  const signature = crypto.sign("sha256", Buffer.from(unsigned), crypto.createPrivateKey(account.privateKey));
  const response = await fetch(account.tokenUri, {
    method: "POST",
    headers: { "content-type": "application/x-www-form-urlencoded" },
    body: new URLSearchParams({
      grant_type: "urn:ietf:params:oauth:grant-type:jwt-bearer",
      assertion: `${unsigned}.${b64url(signature)}`,
    }),
  });
  const json = await response.json().catch(() => ({}));
  if (!response.ok || !json.access_token) {
    throw new Error(`Google OAuth returned ${response.status}`);
  }
  fcmAccessToken = {
    value: json.access_token,
    expiresAt: now + Number(json.expires_in || 3600),
    clientEmail: account.clientEmail,
  };
  return fcmAccessToken.value;
}

// Notification + data message: Android draws it while Zelefin is closed, and a tap
// hands the data keys to the launcher activity as intent extras.
function fcmMessageFor(token, message) {
  const data = { title: message.title, body: message.body };
  for (const key of ["subtitle", "itemId", "seriesId", "type"]) {
    if (message[key]) {
      data[key] = message[key];
    }
  }
  const notification = { channel_id: FCM_CHANNEL_ID, sound: "default" };
  if (message.itemId) {
    notification.tag = message.itemId;
  }
  return {
    message: {
      token,
      notification: {
        title: message.title,
        body: message.subtitle ? `${message.subtitle} · ${message.body}` : message.body,
      },
      data,
      android: { priority: "HIGH", notification },
    },
  };
}

function fcmTokenIsDead(status, body) {
  const text = JSON.stringify(body || {});
  if (status === 404 || text.includes("UNREGISTERED")) {
    return true;
  }
  return status === 400 && text.includes("INVALID_ARGUMENT") && /registration token/i.test(text);
}

async function sendFcmToken(account, token, message, authorization) {
  const response = await fetch(`https://fcm.googleapis.com/v1/projects/${encodeURIComponent(account.projectId)}/messages:send`, {
    method: "POST",
    headers: {
      authorization: `Bearer ${authorization}`,
      "content-type": "application/json",
    },
    body: JSON.stringify(fcmMessageFor(token, message)),
  });
  const body = await response.json().catch(() => ({}));
  return { status: response.status, ok: response.ok, expired: !response.ok && fcmTokenIsDead(response.status, body) };
}

async function sendToken(creds, token, message, authorization) {
  const json = payloadFor(message);
  let last = { status: 0, body: "", ok: false };
  for (const production of hosts(creds.preferProduction)) {
    const host = production ? "api.push.apple.com" : "api.sandbox.push.apple.com";
    last = await sendOne(host, creds, token, json, authorization);
    if (last.ok) {
      return { ...last, expired: false };
    }
    if (last.status >= 500 || !isBadDeviceToken(last.status, last.body)) {
      break;
    }
  }
  return { ...last, expired: tokenIsDead(last.status, last.body) || isBadDeviceToken(last.status, last.body) };
}

const app = express();
app.disable("x-powered-by");
app.use(express.json({ limit: "256kb" }));

app.get("/healthz", (_req, res) => {
  const apns = Boolean(credentials());
  const fcm = Boolean(fcmAccount());
  if (!apns && !fcm) {
    res.status(503).json({ ok: false, apns, fcm, error: "APNs and Firebase are not configured" });
    return;
  }
  res.json({ ok: true, apns, fcm });
});

app.get("/", (_req, res) => {
  res.type("text/plain").send("Zelefin push");
});

function tokenList(value) {
  return [...new Set((Array.isArray(value) ? value : []).map((token) => String(token).trim()).filter(Boolean))].slice(0, 200);
}

app.post("/v1/send", async (req, res) => {
  const tokens = tokenList(req.body?.tokens);
  const fcmTokens = tokenList(req.body?.fcmTokens);
  const body = String(req.body?.body || "").trim();
  if ((tokens.length === 0 && fcmTokens.length === 0) || !body) {
    res.status(400).json({ error: "tokens or fcmTokens, and body, are required" });
    return;
  }
  const creds = tokens.length > 0 ? credentials() : null;
  const account = fcmTokens.length > 0 ? fcmAccount() : null;
  if (!creds && !account) {
    res.status(503).json({ error: tokens.length > 0 ? "APNs is not configured" : "Firebase is not configured" });
    return;
  }
  const message = {
    title: String(req.body.title || "Zelefin").trim() || "Zelefin",
    subtitle: req.body.subtitle ? String(req.body.subtitle).trim() : "",
    body,
    itemId: req.body.itemId ? String(req.body.itemId).trim() : "",
    seriesId: req.body.seriesId ? String(req.body.seriesId).trim() : "",
    type: req.body.type ? String(req.body.type).trim() : "",
  };
  const expiredTokens = [];
  if (creds) {
    const authorization = jwtFor(creds);
    for (const token of tokens) {
      try {
        const result = await sendToken(creds, token, message, authorization);
        if (result.expired) {
          expiredTokens.push(token);
        }
      } catch {
        // Keep going; one bad token must not drop the rest.
      }
    }
  }
  if (account) {
    let authorization = "";
    try {
      authorization = await fcmAuthorization(account);
    } catch (error) {
      console.warn(`fcm auth failed: ${error.message}`);
    }
    if (authorization) {
      for (const token of fcmTokens) {
        try {
          const result = await sendFcmToken(account, token, message, authorization);
          if (result.expired) {
            expiredTokens.push(token);
          }
        } catch {
          // Keep going; one bad token must not drop the rest.
        }
      }
    }
  }
  res.json({ expiredTokens });
});

app.listen(PORT, "0.0.0.0", () => {
  console.log(`zelefin-push on ${PORT} apns=${Boolean(credentials())} fcm=${Boolean(fcmAccount())}`);
});
