const ITEM_EVENTS = /^(library|playback|item)\./;

/** Turns an Emby webhook body into the alert the phone shows, or null when there is nothing to say. */
export function embyMessage(event) {
  const kind = String(event?.Event || "").trim();
  const server = String(event?.Server?.Name || "").trim() || "Emby";
  if (kind === "system.notificationtest") {
    return { title: server, subtitle: "", body: "Zelefin notifications are on for this server.", itemId: "", seriesId: "", type: kind };
  }
  const body = clip(String(event?.Title || "").trim() || String(event?.Description || "").trim(), 240);
  if (!body) {
    return null;
  }
  const item = event?.Item || {};
  return {
    title: server,
    subtitle: "",
    body,
    itemId: ITEM_EVENTS.test(kind) && item.Id ? String(item.Id) : "",
    seriesId: ITEM_EVENTS.test(kind) && item.SeriesId ? String(item.SeriesId) : "",
    type: kind,
  };
}

function clip(text, limit) {
  return text.length > limit ? `${text.slice(0, limit - 1)}…` : text;
}
