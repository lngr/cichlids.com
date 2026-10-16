import { DeviceAuthError, type DeviceAuth, type DeviceTokenSource } from "@cichlids/client-core";

export interface DeviceTokenGateOptions {
  /** The device authentication of the installation; null when the app keeps no device account. */
  getAuth(): DeviceAuth | null;
  /** Whether the device account acts, which holds while no session of an account exists. */
  isActive(): boolean;
  /** Called when the API refuses the credential of the installation, which a retry does not heal. */
  onRefused(): void;
}

/**
 * The device token source of the API client. It hands out a token only while the device account
 * acts. A token that cannot be had lets the request go on without one, so reads of public content
 * keep working; the proof for a registration is available whenever a device authentication exists.
 */
export function createDeviceTokenGate(options: DeviceTokenGateOptions): Required<DeviceTokenSource> {
  return {
    async getAccessToken() {
      const auth = options.getAuth();
      if (!auth || !options.isActive()) return undefined;
      try {
        return await auth.getAccessToken();
      } catch (error) {
        if (error instanceof DeviceAuthError && error.status === 401) options.onRefused();
        return undefined;
      }
    },
    signProof() {
      const auth = options.getAuth();
      if (!auth) return Promise.reject(new Error("This installation has no device account."));
      return auth.signProof();
    },
    invalidate() {
      options.getAuth()?.invalidate();
    },
  };
}
