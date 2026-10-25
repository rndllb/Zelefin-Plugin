import crypto from "node:crypto";

const MAX_AGE_SECONDS = 600;

/**
 * Checks Silo's `X-Silo-Signature` (`t=<epoch>,v1=<hex hmac-sha256 of "<epoch>.<body>">`) against the
 * literal bytes received. Deliveries signed more than ten minutes ago are refused, so a copied
 * request cannot be replayed later.
 */
export function verifySiloSignature(secret, header, rawBody, now = Date.now()) {
  if (!secret || !header || !rawBody) {
    return false;
  }
  const parts = Object.fromEntries(
    String(header)
      .split(",")
      .map((part) => part.trim().split("="))
      .filter((pair) => pair.length === 2),
  );
  const timestamp = Number(parts.t);
  if (!Number.isFinite(timestamp) || Math.abs(now / 1000 - timestamp) > MAX_AGE_SECONDS || !/^[0-9a-f]{64}$/.test(parts.v1 || "")) {
    return false;
  }
  const expected = crypto.createHmac("sha256", secret).update(`${parts.t}.`).update(rawBody).digest();
  return crypto.timingSafeEqual(expected, Buffer.from(parts.v1, "hex"));
}

/** Turns a Silo webhook body into the alert the phone shows, or null when there is nothing to say. */
export function siloMessage(event, label) {
  const title = clip(String(label || "").trim() || "Silo", 60);
  if (event?.test) {
    return message(title, "Zelefin notifications are on for this Silo profile.", event);
  }
  const series = String(event?.series?.title || "").trim();
  const request = String(event?.request?.title || "").trim() || series;
  switch (event?.type) {
    case "episode.available": {
      const episode = event.episode || {};
      const number = episodeNumber(episode.season_number, episode.episode_number);
      const name = String(episode.title || "").trim();
      const detail = [number, name].filter(Boolean).join(" · ");
      if (!series && !detail) {
        return null;
      }
      return message(title, series ? `New episode of ${series}${detail ? `: ${detail}` : ""}` : `New episode: ${detail}`, event);
    }
    case "request.fulfilled":
      return request ? message(title, `Your request for ${request} is ready to watch.`, event) : null;
    case "request.approved":
      return request ? message(title, `Your request for ${request} was approved.`, event) : null;
    case "request.declined": {
      if (!request) {
        return null;
      }
      const reason = String(event.request?.reason || "").trim();
      return message(title, `Your request for ${request} was declined${reason ? `: ${clip(reason, 120)}` : "."}`, event);
    }
    default:
      return null;
  }
}

// Silo's ids are its native catalog ids, not the Jellyfin-compatible ids Zelefin browses with, so
// they ride as siloItemId and siloSeriesId rather than itemId.
function message(title, body, event) {
  return {
    title,
    subtitle: "",
    body: clip(body, 240),
    itemId: "",
    seriesId: "",
    siloItemId: String(event?.episode?.id || event?.series?.id || ""),
    siloSeriesId: String(event?.series?.id || ""),
    type: String(event?.type || (event?.test ? "test" : "")),
  };
}

function episodeNumber(season, episode) {
  if (Number.isInteger(season) && Number.isInteger(episode)) {
    return `S${season} E${episode}`;
  }
  return Number.isInteger(episode) ? `E${episode}` : "";
}

function clip(text, limit) {
  return text.length > limit ? `${text.slice(0, limit - 1)}…` : text;
}
