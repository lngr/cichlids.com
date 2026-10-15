import { Pressable, Text, View } from "react-native";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import { Tabs } from "expo-router";
import { MaterialCommunityIcons } from "@expo/vector-icons";
import { useTranslation } from "react-i18next";
import { selectedTab } from "@cichlids/client-core";
import { minTouchTarget, useTheme } from "../../src/theme";
import { useAppNavigation } from "../../src/navigation/NavigationProvider";

const tabs = [
  { name: "today", icon: "calendar-today" },
  { name: "discover", icon: "compass-outline" },
  { name: "create", icon: "plus" },
  { name: "fish", icon: "fish" },
  { name: "me", icon: "account-circle" },
] as const;
function TabBar() {
  const theme = useTheme();
  const insets = useSafeAreaInsets();
  const { t } = useTranslation();
  const { snapshot, send } = useAppNavigation();
  const activeTab = selectedTab(snapshot.context.path);
  return <View style={{ flexDirection: "row", paddingBottom: insets.bottom, minHeight: 64 + insets.bottom, backgroundColor: theme.colors.surface, borderTopWidth: 1, borderTopColor: theme.colors.border }}>
    {tabs.map(({ name, icon }) => {
      const active = name === activeTab;
      const color = name === "create" ? theme.colors.onAccent : active ? theme.colors.accent : theme.colors.muted;
      return <Pressable key={name} testID={`tab-${name}`} accessibilityRole={name === "create" ? "button" : "tab"} accessibilityLabel={name === "create" ? t("navigation.createTitle") : t(`tabs.${name}`)} accessibilityState={name === "create" ? {} : { selected: active }} aria-selected={name === "create" ? undefined : active}
        onPress={() => send(name === "create" ? { type: "CREATE" } : { type: "TAB", tab: name })}
        style={{ flex: 1, minHeight: minTouchTarget, minWidth: minTouchTarget, alignItems: "center", justifyContent: "center", gap: theme.space[1], paddingHorizontal: theme.space[1] }}>
        <View style={name === "create" ? { backgroundColor: theme.colors.accent, borderRadius: theme.radius.md, paddingHorizontal: theme.space[3], paddingVertical: theme.space[1] } : undefined}>
          <MaterialCommunityIcons name={icon} color={color} size={24} />
        </View>
        <Text numberOfLines={2} style={[theme.type.meta, { color: active ? theme.colors.accent : theme.colors.muted, textAlign: "center" }]}>{t(`tabs.${name}`)}</Text>
      </Pressable>;
    })}
  </View>;
}
export default function TabsLayout() {
  const theme = useTheme();
  const { t } = useTranslation();
  return <Tabs tabBar={() => <TabBar />} screenOptions={{ headerShown: false, headerStyle: { backgroundColor: theme.colors.surface }, headerTintColor: theme.colors.fg }}>
    <Tabs.Screen name="today" options={{ headerShown: true, title: t("tabs.today") }} />
    <Tabs.Screen name="discover" options={{ headerShown: true, title: t("tabs.discover") }} />
    <Tabs.Screen name="fish" options={{ headerShown: true, title: t("tabs.fish") }} />
    <Tabs.Screen name="me" />
    <Tabs.Screen name="gallery" options={{ href: null }} />
    <Tabs.Screen name="tanks" options={{ href: null }} />
    <Tabs.Screen name="community" options={{ href: null }} />
    <Tabs.Screen name="species" options={{ href: null }} />
  </Tabs>;
}
