import { describe, expect, it, vi } from "vitest";
import { DiscoveryUnavailableError, ensureDiscovery } from "./discovery";

const document = { authorizationEndpoint: "https://auth.example.org/auth", tokenEndpoint: "https://auth.example.org/token" };

describe("ensureDiscovery", () => {
  it("returns the loaded document without a request", async () => {
    const load = vi.fn();
    expect(await ensureDiscovery(document, load)).toBe(document);
    expect(load).not.toHaveBeenCalled();
  });

  it("loads the document when it is missing", async () => {
    const load = vi.fn().mockResolvedValue(document);
    expect(await ensureDiscovery(null, load)).toBe(document);
    expect(load).toHaveBeenCalledTimes(1);
  });

  it("reports an unreachable issuer as an unavailable discovery with the cause", async () => {
    const cause = new TypeError("Failed to fetch");
    const load = vi.fn().mockRejectedValue(cause);
    const error = await ensureDiscovery(null, load).catch((err) => err);
    expect(error).toBeInstanceOf(DiscoveryUnavailableError);
    expect(error.cause).toBe(cause);
  });

  it("tries again on the next call after a failure", async () => {
    const load = vi.fn().mockRejectedValueOnce(new Error("down")).mockResolvedValueOnce(document);
    await expect(ensureDiscovery(null, load)).rejects.toBeInstanceOf(DiscoveryUnavailableError);
    expect(await ensureDiscovery(null, load)).toBe(document);
  });
});
