// Story: TASK-4.4
import assert from "node:assert/strict";
import { chromium } from "playwright";
import { API_URL, WEB_URL, byTestId, passwordGrantToken, submitKeycloakLogin, signOutThroughKeycloak } from "./support.mjs";

const browser = await chromium.launch();
try {
  const ownerContext = await browser.newContext({ locale: "en-US", colorScheme: "dark", viewport: { width: 390, height: 844 } });
  const owner = await ownerContext.newPage();
  await owner.goto(`${WEB_URL}/me`, { waitUntil: "networkidle" });
  await submitKeycloakLogin(owner, byTestId(owner, "login-button"));
  await byTestId(owner, "me-name").waitFor();
  await byTestId(owner, "units-settings-toggle").click({ timeout: 5000 });
  for (const choice of ["units-device", "units-metric", "units-us"]) {
    const bounds = await byTestId(owner, choice).boundingBox();
    assert.ok(bounds && bounds.height >= 48, `${choice} meets the touch target`);
  }
  await owner.screenshot({ path: "/tmp/task-4.4-units-dark.png" });
  await byTestId(owner, "units-us").click();
  const token = await passwordGrantToken();
  const response = await fetch(`${API_URL}/api/tanks`, {
    method: "POST",
    headers: { Authorization: `Bearer ${token}`, "Content-Type": "application/json" },
    body: JSON.stringify({ title: "Units round trip", isPublic: true, volume: { value: 55, unit: "us_gallon" }, dimensions: { width: 48.125, height: 20, depth: 12, unit: "inch" } }),
  });
  assert.equal(response.status, 201, await response.clone().text());
  const tank = await response.json();
  assert.equal(tank.volume.liters, 208.19764812);
  assert.equal(tank.volume.inputValue, 55);
  const viewerContext = await browser.newContext({ locale: "de-DE", colorScheme: "light", viewport: { width: 390, height: 844 } });
  const viewer = await viewerContext.newPage();
  await viewer.goto(`${WEB_URL}/me`, { waitUntil: "networkidle" });
  await submitKeycloakLogin(viewer, byTestId(viewer, "login-button"), "dev-mod");
  await byTestId(viewer, "me-name").waitFor();
  await byTestId(viewer, "units-settings-toggle").click();
  await viewer.screenshot({ path: "/tmp/task-4.4-units-light.png" });
  await byTestId(viewer, "units-metric").click();
  await viewer.goto(`${WEB_URL}/tanks/${tank.id}`, { waitUntil: "networkidle" });
  await viewer.getByText("208,2 L", { exact: true }).waitFor();
  await viewer.getByText("122,24", { exact: false }).waitFor();
  await owner.goto(`${WEB_URL}/me`, { waitUntil: "networkidle" });
  await byTestId(owner, "units-settings-toggle").click();
  await byTestId(owner, "units-metric").click();
  await owner.reload({ waitUntil: "networkidle" });
  await byTestId(owner, "units-settings-toggle").waitFor();
  assert.match(await byTestId(owner, "units-settings-toggle").textContent(), /Metric/);
  await owner.goto(`${WEB_URL}/tanks/${tank.id}`, { waitUntil: "networkidle" });
  await owner.getByText("55 US gal", { exact: true }).waitFor();
  await owner.getByText("48.125", { exact: false }).waitFor();
  await viewer.goto(`${WEB_URL}/me`, { waitUntil: "networkidle" });
  await signOutThroughKeycloak(viewer);
  await owner.goto(`${WEB_URL}/me`, { waitUntil: "networkidle" });
  await signOutThroughKeycloak(owner);
  console.log("TASK-4.4: metric member sees liters; creator sees exact US input despite metric preference");
} finally {
  await browser.close();
}
