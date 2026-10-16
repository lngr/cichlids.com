import { useCallback, useRef, useState } from "react";
import { useFocusEffect } from "expo-router";
import { apiClient } from "../api/client";

/**
 * The number of open to-dos of the member, read whenever the screen gains focus. A count that cannot
 * be read is zero, so Today stays usable.
 */
export function useOpenTodoCount(): number {
  const [count, setCount] = useState(0);
  const current = useRef(0);
  useFocusEffect(useCallback(() => {
    const ticket = ++current.current;
    void apiClient.todos.list({ state: "open", limit: 1 })
      .then(page => { if (ticket === current.current) setCount(page.total); })
      .catch(() => { if (ticket === current.current) setCount(0); });
    return () => { current.current++; };
  }, []));
  return count;
}
