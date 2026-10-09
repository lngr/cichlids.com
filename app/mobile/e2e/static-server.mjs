#!/usr/bin/env node
// Serves a static Expo web export (see e2e/write-stack.sh) with SPA fallback: `expo export
// --platform web` for this app produces a single index.html plus hashed assets, so every path
// that is not an existing file must also serve index.html for expo-router's client-side routing
// to take over. No new dependency: Node's http/fs cover this in a few lines.
import http from "node:http";
import fs from "node:fs";
import path from "node:path";

const [, , rootArg, portArg] = process.argv;
if (!rootArg || !portArg) {
  console.error("Usage: node static-server.mjs <root-dir> <port>");
  process.exit(2);
}

const root = path.resolve(rootArg);
const port = Number(portArg);

const CONTENT_TYPES = {
  ".html": "text/html; charset=utf-8",
  ".js": "text/javascript; charset=utf-8",
  ".css": "text/css; charset=utf-8",
  ".json": "application/json; charset=utf-8",
  ".png": "image/png",
  ".jpg": "image/jpeg",
  ".jpeg": "image/jpeg",
  ".svg": "image/svg+xml",
  ".ico": "image/x-icon",
  ".ttf": "font/ttf",
  ".woff": "font/woff",
  ".woff2": "font/woff2",
};

function send(res, status, filePath) {
  res.writeHead(status, { "Content-Type": CONTENT_TYPES[path.extname(filePath)] ?? "application/octet-stream" });
  fs.createReadStream(filePath).pipe(res);
}

const server = http.createServer((req, res) => {
  const requestPath = decodeURIComponent(new URL(req.url ?? "/", "http://localhost").pathname);
  const candidate = path.join(root, requestPath);

  // Only ever serve files inside root, and only regular files (a directory or a path escaping
  // root falls through to the SPA fallback below).
  if (candidate.startsWith(root) && fs.existsSync(candidate) && fs.statSync(candidate).isFile()) {
    send(res, 200, candidate);
    return;
  }

  const indexPath = path.join(root, "index.html");
  if (fs.existsSync(indexPath)) {
    send(res, 200, indexPath);
    return;
  }

  res.writeHead(404);
  res.end("Not found");
});

server.listen(port, "0.0.0.0", () => {
  console.log(`Serving ${root} on http://0.0.0.0:${port}`);
});
