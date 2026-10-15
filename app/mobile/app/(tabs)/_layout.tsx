import { Text } from "react-native";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import { Tabs } from "expo-router";
import { MaterialCommunityIcons } from "@expo/vector-icons";
import { useTranslation } from "react-i18next";
import { useTheme } from "../../src/theme";

export default function TabsLayout() {
  const theme = useTheme();
  const insets = useSafeAreaInsets();
  const { t } = useTranslation();

  return (
    <Tabs
      screenOptions={{
        headerShown: false,
        tabBarActiveTintColor: theme.colors.accent,
        tabBarInactiveTintColor: theme.colors.muted,
        tabBarLabel: ({ color, children }) => <Text numberOfLines={2} style={[theme.type.meta, { color, textAlign: "center" }]}>{children}</Text>,
        tabBarStyle: {
          height: 64 + insets.bottom,
          backgroundColor: theme.colors.surface,
          borderTopColor: theme.colors.border,
        },
      }}
    >
      <Tabs.Screen
        name="gallery"
        options={{
          title: t("tabs.gallery"),
          tabBarButtonTestID: "tab-gallery",
          tabBarIcon: ({ color, size }) => <MaterialCommunityIcons name="image-multiple" color={color} size={size} />,
        }}
      />
      <Tabs.Screen
        name="tanks"
        options={{
          title: t("tabs.tanks"),
          tabBarButtonTestID: "tab-tanks",
          tabBarIcon: ({ color, size }) => <MaterialCommunityIcons name="fishbowl" color={color} size={size} />,
        }}
      />
      <Tabs.Screen
        name="community"
        options={{
          title: t("tabs.community"),
          tabBarButtonTestID: "tab-community",
          tabBarIcon: ({ color, size }) => <MaterialCommunityIcons name="forum" color={color} size={size} />,
        }}
      />
      <Tabs.Screen
        name="species"
        options={{
          title: t("tabs.species"),
          tabBarButtonTestID: "tab-species",
          tabBarIcon: ({ color, size }) => <MaterialCommunityIcons name="fish" color={color} size={size} />,
        }}
      />
      <Tabs.Screen
        name="me"
        options={{
          title: t("tabs.me"),
          tabBarButtonTestID: "tab-me",
          tabBarIcon: ({ color, size }) => <MaterialCommunityIcons name="account-circle" color={color} size={size} />,
        }}
      />
    </Tabs>
  );
}
