import type { TankFollow, TankVisibility } from "@cichlids/client-core";

/**
 * What the follow control of a tank shows: nothing, the prompt to sign in, or one of the four states of
 * the button. "follow" follows a public tank at once, "request" asks the owner of a tank for followers,
 * "requested" is an open request and "following" an approved follow.
 */
export type FollowControl = "hidden" | "login" | "follow" | "request" | "requested" | "following";

export function followControlOf(
  follow: TankFollow | null | undefined, profile: TankVisibility, isOwner: boolean, signedIn: boolean,
): FollowControl {
  if (isOwner || !follow) return "hidden";
  if (!signedIn) return profile === "public" ? "login" : "hidden";
  if (follow.state === "requested") return "requested";
  if (follow.state === "following") return "following";
  if (!follow.canFollow) return "hidden";
  return profile === "public" ? "follow" : "request";
}

/** Whether pressing the control withdraws a request or stops following; otherwise it sends the follow or request. */
export function undoesFollow(control: FollowControl): boolean {
  return control === "requested" || control === "following";
}
