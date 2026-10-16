import { useLocalSearchParams } from "expo-router";
import { DecommissionScreen } from "../../../../src/decommission/DecommissionScreen";

export default function TankDecommissionRoute() {
  const { id } = useLocalSearchParams<{ id: string }>();
  return <DecommissionScreen tankId={Number(id)} />;
}
