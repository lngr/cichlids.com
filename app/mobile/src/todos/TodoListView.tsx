import { useState } from "react";
import { Pressable, ScrollView, Text, TextInput, View } from "react-native";
import { MaterialCommunityIcons } from "@expo/vector-icons";
import { useTranslation } from "react-i18next";
import { TODO_MAX_LENGTH, normalizeTodoText, type TodoListItem } from "@cichlids/client-core";
import { Button } from "../components/Button";
import { Chip } from "../components/Chip";
import { translateApiError } from "../lib/apiErrors";
import { minTouchTarget, useTheme } from "../theme";
import { useTodoLists } from "./useTodoLists";

/**
 * The open to-dos of the member across all tanks with a check-off, the done ones below with a way
 * to reopen them, and a field that adds a to-do to a chosen tank.
 */
export function TodoListView() {
  const { t } = useTranslation();
  const theme = useTheme();
  const { state, send, open, done, tanks, tankId, chooseTank } = useTodoLists();
  const [draft, setDraft] = useState("");
  const working = state.matches("working");
  const text = normalizeTodoText(draft);
  const error = state.context.error ? translateApiError(state.context.error, t, "common.loadError") : null;
  const row = (item: TodoListItem) => {
    const checked = item.done;
    return <View key={String(item.id)} testID={`todo-item-${item.id}`} style={{ flexDirection: "row", alignItems: "center", gap: theme.space[2], borderBottomWidth: 1, borderColor: theme.colors.border }}>
      <Pressable testID={`todo-toggle-${item.id}`} accessibilityRole="checkbox" accessibilityState={{ checked, disabled: working }} disabled={working}
        accessibilityLabel={t(checked ? "todos.reopen" : "todos.check", { text: item.text })} onPress={() => send({ type: "SET_DONE", id: Number(item.id), done: !checked })}
        style={{ flex: 1, minHeight: minTouchTarget, flexDirection: "row", alignItems: "center", gap: theme.space[3], paddingVertical: theme.space[2] }}>
        <MaterialCommunityIcons name={checked ? "checkbox-marked-outline" : "checkbox-blank-outline"} size={24} color={theme.colors.accent} />
        <View style={{ flex: 1 }}>
          <Text style={[theme.type.body, { color: checked ? theme.colors.muted : theme.colors.fg, textDecorationLine: checked ? "line-through" : "none" }]}>{item.text}</Text>
          <Text style={[theme.type.meta, { color: theme.colors.muted }]}>{item.tankTitle || t("common.untitled")}</Text>
        </View>
      </Pressable>
      <Pressable testID={`todo-delete-${item.id}`} accessibilityRole="button" accessibilityLabel={t("todos.remove", { text: item.text })} disabled={working}
        onPress={() => send({ type: "REMOVE", id: Number(item.id) })} style={{ width: minTouchTarget, height: minTouchTarget, alignItems: "center", justifyContent: "center" }}>
        <MaterialCommunityIcons name="trash-can-outline" size={22} color={theme.colors.muted} />
      </Pressable>
    </View>;
  };
  return <ScrollView style={{ flex: 1, backgroundColor: theme.colors.bg }} contentContainerStyle={{ padding: theme.space[4], paddingBottom: theme.space[8], gap: theme.space[4] }} keyboardShouldPersistTaps="handled">
    <View testID="todos-new" style={{ gap: theme.space[2] }}>
      <Text accessibilityRole="header" style={[theme.type.h2, { color: theme.colors.fg }]}>{t("todos.newLabel")}</Text>
      {tanks.length === 0 ? <Text style={[theme.type.body, { color: theme.colors.muted }]}>{t("todos.noTanks")}</Text> : <>
        <Text style={[theme.type.meta, { color: theme.colors.muted }]}>{t("todos.tank")}</Text>
        <View style={{ flexDirection: "row", flexWrap: "wrap", gap: theme.space[2] }}>
          {tanks.map(tank => <Chip key={tank.id} testID={`todo-tank-${tank.id}`} label={tank.title || t("common.untitled")} active={tank.id === tankId} onPress={() => chooseTank(tank.id)} />)}
        </View>
        <TextInput testID="todo-new-input" accessibilityLabel={t("todos.newLabel")} value={draft} onChangeText={setDraft} editable={!working} maxLength={TODO_MAX_LENGTH * 2}
          placeholder={t("care.todo.hint")} placeholderTextColor={theme.colors.muted}
          style={[theme.type.body, { color: theme.colors.fg, backgroundColor: theme.colors.surface, minHeight: minTouchTarget, borderWidth: 1, borderColor: theme.colors.border, borderRadius: theme.radius.md, padding: theme.space[3] }]} />
        <Button testID="todo-new-add" label={t("todos.add")} busy={working} disabled={!text || tankId === null || working}
          onPress={() => { if (text && tankId !== null) { send({ type: "ADD", tankId, text }); setDraft(""); } }} />
      </>}
    </View>
    {error ? <Text accessibilityRole="alert" style={[theme.type.body, { color: theme.colors.danger }]}>{error}</Text> : null}
    <View testID="todos-open" style={{ gap: theme.space[1] }}>
      <Text accessibilityRole="header" style={[theme.type.h2, { color: theme.colors.fg }]}>{t("todos.open")}</Text>
      {open.loading && open.items.length === 0 ? <Text style={[theme.type.body, { color: theme.colors.fg }]}>{t("common.loading")}</Text> : null}
      {open.error ? <Button variant="secondary" label={t("common.retry")} onPress={open.reload} /> : null}
      {open.items.map(row)}
      {!open.loading && !open.error && open.items.length === 0 ? <Text testID="todos-empty" style={[theme.type.body, { color: theme.colors.muted }]}>{t("todos.empty")}</Text> : null}
      {open.hasMore ? <Button variant="secondary" label={t("care.more")} busy={open.loadingMore} onPress={open.loadMore} /> : null}
    </View>
    <View testID="todos-done" style={{ gap: theme.space[1] }}>
      <Text accessibilityRole="header" style={[theme.type.h2, { color: theme.colors.fg }]}>{t("todos.doneHeading")}</Text>
      {done.error ? <Button variant="secondary" label={t("common.retry")} onPress={done.reload} /> : null}
      {done.items.map(row)}
      {!done.loading && !done.error && done.items.length === 0 ? <Text style={[theme.type.body, { color: theme.colors.muted }]}>{t("todos.doneEmpty")}</Text> : null}
      {done.hasMore ? <Button variant="secondary" label={t("care.more")} busy={done.loadingMore} onPress={done.loadMore} /> : null}
    </View>
  </ScrollView>;
}
