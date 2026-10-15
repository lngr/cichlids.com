import Stack from "expo-router/js-stack";
import { Platform, Pressable } from "react-native";
import { MaterialCommunityIcons } from "@expo/vector-icons";
import { useTranslation } from "react-i18next";
import { useAppNavigation, useNavigationAdapter } from "./NavigationProvider";
import { minTouchTarget, useTheme } from "../theme";
import { getDirection } from "../i18n";
import type { ComponentProps } from "react";

function BackButton() {
  const { send } = useAppNavigation();
  const theme = useTheme();
  const { t } = useTranslation();
  return <Pressable accessibilityRole="button" accessibilityLabel={t("navigation.back")} testID="navigation-back" onPress={() => send({ type: "BACK" })} style={{ minWidth: minTouchTarget, minHeight: minTouchTarget, alignItems: "center", justifyContent: "center" }}>
    <MaterialCommunityIcons name={getDirection() ? "arrow-right" : "arrow-left"} size={24} color={theme.colors.fg} />
  </Pressable>;
}
function MachineStackComponent(props: ComponentProps<typeof Stack>) {
  const adapter = useNavigationAdapter();
  return <Stack {...props} screenListeners={{ beforeRemove: event => { if (Platform.OS !== "web") adapter.current?.beforeRemove(event); } }} screenOptions={(args) => ({
    ...(typeof props.screenOptions === "function" ? props.screenOptions(args) : props.screenOptions),
    gestureEnabled: true,
    headerLeft: () => <BackButton />,
  })} />;
}
const Screen: (props: ComponentProps<typeof Stack.Screen> & { dangerouslySingular?: string }) => null = Stack.Screen;
export const MachineStack = Object.assign(MachineStackComponent, { Screen });
