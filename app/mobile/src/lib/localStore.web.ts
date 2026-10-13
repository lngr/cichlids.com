/**
 * Text values kept in this browser across reloads and sign-outs, in localStorage. A browser that
 * refuses storage keeps nothing.
 */
export const localStore = {
  async getItem(key: string): Promise<string | null> {
    try {
      return globalThis.localStorage?.getItem(key) ?? null;
    } catch {
      return null;
    }
  },
  async setItem(key: string, value: string): Promise<void> {
    try {
      globalThis.localStorage?.setItem(key, value);
    } catch {
      // Storage full or disabled: the value is not kept.
    }
  },
  async removeItem(key: string): Promise<void> {
    try {
      globalThis.localStorage?.removeItem(key);
    } catch {
      // Storage disabled: nothing was kept.
    }
  },
};
