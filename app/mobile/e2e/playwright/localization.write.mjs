// Story: TASK-4.2
import assert from "node:assert/strict";
import { createRequire } from "node:module";
import { chromium } from "playwright";
import { API_URL, WEB_URL, byTestId, logInThroughKeycloak, signOutThroughKeycloak } from "./support.mjs";

const require = createRequire(import.meta.url);
const icons = require("@expo/vector-icons/build/vendor/react-native-vector-icons/glyphmaps/MaterialCommunityIcons.json");
const browser = await chromium.launch();
try {
  const context = await browser.newContext({ locale: "pt-BR", viewport: { width: 390, height: 844 } });
  const page = await context.newPage();
  const errors = [];
  page.on("pageerror", (error) => errors.push(String(error)));
  await page.goto(`${WEB_URL}/me`, { waitUntil: "networkidle" });
  await page.getByText("Não conectado", { exact: true }).waitFor();
  assert.equal(await byTestId(page, "login-button").getAttribute("aria-label"), "Entrar");
  await byTestId(page, "language-settings-toggle").click();
  const portugueseFormat = await byTestId(page, "locale-format-preview").textContent();
  assert.ok(portugueseFormat.includes("12.345,6") && portugueseFormat.includes("jul."), portugueseFormat);
  await byTestId(page, "language-de").click();
  await page.getByText("Nicht angemeldet", { exact: true }).waitFor();
  assert.equal(await byTestId(page, "login-button").getAttribute("aria-label"), "Anmelden");
  await page.reload({ waitUntil: "networkidle" });
  await page.getByText("Nicht angemeldet", { exact: true }).waitFor();
  assert.equal(await page.locator("html").getAttribute("lang"), "de");
  await byTestId(page, "language-settings-toggle").click();
  const germanFormat = await byTestId(page, "locale-format-preview").textContent();
  assert.ok(germanFormat.includes("12.345,6") && germanFormat.includes("Juli"), germanFormat);
  assert.notEqual(germanFormat, portugueseFormat);
  await byTestId(page, "language-device").click();
  await page.getByText("Não conectado", { exact: true }).waitFor();

  await logInThroughKeycloak(page);
  assert.equal(await byTestId(page, "logout-button").getAttribute("aria-label"), "Sair");
  await byTestId(page, "language-settings-toggle").click();
  await byTestId(page, "language-de").click();
  await page.getByRole("button", { name: "Mein öffentliches Profil", exact: true }).waitFor();
  assert.equal(await byTestId(page, "logout-button").getAttribute("aria-label"), "Abmelden");
  await page.reload({ waitUntil: "networkidle" });
  await page.getByRole("button", { name: "Mein öffentliches Profil", exact: true }).waitFor();
  await byTestId(page, "language-settings-toggle").click();
  await byTestId(page, "language-device").click();
  await page.getByRole("button", { name: "Meu perfil público", exact: true }).waitFor();
  await signOutThroughKeycloak(page);
  await page.getByText("Não conectado", { exact: true }).waitFor();

  for (const width of [390, 320]) {
    await page.setViewportSize({ width, height: 844 });
    for (const route of ["today", "discover", "fish", "me"]) {
      await page.goto(`${WEB_URL}/${route}?rtl=1`, { waitUntil: "networkidle" });
      assert.equal(await page.locator("html").getAttribute("dir"), "rtl");
      assert.equal(await page.locator("html").getAttribute("lang"), "pt-BR");
      const overflow = await page.evaluate(() => document.documentElement.scrollWidth > innerWidth);
      assert.equal(overflow, false, `${route} at ${width} overflows`);
      for (const tab of ["today", "discover", "create", "fish", "me"]) {
        const bounds = await byTestId(page, `tab-${tab}`).boundingBox();
        assert.ok(bounds && bounds.x >= -1 && bounds.x + bounds.width <= width + 1, `${tab} is clipped`);
        const clippedLabels = await byTestId(page, `tab-${tab}`).evaluate((element) =>
          [...element.querySelectorAll("div, span")].filter((child) => child.childElementCount === 0 && /[a-z]/i.test(child.textContent ?? "") && child.scrollWidth > child.clientWidth + 1).map((child) => child.textContent),
        );
        assert.deepEqual(clippedLabels, [], `${tab} has a clipped label`);
      }
      const mirroredPhotos = await page.locator('[data-testid="picture-card"] img, [data-testid="tank-card"] img').evaluateAll((photos) => photos.filter((photo) => {
        for (let element = photo; element; element = element.parentElement) {
          const transform = getComputedStyle(element).transform;
          if (transform !== "none") {
            const matrix = new DOMMatrix(transform);
            if (matrix.a * matrix.d - matrix.b * matrix.c < 0) return true;
          }
        }
        return false;
      }).length);
      assert.equal(mirroredPhotos, 0, "Photos retain their original orientation in RTL");
      const today = await byTestId(page, "tab-today").boundingBox();
      const me = await byTestId(page, "tab-me").boundingBox();
      assert.ok(today.x > me.x, "RTL tab order follows reading direction");
    }
  }
  await page.route(`${API_URL}/api/species/rtl-check`, (route) => route.fulfill({
    status: 200,
    contentType: "application/json",
    headers: { "access-control-allow-origin": WEB_URL, "access-control-allow-credentials": "true" },
    body: JSON.stringify({ id: 1, slug: "rtl-check", genus: "Cyphotilapia", name: "frontosa", displayName: "Cyphotilapia frontosa", category: null, temperatureRange: null, phRange: null, ghRange: null, khRange: null, maxSize: null, breeding: "mouthbreeder", diet: "carnivore", commonNames: [], links: [], description: null, origin: null, habitat: null, morphs: null, pictureCount: 2 }),
  }));
  for (const [mode, icon] of [["1", "arrow-left"], ["0", "arrow-right"]]) {
    await page.goto(`${WEB_URL}/species/rtl-check?rtl=${mode}`, { waitUntil: "networkidle" });
    assert.equal(await byTestId(page, "species-gallery-arrow").textContent(), String.fromCodePoint(icons[icon]));
  }
  await page.goto(`${WEB_URL}/me?rtl=1`, { waitUntil: "networkidle" });
  await page.screenshot({ path: "/tmp/task-4.2-rtl-320.png" });
  assert.deepEqual(errors, []);
  console.log("TASK-4.2: pt-BR device, German preference, persistence, device reset and pseudo RTL passed");
} finally {
  await browser.close();
}
