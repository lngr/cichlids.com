// Story: TASK-4.3
import assert from "node:assert/strict";
import en from "../../src/i18n/en.json" with { type: "json" };
import { chromium } from "playwright";
import { WEB_URL, byTestId, logInThroughKeycloak, signOutThroughKeycloak } from "./support.mjs";

const browser = await chromium.launch();
let page;
const errors = [];
try {
  page = await browser.newPage({ viewport: { width: 390, height: 844 } });
  page.on("pageerror", error => errors.push(String(error)));
  await page.goto(`${WEB_URL}/me?lang=en`, { waitUntil: "networkidle" });
  await logInThroughKeycloak(page);
  await page.goto(WEB_URL, { waitUntil: "networkidle" });
  await byTestId(page, "today-member").waitFor({ timeout: 5000 });
  const tabs = page.locator('[data-testid^="tab-"]');
  assert.deepEqual(await tabs.evaluateAll(nodes => nodes.map(node => node.dataset.testid)), ["tab-today", "tab-discover", "tab-create", "tab-fish", "tab-me"]);
  for (const tab of ["today", "discover", "create", "fish", "me"]) {
    assert.ok((await byTestId(page, `tab-${tab}`).textContent()).endsWith(en.tabs[tab]));
    const bounds = await byTestId(page, `tab-${tab}`).boundingBox();
    assert.ok(bounds.width >= 48 && bounds.height >= 48);
  }
  await byTestId(page, "tab-discover").click();
  await byTestId(page, "discover-archive").waitFor();
  for (const section of ["gallery", "tanks", "community"]) {
    await byTestId(page, `discover-${section}`).click();
    await page.waitForURL(`${WEB_URL}/${section}`);
    assert.equal(await byTestId(page, "tab-discover").getAttribute("aria-selected"), "true");
    if (section === "gallery") await page.getByText("Newest", { exact: true }).waitFor();
    if (section === "community") await page.getByText("Archive of the old community forums. Read-only, no new posts.", { exact: true }).waitFor();
    await byTestId(page, "tab-discover").click();
  }
  await byTestId(page, "tab-fish").click();
  await page.getByPlaceholder("Search genus or species…").waitFor();
  await byTestId(page, "tab-create").click();
  await byTestId(page, "create-sheet").waitFor();
  assert.equal(await byTestId(page, "create-photo").count(), 1);
  await byTestId(page, "create-cancel").click();
  await page.waitForURL(`${WEB_URL}/fish`);
  await byTestId(page, "tab-create").click();
  await byTestId(page, "create-photo").click();
  await page.waitForURL(`${WEB_URL}/upload`);
  await byTestId(page, "upload-pick").waitFor();
  await page.goBack();
  await page.waitForURL(`${WEB_URL}/fish`);
  await page.goForward();
  await page.waitForURL(`${WEB_URL}/upload`);
  await page.goBack();
  await page.waitForURL(`${WEB_URL}/fish`);
  await byTestId(page, "tab-me").click();
  await byTestId(page, "me-tab-pictures").waitFor();
  await signOutThroughKeycloak(page);
  await byTestId(page, "tab-discover").click();
  await byTestId(page, "tab-create").click();
  await byTestId(page, "create-photo").click();
  await page.waitForURL(`${WEB_URL}/me`);
  await byTestId(page, "login-button").waitFor();
  await byTestId(page, "create-sheet").waitFor({ state: "hidden" });
  const lightSurface = await byTestId(page, "tab-discover").evaluate(element => getComputedStyle(element.parentElement).backgroundColor);
  await page.emulateMedia({ colorScheme: "dark" });
  await page.waitForFunction(previous => getComputedStyle(document.querySelector('[data-testid="tab-discover"]').parentElement).backgroundColor !== previous, lightSurface);
  await byTestId(page, "tab-discover").click();
  await page.screenshot({ path: "/tmp/task43-discover-dark.png" });
  assert.deepEqual(errors, []);
  console.log("TASK-4.3: five tabs, Discover sections, Fish, create sheet and Me passed.");
} catch (error) {
  console.error(errors, await page?.locator("body").innerText());
  await page?.screenshot({ path: "/tmp/task43-navigation-failure.png" });
  throw error;
} finally {
  await browser.close();
}
