import { ActivityIndicator, Pressable, StyleSheet, Text, View } from "react-native";
import { useTranslation } from "react-i18next";
import { minTouchTarget, useTheme } from "../theme";

export function LoadingView({ label }: { label?: string }) {
  const theme = useTheme();
  const { t } = useTranslation();
  return (
    <View style={[styles.center, { backgroundColor: theme.colors.bg }]}>
      <ActivityIndicator color={theme.colors.accent} />
      <Text style={[styles.label, { color: theme.colors.muted }]}>{label ?? t("common.loading")}</Text>
    </View>
  );
}

export function ErrorView({ message, onRetry }: { message: string; onRetry?: () => void }) {
  const theme = useTheme();
  const { t } = useTranslation();
  return (
    <View style={[styles.center, { backgroundColor: theme.colors.bg }]}>
      <Text style={[styles.label, { color: theme.colors.danger }]}>{message}</Text>
      {onRetry ? (
        <Pressable testID="error-retry" accessibilityRole="button" onPress={onRetry} style={styles.retryTarget}>
          <Text style={[styles.retry, { color: theme.colors.accent }]}>{t("common.retry")}</Text>
        </Pressable>
      ) : null}
    </View>
  );
}

export function EmptyView({ message }: { message: string }) {
  const theme = useTheme();
  return (
    <View style={[styles.center, { backgroundColor: theme.colors.bg }]}>
      <Text style={[styles.label, { color: theme.colors.muted }]}>{message}</Text>
    </View>
  );
}

const styles = StyleSheet.create({
  center: {
    flex: 1,
    alignItems: "center",
    justifyContent: "center",
    padding: 24,
    gap: 8,
  },
  label: {
    fontSize: 15,
    textAlign: "center",
  },
  retry: {
    fontSize: 15,
    fontWeight: "600",
  },
  retryTarget: {
    minHeight: minTouchTarget,
    minWidth: minTouchTarget,
    alignItems: "center",
    justifyContent: "center",
  },
});
