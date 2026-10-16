import fs from "node:fs";
import { execFileSync } from "node:child_process";

const apiUrl = "http://localhost:5046";
const keycloakUrl = "http://localhost:8180";
const composeFile = new URL("../../../stack/compose.yaml", import.meta.url).pathname;

async function json(url, options) {
  const response = await fetch(url, options);
  if (!response.ok) throw new Error(`${url}: ${response.status} ${await response.text()}`);
  return response.json();
}

const { access_token: token } = await json(`${keycloakUrl}/realms/cichlids/protocol/openid-connect/token`, {
  method: "POST",
  headers: { "Content-Type": "application/x-www-form-urlencoded" },
  body: new URLSearchParams({
    client_id: "cichlids-app", grant_type: "password", username: "dev-mod",
    password: "dev-password", // gitleaks:allow
  }),
});
const headers = { Authorization: `Bearer ${token}` };
const { profile } = await json(`${apiUrl}/api/me`, { headers });
if (!/^\d+$/.test(String(profile.id))) throw new Error("Unexpected fixture profile id");
const file = fs.readFileSync(new URL("../fixtures/upload-photo.jpg", import.meta.url));
const form = new FormData();
form.append("file", new Blob([file], { type: "image/jpeg" }), "maestro-fixture.jpg");
const draft = await json(`${apiUrl}/api/uploads`, { method: "POST", headers, body: form });
await json(`${apiUrl}/api/stories/${draft.id}/publish`, {
  method: "POST",
  headers: { ...headers, "Content-Type": "application/json" },
  body: JSON.stringify({ title: "Maestro rating target" }),
});

// The database name is literal so fixtures can only write to the isolated E2E database.
execFileSync("docker", ["compose", "-f", composeFile, "exec", "-T", "postgres", "psql",
  "-U", "cichlids", "-d", "cichlids_e2e", "-v", "ON_ERROR_STOP=1"], {
  stdio: ["pipe", "inherit", "inherit"],
  input: `
    BEGIN;
    INSERT INTO tank (profile_id, title, state, created_at)
      VALUES (${profile.id}, 'Maestro fixture tank', 'published', now());
    WITH thread AS (
      INSERT INTO discussion_thread (category, title, state, created_at, last_post_at, post_count)
        VALUES ('cichlids', 'Maestro fixture thread', 'archived', now(), now(), 1)
        RETURNING id
    )
    INSERT INTO discussion_post (thread_id, author_profile_id, body, sort, created_at)
      SELECT id, ${profile.id}, 'Synthetic aquarium discussion for native E2E.', 0, now() FROM thread;
    COMMIT;
  `,
});
console.log("Maestro fixtures: published picture, tank and archived discussion in cichlids_e2e");
