// Story: TASK-3.22
// Seeds one published picture through the API as the local dev user, opens its detail page
// anonymously, logs in from the comment section's login prompt and posts a comment with a
// 4-star rating. A failed first post shows the inline error and keeps the draft; the retry posts
// it. The comment renders with its stars, the picture's rating summary counts the rating, the
// composer clears, and a page reload shows the comment with its stars. Runs on the isolated write
// stack only (see e2e/write-stack.sh), since seeding and commenting write to the database.
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { chromium } from "playwright";
import { API_URL, KEYCLOAK_URL, PASSWORD, USERNAME, WEB_URL, byTestId, submitKeycloakLogin } from "./support.mjs";

const FIXTURE = path.join(path.dirname(fileURLToPath(import.meta.url)), "..", "fixtures", "upload-photo.jpg");

async function devUserToken() {
  const response = await fetch(`${KEYCLOAK_URL}/realms/cichlids/protocol/openid-connect/token`, {
    method: "POST",
    headers: { "Content-Type": "application/x-www-form-urlencoded" },
    body: new URLSearchParams({ client_id: "cichlids-app", grant_type: "password", username: USERNAME, password: PASSWORD }),
  });
  if (!response.ok) throw new Error(`Password grant returned ${response.status}: ${await response.text()}`);
  return (await response.json()).access_token;
}

async function seedPublishedPicture(title) {
  const token = await devUserToken();
  const headers = { Authorization: `Bearer ${token}` };

  const form = new FormData();
  form.append("file", new Blob([fs.readFileSync(FIXTURE)], { type: "image/jpeg" }), "comment-e2e.jpg");
  const upload = await fetch(`${API_URL}/api/uploads`, { method: "POST", headers, body: form });
  if (!upload.ok) throw new Error(`POST /api/uploads returned ${upload.status}: ${await upload.text()}`);
  const draft = await upload.json();

  const publish = await fetch(`${API_URL}/api/posts/${draft.id}/publish`, {
    method: "POST",
    headers: { ...headers, "Content-Type": "application/json" },
    body: JSON.stringify({ title }),
  });
  if (!publish.ok) throw new Error(`POST /api/posts/${draft.id}/publish returned ${publish.status}: ${await publish.text()}`);
  return (await publish.json()).slug;
}

const slug = await seedPublishedPicture(`Comment target ${Date.now()}`);
console.log(`Seeded published picture /gallery/${slug}`);

const browser = await chromium.launch();
try {
  const page = await browser.newPage({ viewport: { width: 390, height: 844 } });
  const pageErrors = [];
  page.on("pageerror", (err) => pageErrors.push(String(err)));

  const commentText = `E2E comment ${Date.now()}`;
  const input = byTestId(page, "comment-input");
  const submit = byTestId(page, "comment-submit");
  const postedItem = byTestId(page, "comment-item").filter({ hasText: commentText });

  async function expectPostedCommentWithFourStars(context) {
    await postedItem.waitFor({ timeout: 20000 });
    if ((await postedItem.count()) !== 1) throw new Error(`Expected the comment exactly once ${context}, found ${await postedItem.count()}`);
    const stars = (await byTestId(postedItem, "comment-item-stars").textContent())?.trim();
    if (stars !== "★★★★") throw new Error(`Expected 4 stars on the comment ${context}, got "${stars}"`);
  }

  await page.goto(`${WEB_URL}/gallery/${slug}`, { waitUntil: "networkidle", timeout: 60000 });
  await byTestId(page, "picture-title").waitFor({ timeout: 20000 });
  if ((await input.count()) !== 0) throw new Error("The comment input is visible before logging in");
  await submitKeycloakLogin(page, byTestId(page, "comment-login"));
  await input.waitFor({ timeout: 30000 });
  if ((await page.url()) !== `${WEB_URL}/gallery/${slug}`) throw new Error(`Expected to stay on the picture after login, got ${page.url()}`);
  console.log("Anonymous comment section offers the login, logged in from there");

  if ((await submit.getAttribute("aria-disabled")) !== "true") throw new Error("Submit is enabled for an empty draft");
  await input.fill(commentText);
  await byTestId(page, "comment-star-2").click();
  await byTestId(page, "comment-star-2").click();
  await byTestId(page, "comment-star-4").click();
  if ((await byTestId(page, "comment-star-4").getAttribute("aria-checked")) !== "true") throw new Error("Star 4 is not selected");

  // The first post fails on the network; the draft stays and the retry posts it.
  let failNextPost = true;
  await page.route(`${API_URL}/api/pictures/${slug}/comments`, (route) => {
    if (route.request().method() === "POST" && failNextPost) {
      failNextPost = false;
      return route.fulfill({ status: 500, body: "" });
    }
    return route.continue();
  });
  await submit.click();
  await byTestId(page, "comment-error").waitFor({ timeout: 20000 });
  if ((await input.inputValue()) !== commentText) throw new Error("The draft text is lost after a failed post");
  console.log("A failed post shows the inline error and keeps the draft");

  await byTestId(page, "comment-retry").click();
  await expectPostedCommentWithFourStars("after posting");
  if ((await input.inputValue()) !== "") throw new Error("The comment input is not cleared after posting");
  if ((await byTestId(page, "comment-error").count()) !== 0) throw new Error("The error stays after a successful retry");
  if ((await byTestId(page, "comment-star-4").getAttribute("aria-checked")) === "true") throw new Error("The rating is not cleared after posting");
  await page.waitForFunction(() => document.querySelector('[data-testid="picture-meta"]')?.textContent?.includes("★★★★ (1)"), null, { timeout: 20000 });
  console.log(`The retry posts "${commentText}" with 4 stars, the rating summary shows 1 rating`);

  await page.reload({ waitUntil: "networkidle", timeout: 60000 });
  await expectPostedCommentWithFourStars("after a reload");
  console.log("The comment and its stars survive a page reload");

  if (pageErrors.length > 0) throw new Error(`Page errors: ${pageErrors.join("; ")}`);
} finally {
  await browser.close();
}
