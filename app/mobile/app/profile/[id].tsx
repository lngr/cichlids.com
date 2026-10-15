import { translateApiError } from "../../src/lib/apiErrors";
import { useEffect, useState } from "react";
import { Text, View } from "react-native";
import { useLocalSearchParams, useNavigation } from "expo-router";
import { useTranslation } from "react-i18next";
import { apiClient } from "../../src/api/client";
import { useTheme } from "../../src/theme";
import { ErrorView, LoadingView } from "../../src/components/StatusView";
import { ProfileHeader } from "../../src/profile/ProfileHeader";
import { ProfileTabs, type ProfileSection } from "../../src/profile/ProfileTabs";
import { ProfileContentList } from "../../src/profile/ProfileContentList";
import { useProfileLists } from "../../src/profile/useProfileLists";
import type { ProfileDetail } from "@cichlids/client-core";

export default function ProfileScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const profileId = Number(id);
  const theme = useTheme();
  const navigation = useNavigation();
  const { t } = useTranslation();

  const [profile, setProfile] = useState<ProfileDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [tab, setTab] = useState<ProfileSection>("pictures");

  useEffect(() => {
    let cancelled = false;
    apiClient.profiles
      .get(profileId)
      .then((detail) => {
        if (cancelled) return;
        setProfile(detail);
        navigation.setOptions({ title: detail.displayName ?? detail.username });
      })
      .catch((err) => !cancelled && setError(translateApiError(err, t, "common.loadError")))
      .finally(() => !cancelled && setLoading(false));
    return () => {
      cancelled = true;
    };
  }, [profileId, navigation, t]);

  const { pictures, tanks } = useProfileLists(profileId);

  if (loading) return <LoadingView label={t("profile.loading")} />;
  if (error || !profile) return <ErrorView message={error ?? t("profile.notFound")} />;

  const header = (
    <View>
      <ProfileHeader profile={profile} />
      <View style={{ flexDirection: "row", flexWrap: "wrap", gap: theme.space[4], paddingHorizontal: theme.space[4] }}>
        <Text style={[theme.type.meta, { color: theme.colors.muted }]}>{t("profile.pictures", { count: Number(profile.stats.pictureCount) })}</Text>
        <Text style={[theme.type.meta, { color: theme.colors.muted }]}>{t("profile.tanks", { count: Number(profile.stats.tankCount) })}</Text>
        <Text style={[theme.type.meta, { color: theme.colors.muted }]}>{t("profile.comments", { count: Number(profile.stats.commentCount) })}</Text>
      </View>
      <ProfileTabs section={tab} onSelect={setTab} />
    </View>
  );

  return <ProfileContentList section={tab === "tanks" ? "tanks" : "pictures"} pictures={pictures} tanks={tanks} header={header} />;
}
