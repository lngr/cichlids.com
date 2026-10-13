const SAFE_METHODS = new Set(["GET", "HEAD", "OPTIONS"]);

export interface CookieSessionFetchOptions {
  fetch: typeof fetch;
  /** The CSRF token of the web session, null without a session. */
  getCsrfToken(): string | null;
  /** Called when the API answers 401: the session has ended on the server. */
  onUnauthorized(): void;
}

/**
 * The fetch of the web app's API client: every request carries the session cookie, and every
 * state-changing request the CSRF token of the session in the X-CSRF-Token header.
 */
export function createCookieSessionFetch(options: CookieSessionFetchOptions): typeof fetch {
  return async (input, init) => {
    const request = new Request(input, init);
    const headers = new Headers(request.headers);
    const csrfToken = options.getCsrfToken();
    if (csrfToken && !SAFE_METHODS.has(request.method.toUpperCase())) headers.set("X-CSRF-Token", csrfToken);
    const response = await options.fetch(new Request(request, { credentials: "include", headers }));
    if (response.status === 401) options.onUnauthorized();
    return response;
  };
}
