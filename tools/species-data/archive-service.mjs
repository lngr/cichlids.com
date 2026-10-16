// The archive search service. A caged agent has no docker access and no network of its own, so the
// forum archive search runs outside the cage: this service listens on a unix socket that the cage
// mounts and answers searches with the text that archive-search.mjs prints.
//
// Protocol: one request per connection. The client sends one JSON line {"term": "...", "limit": n}
// and the service answers the text of the search, or one line "error: <message>" for an invalid
// request or a failed search, and closes the connection. The service runs the queries with an
// injected query function (see createDockerQuery), takes a bounded number of queries at once,
// bounds the time of a query and the size of an answer, and never logs a term or a result.
import net from "node:net";
import {
  ArchiveError, MAX_OUTPUT_BYTES, QUERY_TIMEOUT_MS, capOutput, formatResults, publicMessage,
  validateRequest,
} from "./archive-query.mjs";

export const MAX_QUERIES = 4;
export const REQUEST_TIMEOUT_MS = 10_000;
export const MAX_CONNECTIONS = 32;

// A request line holds at most 100 characters of term, which JSON escapes to a few hundred
// bytes.
const MAX_REQUEST_BYTES = 4096;

/** The answer line for an error. */
export const errorLine = (error) => `error: ${publicMessage(error)}\n`;

/**
 * Starts the service on the unix socket socketPath and returns { socketPath, close }. query is
 * called as query({ term, limit, signal }) and resolves with the raw psql output (see
 * createDockerQuery). Its signal aborts when the query exceeds timeoutMs (the client then gets the
 * timeout error) or when close() ends the connection. At most maxQueries queries run at once; a further
 * request answers the busy error at once, and a query that ignores its signal keeps its place
 * until it ends. A client that sends no request line within requestTimeoutMs gets an error line,
 * and so does one that sends more than 4096 bytes. An answer is cut at maxOutputBytes. close()
 * stops the server, ends open connections and aborts the running queries.
 */
export async function startArchiveService({
  socketPath, query, timeoutMs = QUERY_TIMEOUT_MS, maxQueries = MAX_QUERIES,
  maxOutputBytes = MAX_OUTPUT_BYTES, requestTimeoutMs = REQUEST_TIMEOUT_MS,
}) {
  let running = 0;
  const sockets = new Set();

  // Returns the text that answers the request line; clientGone aborts when the connection closes.
  async function answer(line, clientGone) {
    let request;
    try {
      try {
        request = JSON.parse(line);
      } catch {
        throw new ArchiveError("invalid", "the request is not one JSON line");
      }
      request = validateRequest(request);
      if (running >= maxQueries) throw new ArchiveError("busy");
    } catch (error) {
      return errorLine(error);
    }
    const expired = new AbortController();
    const signal = AbortSignal.any([clientGone, expired.signal]);
    running += 1;
    const work = Promise.resolve()
      .then(() => query({ ...request, signal }))
      .finally(() => {
        running -= 1;
      });
    let timer;
    const deadline = new Promise((resolve, reject) => {
      timer = setTimeout(() => {
        expired.abort();
        reject(new ArchiveError("timeout"));
      }, timeoutMs);
    });
    try {
      const raw = await Promise.race([work, deadline]);
      if (typeof raw !== "string") throw new ArchiveError("failed");
      return capOutput(formatResults(raw), maxOutputBytes);
    } catch (error) {
      return errorLine(error);
    } finally {
      clearTimeout(timer);
    }
  }

  const server = net.createServer({ allowHalfOpen: true }, (socket) => {
    sockets.add(socket);
    const gone = new AbortController();
    let received = false;
    let buffered = "";
    const reply = (text) => {
      if (!socket.destroyed) socket.end(text);
    };
    const receive = (line) => {
      if (received) return;
      received = true;
      socket.setTimeout(0);
      answer(line, gone.signal).then(reply);
    };
    socket.setEncoding("utf8");
    socket.setTimeout(requestTimeoutMs, () => {
      if (received) return;
      received = true;
      reply("error: no request line received\n");
    });
    socket.on("data", (chunk) => {
      if (received) return;
      buffered += chunk;
      const end = buffered.indexOf("\n");
      if (end >= 0) receive(buffered.slice(0, end));
      else if (Buffer.byteLength(buffered) > MAX_REQUEST_BYTES) {
        received = true;
        reply("error: the request line is too long\n");
      }
    });
    socket.on("end", () => receive(buffered));
    socket.on("error", () => {});
    socket.on("close", () => {
      sockets.delete(socket);
      gone.abort();
    });
  });
  server.maxConnections = MAX_CONNECTIONS;
  await new Promise((done, fail) => {
    server.once("error", fail);
    server.listen(socketPath, done);
  });
  return {
    socketPath,
    close: () => new Promise((done) => {
      server.close(() => done());
      for (const socket of sockets) socket.destroy();
    }),
  };
}
