import crypto from "node:crypto";
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
  if (!credentials()) {
    res.status(503).json({ ok: false, error: "APNs is not configured" });
    return;
  }
  res.json({ ok: true });
});

app.get("/", (_req, res) => {
  res.type("text/plain").send("Zelefin push");
});

app.post("/v1/send", async (req, res) => {
  const creds = credentials();
  if (!creds) {
    res.status(503).json({ error: "APNs is not configured" });
    return;
  }
  const tokens = [...new Set((req.body?.tokens || []).map((token) => String(token).trim()).filter(Boolean))].slice(0, 200);
  const body = String(req.body?.body || "").trim();
  if (tokens.length === 0 || !body) {
    res.status(400).json({ error: "tokens and body are required" });
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
  const authorization = jwtFor(creds);
  const expiredTokens = [];
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
  res.json({ expiredTokens });
});

app.listen(PORT, "0.0.0.0", () => {
  console.log(`zelefin-push on ${PORT} apns=${Boolean(credentials())}`);
});
