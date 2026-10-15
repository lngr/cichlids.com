import { useEffect, useRef } from "react";
import { Modal, Pressable, Text, View, Platform } from "react-native";
import { useTranslation } from "react-i18next";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import { Button } from "../components/Button";
import { useTheme } from "../theme";
import { useAuth } from "../auth/AuthProvider";
import { useAppNavigation } from "./NavigationProvider";

export function CreateSheet() {
  const { snapshot, send } = useAppNavigation();
  const visible = snapshot.matches("creating");
  const { status } = useAuth();
  const theme = useTheme();
  const insets = useSafeAreaInsets();
  const { t } = useTranslation();
  const focusBefore = useRef<HTMLElement | null>(null);
  useEffect(() => {
    if (Platform.OS !== "web" || !visible) return;
    focusBefore.current = document.activeElement as HTMLElement;
    const escape = (event: KeyboardEvent) => { if (event.key === "Escape") send({ type: "BACK" }); };
    document.addEventListener("keydown", escape);
    return () => { document.removeEventListener("keydown", escape); focusBefore.current?.focus(); };
  }, [visible, send]);
  return <Modal visible={visible} transparent animationType="slide" onRequestClose={() => send({ type: "BACK" })}>
    <View style={{ flex: 1, justifyContent: "flex-end" }}>
      <Pressable accessibilityLabel={t("navigation.cancel")} onPress={() => send({ type: "BACK" })} style={{ flex: 1, backgroundColor: theme.colors.bg, opacity: 0.8 }} />
      <View testID="create-sheet" accessibilityViewIsModal style={{ backgroundColor: theme.colors.surface, borderTopStartRadius: theme.radius.sheet, borderTopEndRadius: theme.radius.sheet, padding: theme.space[6], paddingBottom: theme.space[6] + insets.bottom, gap: theme.space[4] }}>
        <Text accessibilityRole="header" style={[theme.type.h2, { color: theme.colors.fg }]}>{t("navigation.createTitle")}</Text>
        <Button testID="create-photo" label={t("navigation.photo")} onPress={() => send({ type: "PHOTO", authenticated: status === "authenticated" })} />
        <Button testID="create-cancel" variant="secondary" label={t("navigation.cancel")} onPress={() => send({ type: "BACK" })} />
      </View>
    </View>
  </Modal>;
}
