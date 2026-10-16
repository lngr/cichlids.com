import { useLocalSearchParams } from "expo-router";
import { WaterChangeScreen } from "../../../../src/waterChange/WaterChangeScreen";

export default function TankWaterChangeRoute() {
  const { id } = useLocalSearchParams<{ id: string }>();
  return <WaterChangeScreen tankId={Number(id)} />;
}
