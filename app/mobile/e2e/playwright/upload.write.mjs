// Story: TASK-3.21
// Logs in as the local dev user from the gallery's upload button, uploads photos and checks the
// whole flow on the web: a file that is not an image shows the API's error message, a discarded
// draft is deleted, and leaving the screen without publishing keeps the draft, which the Me tab
// lists. A draft opened from that list resumes on the upload screen and publishes from there, and
// deleting a draft from the list removes it. A published photo opens on its detail page with its
// description and topic and shows first in the gallery. The gallery refetches its list on return
// only after a publish.
//
// The JPEG is drawn on a canvas in the page. Runs on the isolated write stack only (see
// e2e/write-stack.sh), since uploading and publishing write to the database and the bucket.
import { chromium } from "playwright";
import { API_URL, WEB_URL, byTestId, logInThroughKeycloak } from "./support.mjs";

const TOKEN_STORAGE_KEY = "cichlids.auth.tokens";

const browser = await chromium.launch();
try {
  const page = await browser.newPage({ viewport: { width: 390, height: 844 } });
  let pictureListRequests = 0;
  page.on("request", (request) => {
    if (new URL(request.url()).pathname === "/api/pictures") pictureListRequests += 1;
  });
  const pageErrors = [];
  page.on("pageerror", (err) => pageErrors.push(String(err)));

  const fileInput = byTestId(page, "upload-file-input");
  const preview = byTestId(page, "upload-preview");
  const publishButton = byTestId(page, "upload-publish");

  // A 1200x800 JPEG with a gradient, well within the API's size and pixel limits.
  const jpeg = Buffer.from(
    await page.evaluate(async () => {
      const canvas = document.createElement("canvas");
      canvas.width = 1200;
      canvas.height = 800;
      const context = canvas.getContext("2d");
      const gradient = context.createLinearGradient(0, 0, 1200, 800);
      gradient.addColorStop(0, "#1d4e89");
      gradient.addColorStop(1, "#f2a541");
      context.fillStyle = gradient;
      context.fillRect(0, 0, 1200, 800);
      const blob = await new Promise((resolve) => canvas.toBlob(resolve, "image/jpeg", 0.9));
      return Array.from(new Uint8Array(await blob.arrayBuffer()));
    }),
  );
  const photo = { name: "e2e-photo.jpg", mimeType: "image/jpeg", buffer: jpeg };

  async function openUploadScreen() {
    await byTestId(page, "tab-gallery").click({ timeout: 20000 });
    await page.waitForURL(/\/gallery$/, { timeout: 10000 });
    await byTestId(page, "upload-button").click({ timeout: 20000 });
    await page.waitForURL(/\/upload$/, { timeout: 10000 });
  }

  async function openMeTab() {
    await byTestId(page, "tab-me").click({ timeout: 20000 });
    await page.waitForURL(/\/me$/, { timeout: 10000 });
  }

  // The Me tab refreshes its draft list on focus, so this waits for the expected count.
  async function expectDraftItems(count, context) {
    try {
      await page.waitForFunction(
        (expected) => document.querySelectorAll('[data-testid="draft-item"]').length === expected,
        count,
        { timeout: 20000 },
      );
    } catch {
      const shown = await byTestId(page, "draft-item").count();
      throw new Error(`Expected ${count} draft(s) in the Me tab ${context}, found ${shown}`);
    }
    if (count === 0) await byTestId(page, "drafts-empty").waitFor({ timeout: 5000 });
  }

  // Polls the drafts API briefly until it is empty.
  async function expectNoDrafts(context) {
    let drafts = [];
    for (let attempt = 0; attempt < 20; attempt += 1) {
      drafts = await listDrafts();
      if (drafts.length === 0) return;
      await page.waitForTimeout(250);
    }
    throw new Error(`Expected no drafts ${context}, found ${drafts.length}`);
  }

  async function listDrafts() {
    const token = await page.evaluate((key) => JSON.parse(sessionStorage.getItem(key) ?? "null")?.accessToken, TOKEN_STORAGE_KEY);
    if (!token) throw new Error("No access token in the session storage");
    const response = await fetch(`${API_URL}/api/me/drafts`, { headers: { Authorization: `Bearer ${token}` } });
    if (!response.ok) throw new Error(`GET /api/me/drafts returned ${response.status}`);
    return response.json();
  }

  await page.goto(`${WEB_URL}/`, { waitUntil: "networkidle", timeout: 60000 });
  // Anonymous, the upload button leads to the Me tab and its login.
  await byTestId(page, "upload-button").click({ timeout: 20000 });
  await page.waitForURL(/\/me$/, { timeout: 10000 });
  console.log(`Anonymous upload leads to the login, logged in as "${await logInThroughKeycloak(page)}"`);

  // A file with a JPEG name and type but other content is rejected by the API with a 400.
  await openUploadScreen();
  await fileInput.setInputFiles({ name: "not-a-photo.jpg", mimeType: "image/jpeg", buffer: Buffer.from("not an image") });
  const error = byTestId(page, "upload-error");
  await error.waitFor({ timeout: 20000 });
  const errorText = (await error.textContent())?.trim() ?? "";
  if (!errorText.includes("not image/jpeg")) throw new Error(`Expected the API's error message, got "${errorText}"`);
  await byTestId(page, "upload-retry").waitFor({ timeout: 5000 });
  if ((await preview.count()) !== 0) throw new Error("A preview is visible after a rejected upload");
  console.log(`A non-image upload shows "${errorText}"`);

  // Picking a valid photo on the same screen replaces the error with the draft's preview.
  await fileInput.setInputFiles(photo);
  await preview.waitFor({ timeout: 30000 });
  if ((await error.count()) !== 0) throw new Error("The error is visible after a successful upload");
  if ((await publishButton.getAttribute("aria-disabled")) !== "true") {
    throw new Error("The publish button is enabled without a title");
  }
  const listRequestsBeforeDiscard = pictureListRequests;
  await byTestId(page, "upload-discard").click();
  await page.waitForURL(/\/gallery$/, { timeout: 10000 });
  await expectNoDrafts("after discarding");
  await page.waitForTimeout(1000);
  if (pictureListRequests !== listRequestsBeforeDiscard) {
    throw new Error("The gallery refetched its list on return although nothing was published");
  }
  console.log("A discarded draft is deleted and the gallery shows without a refetch");

  // Going back from the upload screen with an unpublished draft keeps that draft.
  await openUploadScreen();
  await fileInput.setInputFiles(photo);
  await preview.waitFor({ timeout: 30000 });
  const draftsAfterUpload = await listDrafts();
  if (draftsAfterUpload.length !== 1) throw new Error(`Expected one draft after the upload, found ${draftsAfterUpload.length}`);
  const keptDraftId = String(draftsAfterUpload[0].id);
  await page.goBack();
  await page.waitForURL(/\/gallery$/, { timeout: 10000 });
  // A moment for any request the closing screen sends to reach the API.
  await page.waitForTimeout(1000);
  const draftsAfterLeaving = await listDrafts();
  if (draftsAfterLeaving.length !== 1 || String(draftsAfterLeaving[0].id) !== keptDraftId) {
    throw new Error(`Expected draft ${keptDraftId} to be kept after leaving the upload screen, found ${draftsAfterLeaving.length} draft(s)`);
  }
  await openMeTab();
  await expectDraftItems(1, "after leaving the upload screen");
  console.log(`Leaving the upload screen keeps draft ${keptDraftId}, the Me tab lists it`);

  // Opening the draft from the Me tab resumes it on the upload screen, where it is published.
  await byTestId(page, "draft-open").first().click();
  await page.waitForURL(new RegExp(`/upload\\?draft=${keptDraftId}$`), { timeout: 10000 });
  await preview.waitFor({ timeout: 20000 });
  const resumedTitle = `E2E resumed draft ${Date.now()}`;
  await byTestId(page, "upload-title").fill(resumedTitle);
  await publishButton.click();
  await page.waitForURL(/\/gallery\/[^/]+$/, { timeout: 30000 });
  await byTestId(page, "picture-title").waitFor({ timeout: 20000 });
  const resumedShownTitle = (await byTestId(page, "picture-title").textContent())?.trim();
  if (resumedShownTitle !== resumedTitle) throw new Error(`Expected the detail title "${resumedTitle}", got "${resumedShownTitle}"`);
  const resumedSlug = new URL(page.url()).pathname.split("/").pop();
  await expectNoDrafts("after publishing the resumed draft");
  // The detail opens in the gallery tab; pressing that tab returns its stack to the list.
  await byTestId(page, "tab-gallery").click();
  await page.waitForURL(/\/gallery$/, { timeout: 10000 });
  await openMeTab();
  await expectDraftItems(0, "after publishing the resumed draft");
  console.log(`The resumed draft is published on /gallery/${resumedSlug} and leaves the Me tab's list`);

  // Upload, title and publish.
  const title = `E2E upload ${Date.now()}`;
  await openUploadScreen();
  await fileInput.setInputFiles(photo);
  await preview.waitFor({ timeout: 30000 });
  // The preview shows the draft's image from the bucket, so it loads only when the upload stored
  // the image variants and the bucket serves them.
  await page.waitForFunction(
    () => {
      const image = document.querySelector('[data-testid="upload-preview"] img');
      return Boolean(image?.complete && image.naturalWidth > 0);
    },
    null,
    { timeout: 20000 },
  );
  await byTestId(page, "upload-title").fill(title);
  const description = "Uploaded by the write E2E spec.";
  await byTestId(page, "upload-description").fill(description);
  await byTestId(page, "upload-topic-tanks").click();
  if ((await publishButton.getAttribute("aria-disabled")) === "true") {
    throw new Error("The publish button is disabled with a title");
  }
  await publishButton.click();

  await page.waitForURL(/\/gallery\/[^/]+$/, { timeout: 30000 });
  const pictureTitle = byTestId(page, "picture-title");
  await pictureTitle.waitFor({ timeout: 20000 });
  const shownTitle = (await pictureTitle.textContent())?.trim();
  if (shownTitle !== title) throw new Error(`Expected the detail title "${title}", got "${shownTitle}"`);
  const shownDescription = (await byTestId(page, "picture-description").textContent())?.trim();
  if (shownDescription !== description) throw new Error(`Expected the detail description "${description}", got "${shownDescription}"`);
  // The detail screen does not render the topic, so the API's detail response is checked instead.
  const slug = new URL(page.url()).pathname.split("/").pop();
  const detail = await (await fetch(`${API_URL}/api/pictures/${slug}`)).json();
  if (detail.topic !== "tanks") throw new Error(`Expected the topic "tanks", got "${detail.topic}"`);
  console.log(`Published, detail page /gallery/${slug} shows the title and description, topic tanks`);

  await byTestId(page, "tab-gallery").click();
  await page.waitForURL(/\/gallery$/, { timeout: 10000 });
  const firstCard = byTestId(page, "picture-card").first();
  await page.waitForFunction(
    (expected) => document.querySelector('[data-testid="picture-card"]')?.getAttribute("aria-label") === expected,
    title,
    { timeout: 20000 },
  );
  if (!((await firstCard.textContent()) ?? "").includes(title)) {
    throw new Error("The first gallery card does not show the published title");
  }
  await expectNoDrafts("after publishing");
  console.log("The gallery shows the published photo first");

  // Deleting a draft from the Me tab's list removes it from the API.
  await openUploadScreen();
  await fileInput.setInputFiles(photo);
  await preview.waitFor({ timeout: 30000 });
  await page.goBack();
  await page.waitForURL(/\/gallery$/, { timeout: 10000 });
  await openMeTab();
  await expectDraftItems(1, "before deleting");
  await byTestId(page, "draft-delete").first().click();
  await expectDraftItems(0, "after deleting");
  await expectNoDrafts("after deleting from the Me tab");
  console.log("Deleting a draft from the Me tab removes it from the drafts API");

  if (pageErrors.length > 0) {
    throw new Error(`Page errors observed: ${pageErrors.join("; ")}`);
  }
} finally {
  await browser.close();
}
