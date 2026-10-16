import { useLocalSearchParams } from "expo-router";
import { TankAudienceScreen } from "../../../../src/tanks/TankAudienceScreen";

export default function TankAudienceRoute() {
  const { id } = useLocalSearchParams<{ id: string }>();
  return <TankAudienceScreen tankId={Number(id)} />;
}
