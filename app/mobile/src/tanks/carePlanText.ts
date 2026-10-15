import { dueSummary, type CareDue, type CarePlanRule } from "@cichlids/client-core";

type Translate = (key: string, options?: Record<string, unknown>) => string;

/** The rhythm of a rule with its reason, for example "Every 7 days: The usual recommendation for cichlid tanks". Null for a rule without a rhythm. */
export function ruleText(rule: CarePlanRule, t: Translate): string | null {
  if (rule.rhythmDays === null || rule.reason === null) return null;
  const reason = t(`care.plan.reason.${rule.reason.code}`, { ...rule.reason.parameters, defaultValue: t("care.plan.reasonOther") });
  return t("care.plan.rhythmReason", { rhythm: rule.rhythmDays === 1 ? t("care.plan.daily") : t("care.plan.every", { count: rule.rhythmDays }), reason });
}

/** The due state in calm wording. */
export function dueText(due: CareDue, t: Translate): string {
  const { key, count } = dueSummary(due);
  return t(`care.plan.${key}`, { count });
}

/** A neutral note under an overdue state; null for every other state. */
export function dueNote(due: CareDue, t: Translate): string | null {
  return due.state === "overdue" ? t("care.plan.overdueNote") : null;
}
