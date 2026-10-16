// Tests for the proxy option of the Chromium launches.
import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";

import { browserProxyOptions } from "./browser-proxy.mjs";

test("browserProxyOptions: the proxy server comes from HTTPS_PROXY or https_proxy, without credentials handling", () => {
  assert.deepEqual(browserProxyOptions({ HTTPS_PROXY: "http://127.0.0.1:18081" }), { proxy: { server: "http://127.0.0.1:18081" } });
  assert.deepEqual(browserProxyOptions({ https_proxy: "http://127.0.0.1:2" }), { proxy: { server: "http://127.0.0.1:2" } });
  assert.deepEqual(browserProxyOptions({ HTTPS_PROXY: "http://a:1", https_proxy: "http://b:2" }), { proxy: { server: "http://a:1" } });
  assert.deepEqual(browserProxyOptions({ HTTP_PROXY: "http://a:1" }), {});
  assert.deepEqual(browserProxyOptions({ HTTPS_PROXY: "" }), {});
  assert.deepEqual(browserProxyOptions({}), {});
});

test("fetch-oa.mjs and crc.mjs pass the proxy option to chromium.launch", () => {
  for (const file of ["./fetch-oa.mjs", "./crc.mjs"]) {
    const source = readFileSync(new URL(file, import.meta.url), "utf8");
    assert.match(source, /import \{ browserProxyOptions \} from '\.\/browser-proxy\.mjs'/, file);
    assert.match(source, /chromium\.launch\(\{[^}]*\.\.\.browserProxyOptions\(\)/s, file);
  }
});
