import { createPublishSignal } from "./publishSignal";

/**
 * The app-wide signal that a photo joined the time-lapse series of a tank: the camera screen
 * notifies it after a successful upload, and the tank page compares against it on focus to load
 * the series again only when there is something new to show.
 */
export const tankSeriesSignal = createPublishSignal();
