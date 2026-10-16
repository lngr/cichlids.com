import { View } from "react-native";
import * as ImagePicker from "expo-image-picker";
import { useTranslation } from "react-i18next";
import { Button } from "../components/Button";
import type { PickedVideo, VideoPickButtonsProps } from "./pickedVideo";
import { blobVideo } from "./pickedVideo";

/** Reads the chosen asset as a blob; the native blob refers to the file and does not load it into memory. */
async function fromAsset(asset: ImagePicker.ImagePickerAsset): Promise<PickedVideo> {
  const blob = await (await fetch(asset.uri)).blob();
  return blobVideo(blob, asset.fileName ?? "video.mp4");
}

/** Buttons to choose a video from the library or to record one with the camera. */
export function VideoPickButtons({ testID = "video-pick", disabled, onPicked, onError }: VideoPickButtonsProps) {
  const { t } = useTranslation();
  const run = async (launch: () => Promise<ImagePicker.ImagePickerResult>) => {
    try {
      const result = await launch();
      if (result.canceled || result.assets.length === 0) return;
      onPicked(await fromAsset(result.assets[0]));
    } catch (error) {
      onError(error);
    }
  };
  return <View style={{ gap: 8 }}>
    <Button testID={`${testID}-library`} variant="secondary" label={t("video.add")} disabled={disabled}
      onPress={() => void run(() => ImagePicker.launchImageLibraryAsync({ mediaTypes: ["videos"], allowsEditing: false }))} />
    <Button testID={`${testID}-record`} variant="secondary" label={t("video.record")} disabled={disabled}
      onPress={() => void run(async () => {
        const permission = await ImagePicker.requestCameraPermissionsAsync();
        if (!permission.granted) return { canceled: true, assets: null };
        return ImagePicker.launchCameraAsync({ mediaTypes: ["videos"], allowsEditing: false });
      })} />
  </View>;
}
