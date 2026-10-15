import { View } from "react-native";
import SpeciesScreen from "./species/index";
import { useTheme } from "../../src/theme";
export default function FishScreen() {
  const theme = useTheme();
  return <View testID="fish-species-list" style={{ flex: 1, backgroundColor: theme.colors.bg }}><SpeciesScreen /></View>;
}
