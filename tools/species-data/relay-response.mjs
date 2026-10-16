// Forwarding of an upstream HTTP answer to the client of a proxy. The upstream is not trusted to
// send a status or a header that node accepts, and a refusal of node inside an event callback is an
// uncaught exception, so the answer is checked and written inside a try block.

/** True when status is a final HTTP status that a proxy forwards (200 to 599). */
export const forwardableStatus = (status) =>
  Number.isInteger(status) && status >= 200 && status <= 599;

/**
 * Forwards the answer of an upstream request (an http.IncomingMessage) to the http.ServerResponse
 * res with the given headers and streams the body. The reason phrase of the upstream is dropped.
 * Returns true when the answer is on its way. Returns false, with nothing written to res and the
 * upstream answer destroyed, when the status is not forwardable or node refuses the status or a
 * header; the caller then answers 502.
 */
export function relayResponse(res, answer, headers) {
  if (!forwardableStatus(answer.statusCode)) {
    answer.destroy();
    return false;
  }
  try {
    res.writeHead(answer.statusCode, headers);
  } catch {
    answer.destroy();
    return false;
  }
  answer.on("error", () => res.destroy());
  res.on("error", () => answer.destroy());
  answer.pipe(res);
  return true;
}
