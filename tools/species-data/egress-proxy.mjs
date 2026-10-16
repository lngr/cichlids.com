// Egress proxy of the agent cage. The cage has no network of its own; its programs reach the
// internet through this proxy, which runs outside the cage and listens on a unix socket. It serves
// CONNECT (HTTPS and other tunnels) and plain HTTP requests in absolute form, and only for ports 80
// and 443 of public addresses: it resolves the host itself, refuses when any address belongs to a
// loopback, private, link-local, shared, multicast, documentation or otherwise reserved range
// (an IPv4 address inside an IPv6 form counts as that IPv4 address), and connects to one of the
// vetted addresses, so a second lookup (DNS rebinding) cannot lead elsewhere. The proxy never logs
// a request, a header or a body.
import { lookup as dnsLookup } from "node:dns/promises";
import http from "node:http";
import net from "node:net";
import { relayResponse } from "./relay-response.mjs";

export const ALLOWED_PORTS = [80, 443];

// Limits of the proxy (milliseconds, connections): the wait for a connection to a target address,
// the time a tunnel or an upstream connection may stay silent, and the number of client
// connections that are open at once.
export const CONNECT_TIMEOUT_MS = 10_000;
export const IDLE_TIMEOUT_MS = 300_000;
export const MAX_CONNECTIONS = 128;

// Headers that describe one connection or address the proxy; they are not forwarded.
const HOP_BY_HOP = [
  "connection", "keep-alive", "proxy-authenticate", "proxy-authorization", "proxy-connection", "te",
  "trailer", "transfer-encoding", "upgrade",
];

// IPv4 ranges that are not public: [first address, prefix length, reason].
const IPV4_RANGES = [
  ["0.0.0.0", 8, "unspecified"],
  ["10.0.0.0", 8, "private"],
  ["100.64.0.0", 10, "carrier-grade NAT"],
  ["127.0.0.0", 8, "loopback"],
  ["169.254.0.0", 16, "link-local"],
  ["172.16.0.0", 12, "private"],
  ["192.0.0.0", 24, "reserved"],
  ["192.0.2.0", 24, "documentation"],
  ["192.88.99.0", 24, "reserved"],
  ["192.168.0.0", 16, "private"],
  ["198.18.0.0", 15, "reserved"],
  ["198.51.100.0", 24, "documentation"],
  ["203.0.113.0", 24, "documentation"],
  ["224.0.0.0", 4, "multicast"],
  ["255.255.255.255", 32, "broadcast"],
  ["240.0.0.0", 4, "reserved"],
];

// IPv6 ranges that are not public: [first address, prefix length, reason].
const IPV6_RANGES = [
  ["::", 96, "unspecified or reserved"],
  ["100::", 64, "reserved"],
  ["2001::", 23, "reserved"],
  ["2001:db8::", 32, "documentation"],
  ["3fff::", 20, "documentation"],
  ["5f00::", 16, "reserved"],
  ["64:ff9b:1::", 48, "reserved"],
  ["::ffff:0:0:0", 96, "reserved"],
  ["fc00::", 7, "unique local"],
  ["fe80::", 10, "link-local"],
  ["fec0::", 10, "reserved"],
  ["ff00::", 8, "multicast"],
];

// Returns the four bytes of a dotted IPv4 address, or null.
function parseIPv4(text) {
  if (net.isIP(text) !== 4) return null;
  return text.split(".").map(Number);
}

// Returns the sixteen bytes of an IPv6 address (also with a dotted IPv4 tail), or null.
function parseIPv6(text) {
  if (net.isIP(text) !== 6) return null;
  let rest = text.split("%")[0];
  const tail = /(\d+\.\d+\.\d+\.\d+)$/.exec(rest);
  if (tail) {
    const [a, b, c, d] = tail[1].split(".").map(Number);
    const high = ((a << 8) | b).toString(16);
    const low = ((c << 8) | d).toString(16);
    rest = `${rest.slice(0, -tail[1].length)}${high}:${low}`;
  }
  const [head, end, extra] = rest.split("::");
  if (extra !== undefined) return null;
  const groupsOf = (part) => (part ? part.split(":") : []);
  const first = groupsOf(head);
  const last = end === undefined ? [] : groupsOf(end);
  const fill = end === undefined ? 0 : 8 - first.length - last.length;
  const groups = [...first, ...Array(fill).fill("0"), ...last].map((g) => parseInt(g, 16));
  const invalid = (g) => !Number.isInteger(g) || g < 0 || g > 0xffff;
  if (groups.length !== 8 || groups.some(invalid)) return null;
  return groups.flatMap((g) => [g >> 8, g & 0xff]);
}

// True when the first bits bits of address and prefix agree.
function inPrefix(address, prefix, bits) {
  for (let i = 0; i < prefix.length && bits > 0; i++, bits -= 8) {
    const mask = bits >= 8 ? 0xff : (0xff << (8 - bits)) & 0xff;
    if ((address[i] & mask) !== (prefix[i] & mask)) return false;
  }
  return true;
}

