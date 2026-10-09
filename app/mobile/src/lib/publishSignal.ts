export interface PublishSignal {
  /** Records that a picture was published. */
  notifyPublished(): void;
  /** The current version; it grows with every publish. */
  version(): number;
  /** True when a picture was published after the given version was taken. */
  changedSince(seenVersion: number): boolean;
}

export function createPublishSignal(): PublishSignal {
  let current = 0;
  return {
    notifyPublished: () => {
      current += 1;
    },
    version: () => current,
    changedSince: (seenVersion) => current !== seenVersion,
  };
}

/**
 * The app-wide signal: the upload screen notifies it after a successful publish, and picture
 * lists compare against it on focus to refetch only when there is something new to show.
 */
export const publishSignal = createPublishSignal();
