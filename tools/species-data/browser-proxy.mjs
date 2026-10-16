// Proxy setting of the Chromium launches. In the agent cage the only way to the internet is the
// egress proxy that HTTPS_PROXY names; Chromium ignores that variable, so the launch option carries
// the address. Outside the cage the variable is not set and Chromium connects directly.

/** Returns { proxy: { server } } for the proxy of env (HTTPS_PROXY or https_proxy), else {}. */
export function browserProxyOptions(env = process.env) {
  const server = env.HTTPS_PROXY || env.https_proxy;
  return server ? { proxy: { server } } : {};
}
