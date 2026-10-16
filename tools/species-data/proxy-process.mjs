// Child process that hosts the credential proxy, the egress proxy and the archive search service
// of one runner call. They handle input of an agent (the egress proxy even the answers of
// arbitrary web servers), so they run in a process of their own: a crash of one ends this child
// and leaves the runner, which audits the write roots after every attempt, alive. The runner
// learns about the end of the child from the died promise and treats the attempt as failed.
//
// The parent starts the child with startProxyProcess and sends it the configuration over the
// IPC channel; the child answers { type: "ready" } when all sockets listen, or { type: "failed",
// message } when one does not start. A { type: "stop" } message or the end of the parent makes
// the child close everything and exit. The child has an empty environment and no output streams,
// so nothing of a request or a credential can reach a log. The archive search runs docker with
// the environment that the parent names in the configuration (archiveEnv, the docker variables of
// the parent) and nothing else.
import { fork } from "node:child_process";
import { resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { createDockerQuery } from "./archive-query.mjs";
import { startArchiveService } from "./archive-service.mjs";
import { startCredentialProxy } from "./credential-proxy.mjs";
import { startEgressProxy } from "./egress-proxy.mjs";

const SELF = fileURLToPath(import.meta.url);

/**
 * Runs inside the child: waits for the "start" message of the parent over channel (default
 * process), starts the proxies and the archive search service and reports. Resolves when the child
 * has closed them after a "stop" message or a lost parent.
 */
export async function runProxyChild(channel = process) {
  const config = await new Promise((done) => {
    channel.once("message", done);
    channel.once("disconnect", () => done(null));
  });
  if (config === null || config.type !== "start") return;
  const proxies = [];
  try {
    proxies.push(await startCredentialProxy({
      billing: config.billing, apiKeyFile: config.apiKeyFile,
      credentialsFile: config.credentialsFile, socketPath: config.apiSocket,
    }));
    proxies.push(await startEgressProxy({ socketPath: config.egressSocket }));
    proxies.push(await startArchiveService({
      socketPath: config.archiveSocket, query: createDockerQuery({ env: config.archiveEnv }),
    }));
  } catch (error) {
    await Promise.all(proxies.map((proxy) => proxy.close()));
    await new Promise((done) => channel.send({ type: "failed", message: error.message }, done));
    return;
  }
  const stopped = new Promise((done) => {
    channel.on("message", (message) => message?.type === "stop" && done());
    channel.once("disconnect", done);
  });
  channel.send({ type: "ready" });
  await stopped;
  await Promise.all(proxies.map((proxy) => proxy.close()));
}

// How a child ended, as text.
const endText = (code, signal) => (signal ? `signal ${signal}` : `exit code ${code}`);

/**
 * Starts the proxy child with fork (injectable) and resolves, once all sockets listen, with
 * { died, close }. died is a promise that resolves with a text ("exit code 1", "signal SIGKILL")
 * when the child ends, also after close; close() stops the proxies and the child (SIGKILL after
 * stopGraceMs) and resolves when the child is gone. billing, apiKeyFile and credentialsFile are
 * those of startCredentialProxy; apiSocket, egressSocket and archiveSocket are the socket paths;
 * archiveEnv is the environment of the docker process of the archive search (see dockerEnv).
 * Rejects when a socket fails to start, when the child ends first or when it is not ready within
 * readyTimeoutMs.
 */
export async function startProxyProcess({
  billing, apiKeyFile, credentialsFile, apiSocket, egressSocket, archiveSocket,
  archiveEnv = {}, forkImpl = fork, readyTimeoutMs = 15_000, stopGraceMs = 5_000,
}) {
  const child = forkImpl(SELF, [], {
    env: {}, execArgv: [], stdio: ["ignore", "ignore", "ignore", "ipc"],
  });
  const died = new Promise((done) => {
    child.once("exit", (code, signal) => done(endText(code, signal)));
  });
  const gone = new Promise((done) => child.once("exit", done));
  const close = async () => {
    if (child.exitCode !== null || child.signalCode !== null) return;
    const timer = setTimeout(() => child.kill("SIGKILL"), stopGraceMs);
    try {
      child.send({ type: "stop" });
    } catch {
      child.kill("SIGKILL");
    }
    await gone;
    clearTimeout(timer);
  };
  try {
    await new Promise((done, fail) => {
      const timer = setTimeout(
        () => fail(new Error("the proxy process did not start in time")), readyTimeoutMs,
      );
      const settle = (action) => (...args) => {
        clearTimeout(timer);
        action(...args);
      };
      child.once("message", settle((message) => {
        if (message?.type === "ready") done();
        else fail(new Error(`the proxies did not start: ${message?.message ?? "no reason"}`));
      }));
      child.once("exit", settle((code, signal) => fail(
        new Error(`the proxy process ended before it was ready (${endText(code, signal)})`),
      )));
      child.once("error", settle(fail));
      child.send({
        type: "start", billing, apiKeyFile, credentialsFile, apiSocket, egressSocket,
        archiveSocket, archiveEnv,
      });
    });
  } catch (error) {
    child.kill("SIGKILL");
    throw error;
  }
  return { died, close };
}

if (process.send && process.argv[1] && resolve(process.argv[1]) === SELF) {
  await runProxyChild();
  process.exit(0);
}
