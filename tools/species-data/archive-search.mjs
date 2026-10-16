#!/usr/bin/env node
// Searches the cichlids.com forum archive for the species research agents and prints each matching
// thread with its citation: the legacy thread id, which is stable across imports, and the legacy
// forum URL, which the platform redirects to the thread. Prints post text only, never member names.
//
//   node tools/species-data/archive-search.mjs "<term>" [limit]
//
// Where CICHLIDS_ARCHIVE_SEARCH_SOCKET is set (a caged agent has no docker access), the search
// runs in the archive search service behind that unix socket (archive-service.mjs). Otherwise the
// search reads the local dev database read-only through "docker exec".
import net from 'node:net';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import {
  QUERY_TIMEOUT_MS, ArchiveError, createDockerQuery, dockerEnv, formatResults, publicMessage,
  validateRequest,
} from './archive-query.mjs';

const USAGE = 'Usage: node tools/species-data/archive-search.mjs "<term>" [limit]';

// The service takes its own query timeout; the client waits a little longer for the answer.
const ANSWER_TIMEOUT_MS = QUERY_TIMEOUT_MS + 10_000;

/**
 * Sends one search request to the service on the unix socket socketPath and resolves with its
 * answer text. Rejects with an Error when the service cannot be reached, closes the connection
 * without an answer or stays silent for timeoutMs.
 */
export function askService(socketPath, request, timeoutMs = ANSWER_TIMEOUT_MS) {
  return new Promise((done, fail) => {
    const socket = net.connect(socketPath);
    const chunks = [];
    socket.setTimeout(
      timeoutMs, () => socket.destroy(new Error('the service did not answer in time')),
    );
    socket.on('connect', () => socket.write(`${JSON.stringify(request)}\n`));
    socket.on('data', (chunk) => chunks.push(chunk));
    socket.on('error', fail);
    socket.on('close', (hadError) => {
      const text = Buffer.concat(chunks).toString('utf8');
      if (!hadError && text !== '') done(text);
      else if (!hadError) fail(new Error('the service closed the connection without an answer'));
    });
  });
}

/**
 * Runs the command line argv (without node and script) and resolves with the exit code. env selects
 * the service (CICHLIDS_ARCHIVE_SEARCH_SOCKET) or the docker query; query (default: docker with the
 * docker variables of env) and ask (default askService) are injectable. The answer of the service
 * goes to stdout unchanged, except that an error line goes to stderr with exit code 1.
 */
export async function main(argv, {
  env = process.env, stdout = process.stdout, stderr = process.stderr,
  query = createDockerQuery({ env: dockerEnv(env) }), ask = askService,
} = {}) {
  const [term, limitArg] = argv;
  if (!term) {
    stderr.write(`${USAGE}\n`);
    return 2;
  }
  let request;
  try {
    // A limit that is not a plain number stays a string and fails the validation.
    const limit = /^\d+$/.test(limitArg ?? '0') ? Number(limitArg) : limitArg;
    request = validateRequest({ term, ...(limitArg === undefined ? {} : { limit }) });
  } catch (error) {
    stderr.write(`error: ${publicMessage(error)}\n`);
    return 2;
  }
  let text;
  try {
    if (env.CICHLIDS_ARCHIVE_SEARCH_SOCKET) {
      text = await ask(env.CICHLIDS_ARCHIVE_SEARCH_SOCKET, request);
    } else {
      const raw = await query({ ...request, signal: AbortSignal.timeout(QUERY_TIMEOUT_MS) });
      text = formatResults(raw);
    }
  } catch (error) {
    const reason = error instanceof ArchiveError
      ? publicMessage(error) : `cannot reach the archive search service (${error.message})`;
    stderr.write(`error: ${reason}\n`);
    return 1;
  }
  if (text.startsWith('error: ')) {
    stderr.write(text);
    return 1;
  }
  stdout.write(text);
  return 0;
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  process.exitCode = await main(process.argv.slice(2));
}
