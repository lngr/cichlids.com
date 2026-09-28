import { defineConfig } from "vitest/config";

// Unit tests cover the pure TypeScript modules only; React Native and Expo native modules are
// replaced with vi.mock stubs inside each test file.
export default defineConfig({
  test: {
    include: ["src/**/*.test.ts"],
    environment: "node",
  },
});
