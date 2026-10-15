// Story: TASK-3.40
// Two paths of the hardened app on the web build. First, the gallery while a further page loads:
// the member changes the sort, the overtaken load-more is dropped, and scrolling to the end loads
// further pages of the new sort. Second, login while the sign-in service is unreachable: the login
// button stays usable and keeps its accessibility label while busy, a failed tap shows a message
// with a retry, and a retry once the service is back opens the Keycloak login. The API list and the
// login start of the API are served by Playwright routes, so the spec writes nothing; it runs on
// the write stack with the other web specs (see e2e/write-stack.sh).
import { chromium } from "playwright";
import { API_URL, AUTHORIZATION_ENDPOINT, WEB_URL, byTestId } from "./support.mjs";

const PAGE_SIZE = 20;
const TOTAL = 60;

function pictureItem(sort, index) {
  return {
    id: index + 1,
    slug: `${sort}-${index}`,
    title: `${sort}-${index}`,
    description: null,
    publishedAt: "2026-01-01T00:00:00Z",
    viewCount: 1,
    ratingAverage: null,
    ratingCount: 0,
    commentCount: 0,
    topic: "cichlids",
    author: { id: 1, username: "seed", displayName: null, avatarUrl: null },
    image: null,
  };
}

const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

async function scrollToEnd(page) {
  await page.locator('[data-testid="picture-card"]').last().scrollIntoViewIfNeeded();
  await page.mouse.move(195, 500);
  // A list reports its end once per content length; moving away from the end first re-arms it.
  await page.mouse.wheel(0, -1500);
  await sleep(150);
  await page.mouse.wheel(0, 6000);
}

const browser = await chromium.launch();
try {
  const context = await browser.newContext({ viewport: { width: 390, height: 844 } });
  const page = await context.newPage();
  const pageErrors = [];
  page.on("pageerror", (err) => pageErrors.push(String(err)));

  // Gallery: the load-more of the first sort is held back until the sort has changed.
  const requests = [];
  let releaseHeldPage;
  const held = new Promise((resolve) => (releaseHeldPage = resolve));
  await page.route(`${API_URL}/api/pictures?*`, async (route) => {
    const url = new URL(route.request().url());
    const sort = url.searchParams.get("sort") ?? "newest";
    const offset = Number(url.searchParams.get("offset") ?? 0);
    requests.push({ sort, offset });
    if (sort === "newest" && offset > 0) await held;
    const count = Math.min(PAGE_SIZE, TOTAL - offset);
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      // The web client sends its requests with the session cookie, so CORS needs the exact origin.
      headers: { "access-control-allow-origin": WEB_URL, "access-control-allow-credentials": "true" },
      body: JSON.stringify({ total: TOTAL, items: Array.from({ length: count }, (_, i) => pictureItem(sort, offset + i)) }),
    });
  });

  await page.goto(`${WEB_URL}/gallery`, { waitUntil: "networkidle", timeout: 60000 });
  await page.waitForSelector('[data-testid="picture-card"]', { timeout: 20000 });

  for (let i = 0; i < 20 && !requests.some((r) => r.sort === "newest" && r.offset === PAGE_SIZE); i++) {
    await scrollToEnd(page);
    await sleep(250);
  }
  if (!requests.some((r) => r.sort === "newest" && r.offset === PAGE_SIZE)) throw new Error("Scrolling to the end did not start a load-more");
  console.log("A load-more is in flight");

  await page.getByText("Most viewed", { exact: true }).click();
  await page.locator('[data-testid="picture-card"]', { hasText: "views-0" }).first().waitFor({ timeout: 20000 });
  releaseHeldPage();
  await sleep(500);

  for (let i = 0; i < 20 && !requests.some((r) => r.sort === "views" && r.offset === PAGE_SIZE); i++) {
    await scrollToEnd(page);
    await sleep(250);
  }
  if (!requests.some((r) => r.sort === "views" && r.offset === PAGE_SIZE)) {
    throw new Error(`Scrolling to the end after the sort change loaded no further page; requests: ${JSON.stringify(requests)}`);
  }
  await page.locator('[data-testid="picture-card"]', { hasText: `views-${PAGE_SIZE}` }).first().waitFor({ timeout: 20000 });
  console.log("After a sort change during a load-more, scrolling to the end loads further pages");
  await page.unroute(`${API_URL}/api/pictures?*`);

  // Login while the sign-in service is unreachable: the API cannot start the login.
  let keycloakReachable = false;
  await page.route(`${API_URL}/api/auth/login`, async (route) => {
    if (keycloakReachable) return route.continue();
    await sleep(600);
    return route.abort("connectionrefused");
  });

  await page.goto(`${WEB_URL}/`, { waitUntil: "networkidle", timeout: 60000 });
  await byTestId(page, "tab-me").click({ timeout: 20000 });
  await page.waitForURL(/\/me$/, { timeout: 10000 });

  const loginButton = byTestId(page, "login-button");
  await loginButton.waitFor({ timeout: 20000 });
  if (await loginButton.isDisabled()) throw new Error("The login button is disabled although Keycloak is unreachable");

  await loginButton.click();
  const busyLabel = await loginButton.getAttribute("aria-label");
  if (busyLabel !== "Log in") throw new Error(`Expected the busy login button to be labelled "Log in", got "${busyLabel}"`);
  console.log("The busy login button keeps its accessibility label");

  const loginError = byTestId(page, "login-error");
  await loginError.waitFor({ timeout: 20000 });
  const message = (await loginError.textContent())?.trim() ?? "";
  if (!message.includes("cannot be reached")) throw new Error(`Expected the unreachable-login message, got "${message}"`);
  await page.getByText("Try again", { exact: true }).waitFor({ timeout: 5000 });
  console.log(`Login with Keycloak unreachable shows "${message}" and a retry`);

  keycloakReachable = true;
  await loginButton.click();
  await page.waitForURL((url) => url.href.startsWith(AUTHORIZATION_ENDPOINT), { timeout: 20000 });
  await page.waitForSelector("#username", { timeout: 20000 });
  console.log("The retry opens the Keycloak login");

  const unexpected = pageErrors.filter((error) => !error.includes("Failed to fetch"));
  if (unexpected.length > 0) throw new Error(`Page errors: ${unexpected.join("; ")}`);
} finally {
  await browser.close();
}
