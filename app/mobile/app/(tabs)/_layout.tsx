import { Tabs } from "expo-router";
import { MaterialCommunityIcons } from "@expo/vector-icons";
import { useTranslation } from "react-i18next";
import { useTheme } from "../../src/theme";

export default function TabsLayout() {
  const theme = useTheme();
  const { t } = useTranslation();

  return (
    <Tabs
      screenOptions={{
        headerShown: false,
        tabBarActiveTintColor: theme.colors.accent,
        tabBarInactiveTintColor: theme.colors.muted,
        tabBarStyle: {
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
          tabBarIcon: ({ color, size }) => <MaterialCommunityIcons name="fishbowl" color={color} size={size} />,
        }}
      />
      <Tabs.Screen
        name="community"
        options={{
          title: t("tabs.community"),
          tabBarIcon: ({ color, size }) => <MaterialCommunityIcons name="forum" color={color} size={size} />,
        }}
      />
      <Tabs.Screen
        name="species"
        options={{
          title: t("tabs.species"),
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
