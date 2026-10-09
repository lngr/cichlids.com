import { ActivityIndicator, StyleSheet, Text, View } from "react-native";
import { useTranslation } from "react-i18next";
import { useTheme } from "../theme";

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
        <Text style={[styles.retry, { color: theme.colors.accent }]} onPress={onRetry}>
          {t("common.retry")}
        </Text>
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
    marginTop: 4,
  },
});