function reasonOfIPv4(bytes) {
  for (const [first, bits, reason] of IPV4_RANGES) {
    if (inPrefix(bytes, parseIPv4(first), bits)) return reason;
  }
  return null;
}

/**
 * Returns the reason when an IP address (text) is not a public unicast address, else null. The
 * ranges are loopback, unspecified, private (10/8, 172.16/12, 192.168/16), link-local,
 * carrier-grade NAT, unique local, multicast, broadcast, documentation and other reserved ranges.
 * An IPv6 form that holds an IPv4 address (IPv4-mapped, NAT64 and 6to4) gets the verdict of that
 * IPv4 address. Text that is not an IP address counts as invalid.
 */
export function addressReason(address) {
  const v4 = parseIPv4(address);
  if (v4) return reasonOfIPv4(v4);
  const v6 = parseIPv6(address);
  if (!v6) return "invalid address";
  const embedded = [];
  if (inPrefix(v6, parseIPv6("::ffff:0:0"), 96)) embedded.push(v6.slice(12));
  if (inPrefix(v6, parseIPv6("64:ff9b::"), 96)) embedded.push(v6.slice(12));
  if (inPrefix(v6, parseIPv6("2002::"), 16)) embedded.push(v6.slice(2, 6));
  for (const bytes of embedded) {
    const reason = reasonOfIPv4(bytes);
    if (reason) return reason;
  }
  for (const [first, bits, reason] of IPV6_RANGES) {
    if (inPrefix(v6, parseIPv6(first), bits)) return reason;
  }
  return null;
}

/**
 * Decides on a target. addresses are the IP addresses that the host resolved to. Returns
 * { allow: true, addresses } when the port is allowed and every address is public, else
 * { allow: false, reason } with a short text for the 403 answer.
 */
export function decideTarget({ port, addresses }) {
  if (!ALLOWED_PORTS.includes(port)) {
    return { allow: false, reason: `port ${port} is not allowed` };
  }
  if (addresses.length === 0) return { allow: false, reason: "the host has no address" };
  for (const address of addresses) {
    const reason = addressReason(address);
    if (reason) return { allow: false, reason: `the host resolves to a ${reason} address` };
  }
  return { allow: true, addresses };
}

// Resolves host to its addresses; an IP literal is its own address.
async function resolveAll(host, lookup) {
  if (net.isIP(host)) return [host];
  const found = await lookup(host, { all: true });
  return found.map((entry) => entry.address);
}

// Splits "host:port" or "[v6]:port" into { host, port } or returns null.
export function parseAuthority(text, defaultPort = null) {
  const match = /^(?:\[([^\]\s]+)\]|([^:\[\]\s]+))(?::(\d{1,5}))?$/.exec(text);
  if (!match) return null;
  const port = match[3] === undefined ? defaultPort : Number(match[3]);
  return port === null ? null : { host: match[1] ?? match[2], port };
}

const without = (headers, names) =>
  Object.fromEntries(Object.entries(headers).filter(([n]) => !names.includes(n)));

// Returns the headers without the hop-by-hop headers, including those that the connection header
// names.
function forwardHeaders(headers) {
  const named = String(headers.connection ?? "").split(",").map((h) => h.trim().toLowerCase())
    .filter(Boolean);
  return without(headers, [...HOP_BY_HOP, ...named]);
}

/**
 * Opens a TCP connection to the first address that accepts it within timeoutMs and resolves with
 * the socket; rejects with the last error when none does. open (default net.connect) creates the
 * socket and is injectable for tests.
 */
export function connectAny(addresses, port, timeoutMs = CONNECT_TIMEOUT_MS, open = net.connect) {
  return new Promise((done, fail) => {
    const attempt = (index) => {
      const socket = open({ host: addresses[index], port });
      let over = false;
      const failed = (error) => {
        if (over) return;
        over = true;
        socket.destroy();
        if (index + 1 < addresses.length) attempt(index + 1);
        else fail(error);
      };
      socket.setTimeout(timeoutMs);
      socket.once("connect", () => {
        socket.removeAllListeners("error");
        socket.removeAllListeners("timeout");
        socket.setTimeout(0);
        done(socket);
      });
      socket.once("error", failed);
      socket.once("timeout", () => failed(new Error(`connect to ${addresses[index]} timed out`)));
    };
    attempt(0);
  });
}

function refuse(res, status, reason) {
  res.writeHead(status, {
    "content-type": "text/plain", "content-length": Buffer.byteLength(reason) + 1,
    connection: "close",
  });
  res.end(`${reason}\n`);
}

