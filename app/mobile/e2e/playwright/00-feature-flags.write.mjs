// Story: TASK-4.1
import assert from "node:assert/strict";
import { execFile } from "node:child_process";
import { promisify } from "node:util";
import { chromium } from "playwright";
import { API_URL, WEB_URL, byTestId } from "./support.mjs";

const run = promisify(execFile);
const browser = await chromium.launch();
let switched = false;
try {
  const context = await browser.newContext({ viewport: { width: 390, height: 844 } });
  const page = await context.newPage();
  const response = await fetch(`${API_URL}/api/feature-flags`);
  assert.equal(response.status, 200);
  assert.deepEqual(await response.json(), { "discover-activity": false });
  await page.goto(`${WEB_URL}/discover?lang=en`, { waitUntil: "networkidle" });
  assert.equal(await byTestId(page, "discover-activity").count(), 0);

  switched = true;
  await run("bash", ["e2e/write-stack.sh", "set-discover-activity", "true"]);
  assert.deepEqual(await (await fetch(`${API_URL}/api/feature-flags`)).json(), { "discover-activity": true });
  await page.evaluate(() => document.dispatchEvent(new Event("visibilitychange")));
  await byTestId(page, "discover-activity").waitFor({ state: "visible", timeout: 15000 });

  await run("bash", ["e2e/write-stack.sh", "set-discover-activity", "false"]);
  switched = false;
  await page.evaluate(() => document.dispatchEvent(new Event("visibilitychange")));
  await byTestId(page, "discover-activity").waitFor({ state: "detached", timeout: 15000 });
  await page.reload({ waitUntil: "networkidle" });
  assert.equal(await byTestId(page, "discover-activity").count(), 0);
  console.log("TASK-4.1: configuration toggles the section on foreground return with the same web build.");
} finally {
  try {
    if (switched) await run("bash", ["e2e/write-stack.sh", "set-discover-activity", "false"]);
  } finally {
    await browser.close();
  }
}
