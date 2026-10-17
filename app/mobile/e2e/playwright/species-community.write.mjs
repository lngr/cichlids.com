// Story: TASK-9.5
import assert from "node:assert/strict";
import { chromium } from "playwright";
import { API_URL, WEB_URL, byTestId, passwordGrantToken, sql } from "./support.mjs";

assert.equal(process.env.E2E_DB, "cichlids_e2e");
const run = Date.now().toString(36);
const slugs = { main: `speccom-${run}-main`, partner: `speccom-${run}-partner`, rare: `speccom-${run}-rare` };
const names = { main: `Speccom ${run} main`, partner: `Speccom ${run} partner`, rare: `Speccom ${run} rare` };
const tankTitles = [1, 2, 3].map((n) => `Speccom ${run} tank ${n}`);
const hiddenTitle = `Speccom ${run} hidden tank`;
let storyId = null;

function speciesId(key) {
  return Number(sql(`select id from species where slug = '${slugs[key]}'`));
}

const browser = await chromium.launch();
try {
  const token = await passwordGrantToken();
  const page = await browser.newPage();
  const bytes = await page.evaluate(async () => {
    const canvas = document.createElement("canvas");
    canvas.width = 800; canvas.height = 600;
    const ctx = canvas.getContext("2d");
    ctx.fillStyle = "teal"; ctx.fillRect(0, 0, 800, 600);
    const blob = await new Promise((resolve) => canvas.toBlob(resolve, "image/jpeg"));
    return [...new Uint8Array(await blob.arrayBuffer())];
  });
  await page.close();
  const body = new FormData();
  body.append("file", new Blob([new Uint8Array(bytes)], { type: "image/jpeg" }), "species-community.jpg");
  const upload = await fetch(`${API_URL}/api/uploads`, { method: "POST", headers: { Authorization: `Bearer ${token}` }, body });
  assert.equal(upload.status, 201);
  storyId = Number((await upload.json()).id);
  const publish = await fetch(`${API_URL}/api/stories/${storyId}/publish`, { method: "POST",
    headers: { Authorization: `Bearer ${token}`, "Content-Type": "application/json" },
    body: JSON.stringify({ title: `Speccom ${run} photo`, topic: "cichlids" }) });
  assert.equal(publish.status, 200);

  for (const key of ["main", "partner", "rare"]) {
    sql(`insert into species (genus, name, display_name, slug, breeding, aggression, intra_aggression, diet, created_at)
      values ('Speccom${run}', '${key}', '${names[key]}', '${slugs[key]}', 'unspecified', 'unspecified', 'unspecified', 'unspecified', now())`);
  }
  const owner = Number(sql(`select author_profile_id from story where id = ${storyId}`));
  sql(`insert into story_subject (story_id, subject_type, species_id) values (${storyId}, 'species', ${speciesId("main")});
    update story set view_count = 40 where id = ${storyId}`);
  const addTank = (title, visibility, species) => {
    const id = Number(sql(`insert into tank (profile_id, title, state, profile_visibility, stream_visibility, created_at, published_at)
      values (${owner}, '${title}', 'published', '${visibility}', '${visibility}', now(), now()) returning id`).split("\n")[0]);
    species.forEach((key, index) => sql(`insert into inhabitant (tank_id, species_id, count, sort) values (${id}, ${speciesId(key)}, 2, ${index})`));
    return id;
  };
  tankTitles.forEach((title) => addTank(title, "public", ["main", "partner"]));
  addTank(`Speccom ${run} rare 1`, "public", ["main", "rare"]);
  addTank(`Speccom ${run} rare 2`, "public", ["main", "rare"]);
  addTank(hiddenTitle, "private", ["main", "partner", "rare"]);

  const response = await fetch(`${API_URL}/api/species/${slugs.main}/community`);
  assert.equal(response.status, 200);
  const community = await response.json();
  assert.equal(community.tankCount, 5);
  assert.equal(community.photos.length, 1);
  assert.deepEqual(community.stockingPartners.map((p) => [p.species.slug, p.tankCount]), [[slugs.partner, 3]]);
  assert.ok(!JSON.stringify(community).includes(hiddenTitle));

  for (const scheme of ["light", "dark"]) {
    const context = await browser.newContext({ locale: "en-US", colorScheme: scheme, viewport: { width: 390, height: 844 } });
    const visitor = await context.newPage();
    await visitor.goto(`${WEB_URL}/species/${slugs.main}?lang=en`, { waitUntil: "networkidle" });
    await byTestId(visitor, "species-community").waitFor();
    assert.equal(await byTestId(visitor, "species-community-photo").count(), 1);
    assert.match(await byTestId(visitor, "species-community-tank-count").innerText(), /^5 public tanks keep this species$/);
    assert.equal(await byTestId(visitor, "species-community-tank").count(), 5);
    assert.equal(await byTestId(visitor, "species-community-partner").count(), 1);
    const partners = await byTestId(visitor, "species-community-partners").innerText();
    assert.ok(partners.includes(names.partner) && partners.includes("in 3 tanks") && !partners.includes(names.rare));
    assert.ok(!(await visitor.content()).includes(hiddenTitle));
    for (const id of ["species-community-photo", "species-community-tank", "species-community-partner"]) {
      for (const element of await byTestId(visitor, id).all()) {
        const bounds = await element.boundingBox();
        assert.ok(bounds.height >= 48 && bounds.width >= 48, `${id} is at least 48dp`);
      }
    }
    await visitor.screenshot({ path: `/tmp/task-9.5-species-community-${scheme}.png` });
    await byTestId(visitor, "species-community-partner").click();
    await visitor.waitForURL(`${WEB_URL}/species/${slugs.partner}`);
    await context.close();
  }
  console.log("TASK-9.5: photos, tank count without the private tank, partners from three tanks, touch targets and light/dark passed.");
} finally {
  try {
    if (storyId !== null) {
      const items = sql(`select media_item_id from media_link where story_id = ${storyId}`).split("\n").filter(Boolean).join(",") || "0";
      sql(`delete from slug_alias where story_id = ${storyId}`);
      sql(`delete from media_link where story_id = ${storyId}`);
      sql(`delete from media_variant where media_item_id in (${items})`);
      sql(`delete from media_item where id in (${items})`);
      sql(`delete from story where id = ${storyId}`);
    }
    sql(`delete from tank where title like 'Speccom ${run} %'`);
    sql(`delete from species where slug like 'speccom-${run}-%'`);
  } finally {
    await browser.close();
  }
}
