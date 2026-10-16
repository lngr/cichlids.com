import { Image, Text, View } from "react-native";
import { useTranslation } from "react-i18next";
import type { ProfileDetail } from "@cichlids/client-core";
import { useTheme } from "../theme";
import { formatDate } from "../lib/format";
import { HEADER_RATIO } from "./profileImages";

export function ProfileHeader({ profile, own = false }: { profile: ProfileDetail; own?: boolean }) {
  const theme = useTheme();
  const { t, i18n } = useTranslation();
  return (
    <View>
      {profile.profileImageUrl ? (
        <Image testID="profile-header-image" source={{ uri: profile.profileImageUrl }} accessibilityIgnoresInvertColors
          style={{ width: "100%", aspectRatio: HEADER_RATIO, backgroundColor: theme.colors.placeholder }} />
      ) : null}
      <View style={{ flexDirection: "row", alignItems: "center", gap: theme.space[3], padding: theme.space[4] }}>
        {profile.avatarUrl ? (
          <Image testID="profile-avatar-image" source={{ uri: profile.avatarUrl }} style={{ width: 64, height: 64, borderRadius: 32, backgroundColor: theme.colors.placeholder }} />
        ) : <View style={{ width: 64, height: 64, borderRadius: 32, backgroundColor: theme.colors.placeholder }} />}
        <View style={{ flex: 1 }}>
          <Text testID={own ? "me-name" : undefined} style={[theme.type.h1, { color: theme.colors.fg }]}>{profile.displayName ?? profile.username}</Text>
          <Text style={[theme.type.meta, { color: theme.colors.muted }]}>
            {own ? `@${profile.username}` : [profile.city, profile.countryCode].filter(Boolean).join(", ") || t("profile.memberSince", { date: formatDate(profile.createdAt, i18n.language) })}
          </Text>
        </View>
      </View>
    </View>
  );
}
