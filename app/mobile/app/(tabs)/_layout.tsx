import { Tabs } from "expo-router";
import { MaterialCommunityIcons } from "@expo/vector-icons";
import { useTheme } from "../../src/theme";

export default function TabsLayout() {
  const theme = useTheme();

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
          title: "Galerie",
          tabBarIcon: ({ color, size }) => <MaterialCommunityIcons name="image-multiple" color={color} size={size} />,
        }}
      />
      <Tabs.Screen
        name="tanks"
        options={{
          title: "Becken",
          tabBarIcon: ({ color, size }) => <MaterialCommunityIcons name="fishbowl" color={color} size={size} />,
        }}
      />
      <Tabs.Screen
        name="community"
        options={{
          title: "Community",
          tabBarIcon: ({ color, size }) => <MaterialCommunityIcons name="forum" color={color} size={size} />,
        }}
      />
      <Tabs.Screen
        name="species"
        options={{
          title: "Arten",
          tabBarIcon: ({ color, size }) => <MaterialCommunityIcons name="fish" color={color} size={size} />,
        }}
      />
    </Tabs>
  );
}
