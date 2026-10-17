import { useState, type ReactNode } from "react";
import { Modal, Pressable, ScrollView, Text, TextInput, View } from "react-native";
import { useTranslation } from "react-i18next";
import {
  checkGrowthDraft, checkRemovalDay, checkSpawningDraft, growthInput, moveTargets, spawningInput,
  type FishPage, type GrowthDraft, type SpawningDraft,
} from "@cichlids/client-core";
import { apiClient } from "../api/client";
import { Button } from "../components/Button";
import { Chip } from "../components/Chip";
import { translateApiError } from "../lib/apiErrors";
import { removalProblemKey } from "../stocking/stockingText";
import { localCalendarDate, localTime } from "../today/localDate";
import { minTouchTarget, useTheme } from "../theme";
import { useUnits } from "../units/useUnits";
import { growthProblemKey, lengthUnitOf } from "./fishText";
import { useOwnTanks } from "./useOwnTanks";

/** A full-height sheet that holds one form; its content is only built while the sheet is visible. */
export function SheetFrame({ visible, onClose, children }: { visible: boolean; onClose: () => void; children: ReactNode }) {
  const theme = useTheme();
  return <Modal visible={visible} transparent animationType="slide" onRequestClose={onClose}>
    <View style={{ flex: 1, justifyContent: "flex-end", backgroundColor: theme.colors.bg }}>
      <ScrollView contentContainerStyle={{ padding: theme.space[6], gap: theme.space[3] }} keyboardShouldPersistTaps="handled">
        {visible ? children : null}
      </ScrollView>
    </View>
  </Modal>;
}

type SheetProps = { page: FishPage; visible: boolean; onSaved: (page: FishPage) => void; onCancel: () => void };

function useSubmit(onSaved: (page: FishPage) => void, fallbackKey: string) {
  const { t } = useTranslation();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const run = async (action: () => Promise<FishPage>) => {
    setBusy(true);
    setError(null);
    try {
      onSaved(await action());
    } catch (err) {
      setError(translateApiError(err, t, fallbackKey));
    } finally {
      setBusy(false);
    }
  };
  return { busy, error, run };
}

function Field({ label, testID, value, onChange, disabled, problem, placeholder, inputMode }: {
  label: string; testID: string; value: string; onChange: (value: string) => void; disabled: boolean; problem: string | null; placeholder?: string;
  inputMode?: "numeric" | "decimal" | "text";
}) {
  const theme = useTheme();
  return <>
    <Text style={[theme.type.meta, { color: theme.colors.muted }]}>{label}</Text>
    <TextInput testID={testID} accessibilityLabel={label} placeholder={placeholder} placeholderTextColor={theme.colors.muted} value={value} editable={!disabled}
      inputMode={inputMode ?? "text"} autoCapitalize="none" autoCorrect={false} onChangeText={onChange}
      style={[theme.type.body, { color: theme.colors.fg, backgroundColor: theme.colors.surface, minHeight: minTouchTarget, borderWidth: 1,
        borderColor: problem ? theme.colors.danger : theme.colors.border, borderRadius: theme.radius.md, padding: theme.space[3] }]} />
    {problem ? <Text testID={`${testID}-problem`} accessibilityRole="alert" style={[theme.type.body, { color: theme.colors.danger }]}>{problem}</Text> : null}
  </>;
}

function DayField({ page, testID, value, onChange, disabled }: { page: FishPage; testID: string; value: string; onChange: (value: string) => void; disabled: boolean }) {
  const { t } = useTranslation();
  const key = removalProblemKey(checkRemovalDay(value, page.movedInOn, localCalendarDate()));
  return <Field label={t("fish.day")} testID={testID} value={value} onChange={onChange} disabled={disabled} problem={key ? t(key) : null}
    placeholder={t("stocking.dayPlaceholder")} inputMode="numeric" />;
}

function dayProblem(page: FishPage, day: string): boolean {
  return checkRemovalDay(day, page.movedInOn, localCalendarDate()) !== "ok";
}

function SheetActions({ testID, label, busy, disabled, error, onSubmit, onCancel }: {
  testID: string; label: string; busy: boolean; disabled: boolean; error: string | null; onSubmit: () => void; onCancel: () => void;
}) {
  const { t } = useTranslation();
  const theme = useTheme();
  return <>
    {error ? <Text testID={`${testID}-error`} accessibilityRole="alert" style={[theme.type.body, { color: theme.colors.danger }]}>{error}</Text> : null}
    <Button testID={`${testID}-save`} label={label} busy={busy} disabled={disabled} onPress={onSubmit} />
    <Button testID={`${testID}-cancel`} variant="secondary" label={t("navigation.cancel")} disabled={busy} onPress={onCancel} />
  </>;
}

