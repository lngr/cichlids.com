import { Text } from "react-native";
import { useTranslation } from "react-i18next";
import { toVacationShare, type ProfileDetail } from "@cichlids/client-core";
import { useTheme } from "../theme";
import { vacationShareText } from "./streakText";

/** The share of time a member spent on ended vacations; absent when the viewer does not see it or when it is zero. */
export function VacationShareLine({ share }: { share: ProfileDetail["vacationShare"] }) {
  const { t, i18n } = useTranslation();
  const theme = useTheme();
  const text = share ? vacationShareText(toVacationShare(share), t, i18n.language) : null;
  if (!text) return null;
  return <Text testID="profile-vacation-share" style={[theme.type.meta, { color: theme.colors.muted, paddingHorizontal: theme.space[4] }]}>{text}</Text>;
}
