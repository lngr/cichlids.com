import { useCallback, useRef, useState } from "react";
import { ScrollView, Text, View } from "react-native";
import { useFocusEffect } from "expo-router";
import { useTranslation } from "react-i18next";
import type { FollowedTank } from "@cichlids/client-core";
import { apiClient } from "../api/client";
import { Button } from "../components/Button";
import { ErrorView, LoadingView } from "../components/StatusView";
import { translateApiError } from "../lib/apiErrors";
import { useTheme } from "../theme";
import { FollowedTankCard } from "./FollowedTankCard";

const PAGE = 20;

/** All followed tanks the member sees, those with the newest update first, a page at a time. */
export function FollowedTanksView() {
  const { t } = useTranslation();
  const theme = useTheme();
  const [items, setItems] = useState<FollowedTank[] | null>(null);
  const [total, setTotal] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const [loadingMore, setLoadingMore] = useState(false);
  const [attempt, setAttempt] = useState(0);
  const current = useRef(0);
  useFocusEffect(useCallback(() => {
    const ticket = ++current.current;
    setError(null);
    void apiClient.me.followedTanks({ limit: PAGE, offset: 0 })
      .then(page => { if (ticket === current.current) { setItems(page.items); setTotal(page.total); } })
      .catch(err => { if (ticket === current.current) setError(translateApiError(err, t, "common.loadError")); });
    return () => { current.current++; };
  }, [attempt, t]));
  const loadMore = () => {
    if (!items) return;
    const ticket = ++current.current;
    setLoadingMore(true);
    void apiClient.me.followedTanks({ limit: PAGE, offset: items.length })
      .then(page => { if (ticket === current.current) { setItems([...items, ...page.items]); setTotal(page.total); } })
      .catch(err => { if (ticket === current.current) setError(translateApiError(err, t, "common.loadError")); })
      .finally(() => { if (ticket === current.current) setLoadingMore(false); });
  };
  if (error) return <ErrorView message={error} onRetry={() => setAttempt(value => value + 1)} />;
  if (!items) return <LoadingView />;
  return <ScrollView style={{ flex: 1, backgroundColor: theme.colors.bg }} contentContainerStyle={{ padding: theme.space[4], paddingBottom: theme.space[8] }}>
    <View testID="followed-list" style={{ gap: theme.space[3] }}>
      {items.length === 0 ? <Text testID="followed-empty" style={[theme.type.body, { color: theme.colors.muted }]}>{t("today.followed.emptyList")}</Text> : null}
      {items.map(tank => <FollowedTankCard key={tank.tankId} tank={tank} testID={`followed-tank-${tank.tankId}`} />)}
      {items.length < total ? <Button testID="followed-more" variant="secondary" label={t("care.more")} busy={loadingMore} onPress={loadMore} /> : null}
    </View>
  </ScrollView>;
}