/** The sheet that records a measured length in cm or inch, in the unit system of the member by default. */
export function GrowthSheet({ page, visible, onSaved, onCancel }: SheetProps) {
  return <SheetFrame visible={visible} onClose={onCancel}><GrowthForm page={page} onSaved={onSaved} onCancel={onCancel} /></SheetFrame>;
}

function GrowthForm({ page, onSaved, onCancel }: Omit<SheetProps, "visible">) {
  const { t } = useTranslation();
  const theme = useTheme();
  const system = useUnits();
  const [draft, setDraft] = useState<GrowthDraft>({ length: "", unit: lengthUnitOf(system) });
  const [day, setDay] = useState("");
  const { busy, error, run } = useSubmit(onSaved, "fish.saveError");
  const problem = draft.length.trim() === "" ? null : checkGrowthDraft(draft);
  const ready = checkGrowthDraft(draft) === null && !dayProblem(page, day);
  return <>
    <Text accessibilityRole="header" style={[theme.type.h2, { color: theme.colors.fg }]}>{t("fish.growth.title", { name: page.name })}</Text>
    <Field label={t("fish.growth.length")} testID="fish-growth-length" value={draft.length} onChange={length => setDraft({ ...draft, length })} disabled={busy}
      problem={problem ? t(growthProblemKey(problem)) : null} inputMode="decimal" />
    <View accessibilityRole="radiogroup" style={{ flexDirection: "row", gap: theme.space[2] }}>
      {(["cm", "inch"] as const).map(unit => <Chip key={unit} testID={`fish-growth-unit-${unit}`} label={t(`units.symbols.length.${unit === "cm" ? "metric" : "us"}`)}
        active={draft.unit === unit} onPress={() => setDraft({ ...draft, unit })} />)}
    </View>
    <DayField page={page} testID="fish-growth-day" value={day} onChange={setDay} disabled={busy} />
    <SheetActions testID="fish-growth" label={t("fish.save")} busy={busy} disabled={!ready} error={error} onCancel={onCancel}
      onSubmit={() => void run(() => apiClient.fish.recordGrowth(page.id, growthInput(draft, day), localTime()))} />
  </>;
}

/** The sheet that records a spawning with the generation of the offspring, or a hybrid. */
export function SpawningSheet({ page, visible, onSaved, onCancel }: SheetProps) {
  return <SheetFrame visible={visible} onClose={onCancel}><SpawningForm page={page} onSaved={onSaved} onCancel={onCancel} /></SheetFrame>;
}

function SpawningForm({ page, onSaved, onCancel }: Omit<SheetProps, "visible">) {
  const { t } = useTranslation();
  const theme = useTheme();
  const [draft, setDraft] = useState<SpawningDraft>({ hybrid: false, generation: "1" });
  const [day, setDay] = useState("");
  const { busy, error, run } = useSubmit(onSaved, "fish.saveError");
  const generationProblem = !draft.hybrid && checkSpawningDraft(draft) !== null;
  const ready = checkSpawningDraft(draft) === null && !dayProblem(page, day);
  return <>
    <Text accessibilityRole="header" style={[theme.type.h2, { color: theme.colors.fg }]}>{t("fish.spawning.title", { name: page.name })}</Text>
    <Text style={[theme.type.body, { color: theme.colors.muted }]}>{t("fish.spawning.hint")}</Text>
    <View accessibilityRole="radiogroup" style={{ flexDirection: "row", flexWrap: "wrap", gap: theme.space[2] }}>
      <Chip testID="fish-spawning-purebred" label={t("fish.spawning.purebred")} active={!draft.hybrid} onPress={() => setDraft({ ...draft, hybrid: false })} />
      <Chip testID="fish-spawning-hybrid" label={t("fish.spawning.hybrid")} active={draft.hybrid} onPress={() => setDraft({ ...draft, hybrid: true })} />
    </View>
    {draft.hybrid ? null : <Field label={t("fish.spawning.generation")} testID="fish-spawning-generation" value={draft.generation} disabled={busy}
      onChange={generation => setDraft({ ...draft, generation })} problem={generationProblem ? t("fish.errors.generation") : null} inputMode="numeric" />}
    <DayField page={page} testID="fish-spawning-day" value={day} onChange={setDay} disabled={busy} />
    <SheetActions testID="fish-spawning" label={t("fish.save")} busy={busy} disabled={!ready} error={error} onCancel={onCancel}
      onSubmit={() => void run(() => apiClient.fish.recordSpawning(page.id, spawningInput(draft, day), localTime()))} />
  </>;
}

