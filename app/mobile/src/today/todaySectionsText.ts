import { daysSince, type FollowedTank, type FollowedUpdate, type Milestone } from "@cichlids/client-core";

type Translate = (key: string, options?: Record<string, unknown>) => string;

/** The text of what a followed tank published: the kind of update, and for a care entry the kind of care. */
export function updateText(update: FollowedUpdate, t: Translate): string {
  switch (update.kind) {
    case "story": return t("today.followed.update.story");
    case "time_lapse_photo": return t("today.followed.update.timeLapsePhoto");
    case "tank_photo": return t("today.followed.update.tankPhoto");
    default: return t(`today.followed.update.${update.careKind ?? "measurement"}`);
  }
}

/** How long ago an update appeared: "today" or the whole days. */
export function whenText(update: FollowedUpdate, now: Date, t: Translate): string {
  const days = daysSince(update.at, now);
  return days === 0 ? t("today.followed.agoToday") : t("today.followed.ago", { count: days });
}

/** The line of a followed tank card: the update and when it appeared, or the calm text of a tank without one. */
export function followedLine(tank: FollowedTank, now: Date, t: Translate): string {
  return tank.update ? `${updateText(tank.update, t)}, ${whenText(tank.update, now, t)}` : t("today.followed.noUpdate");
}

/** The spoken label of a followed tank card. */
export function followedLabel(tank: FollowedTank, now: Date, t: Translate): string {
  const title = tank.title || t("common.untitled");
  return tank.update
    ? t("today.followed.cardLabel", { tank: title, update: updateText(tank.update, t), when: whenText(tank.update, now, t) })
    : `${title}, ${t("today.followed.noUpdate")}`;
}

/** The congratulation for a milestone. */
export function milestoneText(milestone: Milestone, t: Translate): string {
  const tank = milestone.tankTitle || t("common.untitled");
  switch (milestone.kind) {
    case "first_care_week": return t("today.milestones.firstCareWeek", { tank });
    case "kept_streak": return t("today.milestones.keptStreak", { tank, count: milestone.value });
    case "fish_anniversary": return t("today.milestones.fishAnniversary", { name: milestone.inhabitantName ?? t("common.untitled"), tank, count: milestone.value });
    default: return t("today.milestones.anniversary", { tank, count: milestone.value });
  }
}
