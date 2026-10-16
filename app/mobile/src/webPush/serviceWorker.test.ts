import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { runInNewContext } from "node:vm";
import { describe, expect, it } from "vitest";

type Listener = (event: Record<string, unknown>) => void;

const SOURCE = readFileSync(resolve(__dirname, "../../public/sw.js"), "utf8");
const SETTINGS_URL = "/__cichlids/web-push-settings";
const ORIGIN = "https://cichlids.test";
const payload = { tankId: 7, tankTitle: "Malawi 240", kind: "water_change", dueDate: "2026-10-16", url: "/care?tank=7" };

/** Runs the service worker script against a fake worker scope and returns what it did. */
function load({ settings, maxActions = 2, windows = [] as { url: string; focused: string[]; navigated: string[] }[], fetchImpl }: {
  settings?: object; maxActions?: number; windows?: { url: string; focused: string[]; navigated: string[] }[];
  fetchImpl?: (url: string, init?: RequestInit) => Promise<Response>;
}) {
  const listeners = new Map<string, Listener>();
  const shown: { title: string; options: Record<string, unknown> }[] = [];
  const opened: string[] = [];
  const fetched: { url: string; init?: RequestInit }[] = [];
  const stored = new Map<string, string>();
  if (settings) stored.set(SETTINGS_URL, JSON.stringify(settings));
  const scope = {
    addEventListener: (type: string, listener: Listener) => listeners.set(type, listener),
    location: { origin: ORIGIN },
    registration: { showNotification: async (title: string, options: Record<string, unknown>) => { shown.push({ title, options }); } },
    skipWaiting: async () => {},
    clients: {
      claim: async () => {},
      matchAll: async () => windows.map(w => ({
        url: w.url, focus: async () => { w.focused.push(w.url); }, navigate: async (target: string) => { w.navigated.push(target); },
      })),
      openWindow: async (url: string) => { opened.push(url); },
    },
    caches: { open: async () => ({ match: async (key: string) => stored.has(key) ? new Response(stored.get(key)) : undefined }) },
    Notification: { maxActions },
    Intl,
    URL,
    Response,
    JSON,
    fetch: async (url: string, init?: RequestInit) => {
      fetched.push({ url, init });
      return fetchImpl ? fetchImpl(url, init) : new Response("{}", { status: 404 });
    },
  };
  runInNewContext(SOURCE, { ...scope, self: scope, caches: scope.caches, Notification: scope.Notification, clients: scope.clients });
  const dispatch = async (type: string, event: Record<string, unknown>) => {
    const pending: Promise<unknown>[] = [];
    listeners.get(type)?.({ ...event, waitUntil: (promise: Promise<unknown>) => pending.push(promise) });
    await Promise.all(pending);
  };
  return { shown, opened, fetched, dispatch, listeners };
}

const pushEvent = (data: unknown) => ({ data: { json: () => { if (data === undefined) throw new SyntaxError("no json"); return data; } } });
const clickEvent = (action: string, data: object = payload) => ({ action, notification: { data, closed: false, close() { this.closed = true; } } });

