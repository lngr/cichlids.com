export interface RefreshSignal {
  /** Asks every subscriber to recompute the scheduled reminders. */
  request(): void;
  subscribe(listener: () => void): () => void;
}

export function createRefreshSignal(): RefreshSignal {
  const listeners = new Set<() => void>();
  return {
    request: () => { listeners.forEach(listener => listener()); },
    subscribe: listener => {
      listeners.add(listener);
      return () => { listeners.delete(listener); };
    },
  };
}

/**
 * The app-wide signal: screens request it after a change that moves due dates or the reminder
 * switch, and the reminder host reschedules the notifications.
 */
export const reminderRefresh = createRefreshSignal();
