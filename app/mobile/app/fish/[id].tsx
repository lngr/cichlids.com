import { useLocalSearchParams } from "expo-router";
import { FishScreen } from "../../src/fish/FishScreen";

export default function FishRoute() {
  const { id } = useLocalSearchParams<{ id: string }>();
  return <FishScreen id={Number(id)} />;
}
