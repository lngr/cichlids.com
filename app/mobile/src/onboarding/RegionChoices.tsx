import { Pressable, Text, View } from "react-native";
import { useTranslation } from "react-i18next";
import { useTheme } from "../theme";
import { selectableRegions, type SelectableRegion } from "./standardTanks";

export function RegionChoices({ selected, onChange, regions = selectableRegions, multiple = false }: {
  selected: readonly string[]; onChange: (region: SelectableRegion) => void;
  regions?: readonly SelectableRegion[]; multiple?: boolean;
}) {
  const theme = useTheme();
  const { t } = useTranslation();
  return <View style={{ gap: theme.space[2] }}>
    {regions.map(region => <Pressable key={region} testID={`region-${region}`} accessibilityRole={multiple ? "checkbox" : "radio"}
      accessibilityState={multiple ? { checked: selected.includes(region) } : { selected: selected.includes(region) }}
      onPress={() => onChange(region)} style={{ minHeight: 48, justifyContent: "center", padding: theme.space[3], borderRadius: theme.radius.md,
        backgroundColor: theme.colors.surface, borderWidth: selected.includes(region) ? 2 : 1, borderColor: selected.includes(region) ? theme.colors.fg : theme.colors.border }}>
      <Text style={[theme.type.body, { color: theme.colors.fg }]}>{t(`regions.${region}`)}</Text>
    </Pressable>)}
  </View>;
}
