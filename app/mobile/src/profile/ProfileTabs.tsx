import { Pressable, Text, View } from "react-native";
import { useTranslation } from "react-i18next";
import { minTouchTarget, useTheme } from "../theme";

export type ProfileSection = "pictures" | "tanks" | "drafts";
export function ProfileTabs({ section, onSelect, own = false, drafts = own }: { section: ProfileSection; onSelect: (section: ProfileSection) => void; own?: boolean; drafts?: boolean }) {
  const theme = useTheme();
  const { t } = useTranslation();
  const sections: ProfileSection[] = drafts ? ["pictures", "tanks", "drafts"] : ["pictures", "tanks"];
  return (
    <View accessibilityRole="tablist" style={{ flexDirection: "row", gap: theme.space[2], padding: theme.space[4] }}>
      {sections.map((candidate) => {
        const active = section === candidate;
        return (
          <Pressable key={candidate} testID={own ? `me-tab-${candidate}` : undefined} accessibilityRole="tab" accessibilityState={{ selected: active }} aria-selected={active}
            onPress={() => onSelect(candidate)} style={{ flex: 1, minHeight: minTouchTarget, alignItems: "center", justifyContent: "center", paddingHorizontal: theme.space[2], borderRadius: theme.radius.pill,
              backgroundColor: active ? theme.colors.accent : theme.colors.surface2 }}>
            <Text style={[theme.type.bodyStrong, { color: active ? theme.colors.onAccent : theme.colors.fg }]}>
              {t(candidate === "drafts" ? "me.drafts.title" : `profile.${candidate}Tab`)}
            </Text>
          </Pressable>
        );
      })}
    </View>
  );
}
