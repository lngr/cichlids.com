import * as SecureStore from "expo-secure-store";

// The secure store takes values of at most 2048 bytes; a chunk of this many characters (code
// points) stays below that also when every character needs four bytes in UTF-8.
const CHUNK_LENGTH = 500;

function countKey(key: string): string {
  return `${key}.count`;
}

function chunkKey(key: string, index: number): string {
  return `${key}.${index}`;
}

async function storedCount(key: string): Promise<number> {
  const count = Number(await SecureStore.getItemAsync(countKey(key)));
  return Number.isInteger(count) && count > 0 ? count : 0;
}

/**
 * Text values kept on the device across app starts and sign-outs, in the secure store of the
 * native app. A value of any length is stored in chunks under the key.
 */
export const localStore = {
  async getItem(key: string): Promise<string | null> {
    const count = await storedCount(key);
    if (count === 0) return null;
    const chunks: string[] = [];
    for (let index = 0; index < count; index += 1) {
      const chunk = await SecureStore.getItemAsync(chunkKey(key, index));
      if (chunk === null) return null;
      chunks.push(chunk);
    }
    return chunks.join("");
  },
  async setItem(key: string, value: string): Promise<void> {
    const previous = await storedCount(key);
    // Splitting by code points keeps surrogate pairs together.
    const characters = Array.from(value);
    const count = Math.max(1, Math.ceil(characters.length / CHUNK_LENGTH));
    for (let index = 0; index < count; index += 1) {
      const chunk = characters.slice(index * CHUNK_LENGTH, (index + 1) * CHUNK_LENGTH).join("");
      await SecureStore.setItemAsync(chunkKey(key, index), chunk);
    }
    await SecureStore.setItemAsync(countKey(key), String(count));
    for (let index = count; index < previous; index += 1) {
      await SecureStore.deleteItemAsync(chunkKey(key, index));
    }
  },
  async removeItem(key: string): Promise<void> {
    const count = await storedCount(key);
    await SecureStore.deleteItemAsync(countKey(key));
    for (let index = 0; index < count; index += 1) {
      await SecureStore.deleteItemAsync(chunkKey(key, index));
    }
  },
};
