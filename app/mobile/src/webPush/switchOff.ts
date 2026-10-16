import { apiClient } from "../api/client";
import { forgetOwner } from "./ownership";
import { clearWorkerSettings, currentSubscription } from "./pushBrowser";
import { switchOffWith } from "./switchOffWith";

export function switchOffWebPush(): Promise<void> {
  return switchOffWith({
    current: currentSubscription,
    deleteOnServer: endpoint => apiClient.webPush.unsubscribe(endpoint),
    clearSettings: clearWorkerSettings,
    forgetOwner,
  });
}
