import { useLocalSearchParams } from "expo-router";
import { TimeLapseCameraScreen } from "../../../../src/tanks/TimeLapseCameraScreen";

export default function TankCameraRoute() {
  const { id } = useLocalSearchParams<{ id: string }>();
  return <TimeLapseCameraScreen tankId={Number(id)} />;
}