describe("the service worker of the web app", () => {
  it("shows a reminder with the tank title and the text of the member's language", async () => {
    const worker = load({ settings: { apiUrl: ORIGIN, texts: { water_change: "Teilwechsel ist heute fällig.", measurement: "Messung", snooze: "Morgen erinnern" } } });
    await worker.dispatch("push", pushEvent(payload));
    expect(worker.shown).toHaveLength(1);
    expect(worker.shown[0].title).toBe("Malawi 240");
    expect(worker.shown[0].options.body).toBe("Teilwechsel ist heute fällig.");
    expect(worker.shown[0].options.actions).toEqual([{ action: "snooze", title: "Morgen erinnern" }]);
    expect(worker.shown[0].options.tag).toBe("care-reminder:7:water_change");
    expect(worker.shown[0].options.data).toEqual(payload);
  });

  describe("a reminder of a running streak", () => {
    const streak = (daysLeft: number) => ({ ...payload, daysLeft });
    const settings = { apiUrl: ORIGIN, texts: { water_change: "x", measurement: "y", snooze: "Morgen erinnern", streakToday: "Heute reißt die Serie ab.",
      streakBreaks: { "3": "In 3 Tagen reißt die Serie ab." } } };

    it("shows the text for the days left, or the text for the last day", async () => {
      const worker = load({ settings });
      await worker.dispatch("push", pushEvent(streak(3)));
      await worker.dispatch("push", pushEvent(streak(0)));
      expect(worker.shown.map(n => n.options.body)).toEqual(["In 3 Tagen reißt die Serie ab.", "Heute reißt die Serie ab."]);
      expect(worker.shown[0].options.data).toEqual(streak(3));
    });

    it("offers the snooze except on the last day, when no reminder follows", async () => {
      const worker = load({ settings });
      await worker.dispatch("push", pushEvent(streak(2)));
      await worker.dispatch("push", pushEvent(streak(0)));
      expect(worker.shown[0].options.actions).toEqual([{ action: "snooze", title: "Morgen erinnern" }]);
      expect(worker.shown[1].options.actions).toBeUndefined();
    });

    it("dismisses the reminder on snooze without a request, since the next day brings the next reminder", async () => {
      const worker = load({ settings });
      await worker.dispatch("notificationclick", clickEvent("snooze", streak(2)));
      expect(worker.fetched).toEqual([]);
      expect(worker.opened).toEqual([]);
    });

    it("falls back to English when the page stored no text for the days left", async () => {
      const worker = load({});
      await worker.dispatch("push", pushEvent(streak(2)));
      await worker.dispatch("push", pushEvent(streak(1)));
      await worker.dispatch("push", pushEvent(streak(0)));
      expect(worker.shown.map(n => n.options.body)).toEqual([
        "Your water change streak breaks in 2 days. Do the water change or enter a vacation if you are away.",
        "Your water change streak breaks in 1 day. Do the water change or enter a vacation if you are away.",
        "Your water change streak breaks today. Do the water change now or enter a vacation if you are away.",
      ]);
    });

    it("ignores a message whose days left are not a number of days", async () => {
      const worker = load({ settings });
      await worker.dispatch("push", pushEvent({ ...payload, daysLeft: -1 }));
      await worker.dispatch("push", pushEvent({ ...payload, daysLeft: "3" }));
      expect(worker.shown).toHaveLength(0);
    });
  });

  it("falls back to English texts when the page stored none", async () => {
    const worker = load({});
    await worker.dispatch("push", pushEvent({ ...payload, kind: "measurement" }));
    expect(worker.shown[0].options.body).toBe("A water measurement is due today.");
    expect((worker.shown[0].options.actions as { title: string }[])[0].title).toBe("Remind me tomorrow");
  });

  it("offers no actions where the browser supports none", async () => {
    const worker = load({ maxActions: 0 });
    await worker.dispatch("push", pushEvent(payload));
    expect(worker.shown[0].options.actions).toBeUndefined();
  });

  it.each([
    ["a message without JSON", undefined],
    ["a message with a foreign kind", { ...payload, kind: "feeding" }],
    ["a message with an address on another site", { ...payload, url: "https://evil.test/" }],
    ["a message with a protocol-relative address", { ...payload, url: "//evil.test/" }],
    ["a message without a tank", { ...payload, tankId: "7" }],
  ])("ignores %s", async (_name, data) => {
    const worker = load({});
    await worker.dispatch("push", pushEvent(data));
    expect(worker.shown).toEqual([]);
  });

  it("opens the care view in a new window when the notification is clicked and the app is closed", async () => {
    const worker = load({});
    const event = clickEvent("");
    await worker.dispatch("notificationclick", event);
    expect(event.notification.closed).toBe(true);
    expect(worker.opened).toEqual([`${ORIGIN}/care?tank=7`]);
  });

  it("brings an open window of the app to the care view", async () => {
    const open = { url: `${ORIGIN}/me`, focused: [] as string[], navigated: [] as string[] };
    const worker = load({ windows: [open, { url: "https://other.test/", focused: [], navigated: [] }] });
    await worker.dispatch("notificationclick", clickEvent(""));
    expect(open.focused).toEqual([`${ORIGIN}/me`]);
    expect(open.navigated).toEqual([`${ORIGIN}/care?tank=7`]);
    expect(worker.opened).toEqual([]);
  });

  it("snoozes through the API with the session cookie, the CSRF token and the time zone of the browser", async () => {
    const worker = load({
      settings: { apiUrl: "https://api.cichlids.test", texts: {} },
      fetchImpl: async url => url.endsWith("/api/auth/session")
        ? Response.json({ authenticated: true, csrfToken: "csrf-1", accountId: "a" })
        : new Response(JSON.stringify({ untilDate: "2026-10-17" }), { status: 200 }),
    });
    await worker.dispatch("notificationclick", clickEvent("snooze"));
    expect(worker.fetched.map(f => f.url)).toEqual(["https://api.cichlids.test/api/auth/session", "https://api.cichlids.test/api/me/care/reminders/snooze"]);
    expect(worker.fetched[0].init?.credentials).toBe("include");
    const post = worker.fetched[1].init!;
    expect(post.method).toBe("POST");
    expect(post.credentials).toBe("include");
    expect((post.headers as Record<string, string>)["X-CSRF-Token"]).toBe("csrf-1");
    expect(JSON.parse(post.body as string)).toEqual({
      tankId: 7, kind: "water_change", dueDate: "2026-10-16", timeZone: Intl.DateTimeFormat().resolvedOptions().timeZone,
    });
    expect(worker.opened).toEqual([]);
  });

  it("opens the care view when the snooze cannot be stored", async () => {
    const worker = load({ fetchImpl: async () => Response.json({ authenticated: false, csrfToken: null, accountId: null }) });
    await worker.dispatch("notificationclick", clickEvent("snooze"));
    expect(worker.opened).toEqual([`${ORIGIN}/care?tank=7`]);
  });

  it("takes control of its pages without waiting", async () => {
    const worker = load({});
    expect(worker.listeners.has("install")).toBe(true);
    expect(worker.listeners.has("activate")).toBe(true);
  });
});
