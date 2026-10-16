// Credential proxy for the Claude agents. The agent cage holds only placeholder credentials; this
// proxy runs outside the cage, listens on a unix socket that the cage mounts, adds the real
// credential to every request and forwards it to the Anthropic API. It serves the methods GET, POST
// and HEAD for the paths of the messages and models endpoints and answers 400 to everything else,
// so the credential reaches the fixed upstream host and nothing but those endpoints (the Files and
// batch endpoints of the same workspace stay out of reach).
//
// The credential is read fresh for each request: "api-key" takes the key file and sets x-api-key,
// "subscription" takes the claude.ai login file and sets authorization with its access token. The
// proxy never refreshes a token (a running claude login on the host keeps the file current), and it
// never logs a header or a body.
import { accessSync, constants, readFileSync, statSync } from "node:fs";
import http from "node:http";
import https from "node:https";
import { relayResponse } from "./relay-response.mjs";

export const DEFAULT_UPSTREAM = "https://api.anthropic.com";

const ALLOWED_METHODS = ["GET", "POST", "HEAD"];

// Headers that describe one connection and are not forwarded (RFC 9110 section 7.6.1).
const HOP_BY_HOP = [
  "connection", "keep-alive", "proxy-authenticate", "proxy-authorization", "proxy-connection", "te",
  "trailer", "transfer-encoding", "upgrade",
];

// The paths that the proxy serves: the messages endpoint, token counting, and the models endpoint
// with its sub-paths (a model id).
const ALLOWED_PATHS = ["/v1/messages", "/v1/messages/count_tokens", "/v1/models"];
const ALLOWED_PREFIXES = ["/v1/models/"];

/**
 * Returns null when the key file is a readable regular file with non-whitespace content, else the
 * reason as text. The content is only tested and never returned or logged.
 */
export function checkApiKeyFile(apiKeyFile) {
  let stat;
  try {
    stat = statSync(apiKeyFile);
  } catch {
    return "does not exist";
  }
  if (!stat.isFile()) return "is not a regular file";
  try {
    accessSync(apiKeyFile, constants.R_OK);
    return readFileSync(apiKeyFile, "utf8").trim() === "" ? "is empty" : null;
  } catch {
    return "is not readable";
  }
}

// Reads the claudeAiOauth entry of the login file. Returns { accessToken, expiresAt } or
// { problem }.
function readLogin(credentialsFile) {
  let text;
  try {
    text = readFileSync(credentialsFile, "utf8");
  } catch (error) {
    return { problem: error.code === "ENOENT" ? "does not exist" : "is not readable" };
  }
  let login;
  try {
    login = JSON.parse(text)?.claudeAiOauth;
  } catch {
    return { problem: "is not valid JSON" };
  }
  if (login === null || typeof login !== "object") return { problem: "holds no claude.ai login" };
  if (typeof login.accessToken !== "string" || login.accessToken === "") {
    return { problem: "holds no access token" };
  }
  if (typeof login.expiresAt !== "number" || !Number.isFinite(login.expiresAt)) {
    return { problem: "holds no expiry time" };
  }
  return { accessToken: login.accessToken, expiresAt: login.expiresAt };
}

/**
 * Returns null when the login file of the claude.ai subscription holds an access token that has
 * not expired, else the reason as text. expiresAt of the file is in epoch milliseconds.
 */
export function checkLoginFile(credentialsFile, now = Date.now()) {
  const login = readLogin(credentialsFile);
  if (login.problem) return login.problem;
  return login.expiresAt <= now ? "has an expired access token" : null;
}

/**
 * Reads the credential of a billing. Returns { name, value, problem: null } with the header to set,
 * or { problem, message } where message is the text of the authentication error for the client.
 */
export function loadCredential({ billing, apiKeyFile, credentialsFile, now = Date.now() }) {
  if (billing === "api-key") {
    const problem = checkApiKeyFile(apiKeyFile);
    if (problem) return { problem, message: "API key file is missing or unusable" };
    return { name: "x-api-key", value: readFileSync(apiKeyFile, "utf8").trim(), problem: null };
  }
  if (billing === "subscription") {
    const login = readLogin(credentialsFile);
    if (login.problem) {
      return { problem: login.problem, message: "subscription login missing or unreadable" };
    }
    if (login.expiresAt <= now) {
      return { problem: "has an expired access token", message: "subscription login expired" };
    }
    return { name: "authorization", value: `Bearer ${login.accessToken}`, problem: null };
  }
  throw new Error("billing must be one of: api-key, subscription");
}

