#!/usr/bin/env node
// Init program inside the agent cage. The cage has its own network namespace with a loopback
// interface only, and the proxies listen on unix sockets that the cage mounts. This program opens
// TCP listeners on 127.0.0.1 that relay to those sockets, starts the command with the inherited
// standard streams, passes SIGTERM and SIGINT on to it and exits with its exit code (128 plus the
// signal number when a signal ended it).
//
//   node cage-init.mjs <port>=<socket>[,<port>=<socket>]... -- <command> [argument]...
//
// The relay specification is one argument; a socket path holds neither a comma nor a newline.
import { spawn } from "node:child_process";
import { constants } from "node:os";
import net from "node:net";
import { resolve } from "node:path";
import { fileURLToPath } from "node:url";

/** Parses "<port>=<socket>,..." into [{ port, socket }]. Throws an Error for a malformed entry. */
export function parseRelaySpec(spec) {
  return String(spec).split(",").map((entry) => {
    const match = /^(\d{1,5})=(\/[^\n,]+)$/.exec(entry);
    if (!match || Number(match[1]) < 1 || Number(match[1]) > 65535) {
      throw new Error(`bad relay entry: ${entry}`);
    }
    return { port: Number(match[1]), socket: match[2] };
  });
}

/**
 * Starts a TCP listener on 127.0.0.1 per { port, socket } pair; every connection relays to the unix
 * socket. Returns { close } after all listeners are up. A connection whose socket cannot be reached
 * is closed.
 */
export async function startRelays(pairs) {
  const open = new Set();
  const servers = pairs.map(({ port, socket }) => net.createServer((client) => {
    const upstream = net.connect(socket);
    open.add(client).add(upstream);
    for (const side of [client, upstream]) {
      side.on("error", () => {
        client.destroy();
        upstream.destroy();
      });
      side.on("close", () => {
        open.delete(side);
        client.destroy();
        upstream.destroy();
      });
    }
    client.pipe(upstream);
    upstream.pipe(client);
  }));
  await Promise.all(servers.map((server, i) => new Promise((done, fail) => {
    server.once("error", fail);
    server.listen(pairs[i].port, "127.0.0.1", done);
  })));
  return {
    close: () => {
      for (const server of servers) server.close();
      for (const socket of open) socket.destroy();
    },
  };
}

/**
 * Runs the command line argv (without node and script): the relay specification, "--" and the
 * command. Resolves with the exit code.
 */
export async function main(argv, { signals = process, stderr = process.stderr } = {}) {
  const [spec, separator, command, ...args] = argv;
  if (spec === undefined || separator !== "--" || command === undefined) {
    stderr.write(
      "Usage: cage-init.mjs <port>=<socket>[,<port>=<socket>]... -- <command> [argument]...\n",
    );
    return 2;
  }
  let relays;
  try {
    relays = await startRelays(parseRelaySpec(spec));
  } catch (error) {
    stderr.write(`cage-init: ${error.message}\n`);
    return 2;
  }
  const code = await new Promise((done) => {
    const child = spawn(command, args, { stdio: "inherit" });
    const forward = (signal) => child.kill(signal);
    const names = ["SIGTERM", "SIGINT"];
    for (const name of names) signals.on(name, forward);
    const finish = (value) => {
      for (const name of names) signals.off(name, forward);
      done(value);
    };
    child.on("error", (error) => {
      stderr.write(`cage-init: ${command}: ${error.message}\n`);
      finish(127);
    });
    child.on("close", (exitCode, signal) => {
      finish(signal ? 128 + (constants.signals[signal] ?? 0) : exitCode ?? 1);
    });
  });
  relays.close();
  return code;
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  process.exitCode = await main(process.argv.slice(2));
  process.exit();
}