/** The sheet that moves the fish to another tank of its owner; it says that the photos follow the visibility of the new tank. */
export function MoveSheet({ page, visible, onSaved, onCancel }: SheetProps) {
  return <SheetFrame visible={visible} onClose={onCancel}><MoveForm page={page} onSaved={onSaved} onCancel={onCancel} /></SheetFrame>;
}

function MoveForm({ page, onSaved, onCancel }: Omit<SheetProps, "visible">) {
  const { t } = useTranslation();
  const theme = useTheme();
  const tanks = moveTargets(useOwnTanks(true), page.tank.id);
  const [target, setTarget] = useState<number | null>(null);
  const [day, setDay] = useState("");
  const { busy, error, run } = useSubmit(onSaved, "fish.saveError");
  const ready = target !== null && !dayProblem(page, day);
  return <>
    <Text accessibilityRole="header" style={[theme.type.h2, { color: theme.colors.fg }]}>{t("fish.move.title", { name: page.name })}</Text>
    <Text testID="fish-move-warning" style={[theme.type.body, { color: theme.colors.fg }]}>{t("fish.move.warning")}</Text>
    {tanks.length === 0 ? <Text testID="fish-move-empty" style={[theme.type.body, { color: theme.colors.muted }]}>{t("fish.move.none")}</Text> : null}
    <View accessibilityRole="radiogroup" style={{ gap: theme.space[2] }}>
      {tanks.map(tank => <Pressable key={tank.id} testID={`fish-move-tank-${tank.id}`} accessibilityRole="radio" accessibilityState={{ selected: target === tank.id }}
        onPress={() => setTarget(tank.id)} style={{ minHeight: minTouchTarget, justifyContent: "center", paddingHorizontal: theme.space[3], borderRadius: theme.radius.md, borderWidth: 1,
          borderColor: target === tank.id ? theme.colors.accent : theme.colors.border, backgroundColor: target === tank.id ? theme.colors.accentWeak : theme.colors.surface }}>
        <Text style={[theme.type.bodyStrong, { color: theme.colors.fg }]}>{tank.title || t("common.untitled")}</Text>
      </Pressable>)}
    </View>
    <DayField page={page} testID="fish-move-day" value={day} onChange={setDay} disabled={busy} />
    <SheetActions testID="fish-move" label={t("fish.move.confirm")} busy={busy} disabled={!ready} error={error} onCancel={onCancel}
      onSubmit={() => target !== null && void run(() => apiClient.fish.move(page.id, target, day.trim() === "" ? null : day.trim(), localTime()))} />
  </>;
}

/** The sheet that records the death of the fish; its page stays as a memorial. */
export function DeathSheet({ page, visible, onSaved, onCancel }: SheetProps) {
  return <SheetFrame visible={visible} onClose={onCancel}><DeathForm page={page} onSaved={onSaved} onCancel={onCancel} /></SheetFrame>;
}

function DeathForm({ page, onSaved, onCancel }: Omit<SheetProps, "visible">) {
  const { t } = useTranslation();
  const theme = useTheme();
  const [day, setDay] = useState("");
  const { busy, error, run } = useSubmit(onSaved, "fish.saveError");
  return <>
    <Text accessibilityRole="header" style={[theme.type.h2, { color: theme.colors.fg }]}>{t("fish.death.title", { name: page.name })}</Text>
    <Text style={[theme.type.body, { color: theme.colors.muted }]}>{t("fish.death.hint")}</Text>
    <DayField page={page} testID="fish-death-day" value={day} onChange={setDay} disabled={busy} />
    <SheetActions testID="fish-death" label={t("fish.death.confirm")} busy={busy} disabled={dayProblem(page, day)} error={error} onCancel={onCancel}
      onSubmit={() => void run(() => apiClient.fish.recordDeath(page.id, day.trim() === "" ? null : day.trim(), localTime()))} />
  </>;
}