const errorBody = (type, message) => JSON.stringify({ type: "error", error: { type, message } });

/**
 * Returns true when the request target is allowed: the path (without the query) is /v1/messages,
 * /v1/messages/count_tokens, /v1/models or below /v1/models/, and the target holds no "//", no
 * "://", no backslash and no ".." segment, in the raw form and after percent decoding.
 */
export function allowedTarget(url) {
  let decoded;
  try {
    decoded = decodeURIComponent(url);
  } catch {
    return false;
  }
  return [url, decoded].every((form) => {
    const path = form.split("?")[0];
    const listed = ALLOWED_PATHS.includes(path) || ALLOWED_PREFIXES.some((p) => path.startsWith(p));
    return listed && !form.includes("//") && !form.includes("://") && !form.includes("\\") &&
      !path.split("/").includes("..");
  });
}

function reply(res, status, body, headers = {}) {
  res.writeHead(status, {
    "content-type": "application/json", "content-length": Buffer.byteLength(body), ...headers,
  });
  res.end(body);
}

// Returns the headers without the hop-by-hop headers, including those that the connection header
// names.
function withoutHopByHop(headers) {
  const named = String(headers.connection ?? "").split(",").map((h) => h.trim().toLowerCase())
    .filter(Boolean);
  const copy = {};
  for (const [name, value] of Object.entries(headers)) {
    if (!HOP_BY_HOP.includes(name) && !named.includes(name)) copy[name] = value;
  }
  return copy;
}

/**
 * Starts the proxy on the unix socket socketPath and returns { socketPath, close }. billing is
 * "api-key" (key file apiKeyFile) or "subscription" (login file credentialsFile). A request with
 * another method than GET, POST or HEAD, or with a target that allowedTarget refuses, answers 400.
 * Every other request gets the credential of the billing and no other credential header; a
 * missing, unusable or expired credential answers 401 with an authentication_error body in the
 * format of the API. The request goes to upstream (default https://api.anthropic.com) with its
 * method, path and other headers, and request and response bodies stream. An upstream answer with
 * a status outside 200..599, or one that node refuses to forward, gives 502. close() stops the
 * server and ends open connections.
 */
export async function startCredentialProxy({
  billing, apiKeyFile, credentialsFile, socketPath, upstream = DEFAULT_UPSTREAM,
  now = () => Date.now(),
}) {
  if (billing !== "api-key" && billing !== "subscription") {
    throw new Error("billing must be one of: api-key, subscription");
  }
  const target = new URL(upstream);
  const transport = target.protocol === "https:" ? https : http;
  const server = http.createServer((req, res) => {
    if (!ALLOWED_METHODS.includes(req.method) || !allowedTarget(req.url)) {
      req.resume();
      const body = errorBody("invalid_request_error", "request not allowed by the proxy");
      reply(res, 400, body, { connection: "close" });
      return;
    }
    const credential = loadCredential({ billing, apiKeyFile, credentialsFile, now: now() });
    if (credential.problem) {
      req.resume();
      reply(res, 401, errorBody("authentication_error", credential.message));
      return;
    }
    const headers = withoutHopByHop(req.headers);
    delete headers["x-api-key"];
    delete headers.authorization;
    headers[credential.name] = credential.value;
    headers.host = target.host;
    const forward = transport.request({
      protocol: target.protocol, hostname: target.hostname, port: target.port || undefined,
      method: req.method, path: req.url, headers,
    }, (upstreamRes) => {
      if (!relayResponse(res, upstreamRes, withoutHopByHop(upstreamRes.headers))) {
        reply(res, 502, errorBody("api_error", "upstream answer not valid"));
      }
    });
    forward.on("error", () => {
      if (res.headersSent) res.destroy();
      else reply(res, 502, errorBody("api_error", "upstream request failed"));
    });
    res.on("close", () => {
      if (!res.writableEnded) forward.destroy();
    });
    req.on("error", () => forward.destroy());
    req.pipe(forward);
  });
  await new Promise((done, fail) => {
    server.once("error", fail);
    server.listen(socketPath, done);
  });
  return {
    socketPath,
    close: () => new Promise((done) => {
      server.close(() => done());
      server.closeAllConnections();
    }),
  };
}