/**
 * Starts the proxy on the unix socket socketPath and returns { close }. lookup (default
 * dns.lookup of node:dns/promises) resolves a host name with { all: true } and is injectable for
 * tests; connect (default connectAny with connectTimeoutMs) opens the connection to the target.
 * A tunnel or upstream connection that stays silent for idleTimeoutMs is closed, and the server
 * takes at most maxConnections client connections at once. An upstream answer with a status
 * outside 200..599, or one that node refuses to forward, gives 502 to the client. close() stops
 * the server and ends open connections.
 */
export async function startEgressProxy({
  socketPath, lookup = dnsLookup, connectTimeoutMs = CONNECT_TIMEOUT_MS,
  idleTimeoutMs = IDLE_TIMEOUT_MS, maxConnections = MAX_CONNECTIONS,
  connect = (addresses, port) => connectAny(addresses, port, connectTimeoutMs),
}) {
  const sockets = new Set();
  const server = http.createServer(async (req, res) => {
    let target;
    try {
      target = new URL(req.url);
    } catch {
      refuse(res, 400, "the proxy takes absolute URLs only");
      req.resume();
      return;
    }
    if (target.protocol !== "http:") {
      refuse(res, 403, "only http URLs are proxied in plain form");
      req.resume();
      return;
    }
    const host = target.hostname.replace(/^\[|\]$/g, "");
    const port = target.port ? Number(target.port) : 80;
    let decision;
    try {
      decision = decideTarget({ port, addresses: await resolveAll(host, lookup) });
    } catch {
      refuse(res, 502, "the host does not resolve");
      req.resume();
      return;
    }
    if (!decision.allow) {
      refuse(res, 403, decision.reason);
      req.resume();
      return;
    }
    let upstream;
    try {
      upstream = await connect(decision.addresses, port);
    } catch {
      refuse(res, 502, "the connection failed");
      req.resume();
      return;
    }
    sockets.add(upstream);
    upstream.on("close", () => sockets.delete(upstream));
    upstream.setTimeout(idleTimeoutMs, () => upstream.destroy());
    // Without an agent, createConnection hands the vetted connection to the request.
    const forward = http.request({
      createConnection: () => upstream, method: req.method,
      path: `${target.pathname}${target.search}`,
      headers: { ...forwardHeaders(req.headers), host: target.host },
    }, (answer) => {
      if (!relayResponse(res, answer, forwardHeaders(answer.headers))) {
        refuse(res, 502, "the upstream answer is not valid");
      }
    });
    // A protocol switch (status 101) is not proxied.
    forward.on("upgrade", (answer, socket) => {
      socket.destroy();
      if (!res.headersSent) refuse(res, 502, "the upstream answer is not valid");
    });
    forward.on("error", () => {
      if (res.headersSent) res.destroy();
      else refuse(res, 502, "the request failed");
    });
    res.on("close", () => {
      if (!res.writableEnded) forward.destroy();
    });
    req.on("error", () => forward.destroy());
    req.pipe(forward);
  });
  server.maxConnections = maxConnections;

  server.on("connect", async (req, clientSocket, head) => {
    clientSocket.on("error", () => clientSocket.destroy());
    const deny = (status, reason) => {
      const answer = `HTTP/1.1 ${status}\r\ncontent-length: ${reason.length + 1}\r\n` +
        `connection: close\r\n\r\n${reason}\n`;
      clientSocket.end(answer);
    };
    const authority = parseAuthority(req.url, 443);
    if (!authority) return deny("400 Bad Request", "bad CONNECT target");
    let decision;
    try {
      const addresses = await resolveAll(authority.host, lookup);
      decision = decideTarget({ port: authority.port, addresses });
    } catch {
      return deny("502 Bad Gateway", "the host does not resolve");
    }
    if (!decision.allow) return deny("403 Forbidden", decision.reason);
    let upstream;
    try {
      upstream = await connect(decision.addresses, authority.port);
    } catch {
      return deny("502 Bad Gateway", "the connection failed");
    }
    sockets.add(upstream);
    upstream.on("close", () => sockets.delete(upstream));
    upstream.on("error", () => clientSocket.destroy());
    clientSocket.on("close", () => upstream.destroy());
    upstream.on("close", () => clientSocket.destroy());
    for (const socket of [clientSocket, upstream]) {
      socket.setTimeout(idleTimeoutMs, () => socket.destroy());
    }
    clientSocket.write("HTTP/1.1 200 Connection Established\r\n\r\n");
    if (head.length > 0) upstream.write(head);
    upstream.pipe(clientSocket);
    clientSocket.pipe(upstream);
  });
  server.on("connection", (socket) => {
    sockets.add(socket);
    socket.on("close", () => sockets.delete(socket));
  });
  server.on("clientError", (error, socket) => socket.destroy());

  await new Promise((done, fail) => {
    server.once("error", fail);
    server.listen(socketPath, done);
  });
  return {
    close: () => new Promise((done) => {
      server.close(() => done());
      for (const socket of sockets) socket.destroy();
    }),
  };
}
