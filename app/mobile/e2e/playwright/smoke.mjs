#!/usr/bin/env node
// Playwright-driven smoke test for the Expo web build of the mobile app,
// covering the same ground as the Maestro flows in e2e/maestro/ (gallery,
// tank detail) plus the rest of the primary screens. Used as a fallback
// where a local Maestro web device is not available; see this package's
// README/task report for details.
//
// Also covers i18n: a plain load exercises the English fallback locale (the
// default in a headless browser with no forced language), and a `?lang=de`
// load exercises the German dev override so both locales get asserted
// against real rendered text, not just the translation resource files.
//
// Prerequisites: `expo start --web` (or an export served statically) running
// at PLAYWRIGHT_BASE_URL (default http://localhost:8081), a running
// Cichlids.Api at the mobile app's configured API URL, and the Playwright
// chromium browser installed (`pnpm exec playwright install chromium`).
import { chromium } from "playwright";
import path from "node:path";
import fs from "node:fs";
import { fileURLToPath } from "node:url";

const BASE_URL = process.env.PLAYWRIGHT_BASE_URL ?? "http://localhost:8081";
const OUT_DIR = process.env.PLAYWRIGHT_OUT_DIR ?? path.join(fileURLToPath(new URL(".", import.meta.url)), "../../../../..", "app-screens");
fs.mkdirSync(OUT_DIR, { recursive: true });

function shot(page, name) {
  return page.screenshot({ path: path.join(OUT_DIR, name), fullPage: false });
}

const browser = await chromium.launch();
try {
  const page = await browser.newPage({ viewport: { width: 390, height: 844 } });
  const pageErrors = [];
  page.on("pageerror", (err) => pageErrors.push(String(err)));

  // Gallery: infinite grid of real pictures, English (the untouched default locale).
  await page.goto(`${BASE_URL}/`, { waitUntil: "networkidle", timeout: 60000 });
  await page.waitForSelector('[data-testid="picture-card"]', { timeout: 20000 });
  await page.waitForTimeout(1000);
  const pictureCount = await page.locator('[data-testid="picture-card"]').count();
  console.log(`Gallery visible with ${pictureCount} picture cards`);
  await shot(page, "01-gallery.png");
  if (pictureCount === 0) throw new Error("Expected at least one picture card in the gallery");
  await page.waitForSelector("text=Newest", { timeout: 5000 });

  // Picture detail: tapping a card opens it with comments.
  await page.locator('[data-testid="picture-card"]').first().click();
  await page.waitForURL(/\/gallery\/.+/, { timeout: 10000 });
  await page.waitForSelector("text=Comments", { timeout: 15000 });
  await page.waitForTimeout(800);
  console.log(`Picture detail reached: ${page.url()}`);
  await shot(page, "02-picture-detail.png");

  // Tanks tab: list, then detail with datasheet and section galleries.
  await page.goto(`${BASE_URL}/tanks`, { waitUntil: "networkidle", timeout: 30000 });
  await page.waitForSelector('[data-testid="tank-card"]', { timeout: 20000 });
  await page.waitForTimeout(800);
  await shot(page, "03-tanks-list.png");
  await page.locator('[data-testid="tank-card"]').first().click();
  await page.waitForURL(/\/tanks\/\d+/, { timeout: 10000 });
  await page.waitForSelector("text=Datasheet", { timeout: 15000 });
  await page.waitForTimeout(800);
  console.log(`Tank detail reached: ${page.url()}`);
  await shot(page, "04-tank-detail.png");

  // Community tab: category/thread list, then a thread with real posts.
  await page.goto(`${BASE_URL}/community`, { waitUntil: "networkidle", timeout: 30000 });
  await page.waitForTimeout(1000);
  await shot(page, "05-community-threads.png");

  // i18n: fresh gallery load in English (default) and one forced to German
  // via the `?lang=de` dev override, each asserting real translated text.
  await page.goto(`${BASE_URL}/`, { waitUntil: "networkidle", timeout: 30000 });
  await page.waitForSelector('[data-testid="picture-card"]', { timeout: 20000 });
  await page.waitForSelector("text=Gallery", { timeout: 5000 });
  await page.waitForSelector("text=Most viewed", { timeout: 5000 });
  await page.waitForTimeout(500);
  console.log("English gallery labels confirmed (Gallery, Newest, Most viewed)");
  await shot(page, "11-gallery-en.png");

  await page.goto(`${BASE_URL}/?lang=de`, { waitUntil: "networkidle", timeout: 30000 });
  await page.waitForSelector('[data-testid="picture-card"]', { timeout: 20000 });
  await page.waitForSelector("text=Galerie", { timeout: 5000 });
  await page.waitForSelector("text=Meiste Aufrufe", { timeout: 5000 });
  await page.waitForTimeout(500);
  console.log("German gallery labels confirmed (Galerie, Neueste, Meiste Aufrufe)");
  await shot(page, "12-gallery-de.png");

  if (pageErrors.length > 0) {
    console.error("Page errors observed:", pageErrors);
    process.exitCode = 1;
  } else {
    console.log("Playwright smoke passed.");
  }
} finally {
  await browser.close();
}
