import { useLocalSearchParams } from "expo-router";
import { AdoptionScreen } from "../../../../src/adoption/AdoptionScreen";

export default function TankAdoptionRoute() {
  const { id } = useLocalSearchParams<{ id: string }>();
  return <AdoptionScreen tankId={Number(id)} />;
}
