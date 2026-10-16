import { useRef } from "react";
import { View } from "react-native";
import { useTranslation } from "react-i18next";
import { Button } from "../components/Button";
import { blobVideo, type VideoPickButtonsProps } from "./pickedVideo";

/**
 * The web variant opens the browser's file dialog through hidden file inputs: one for the library
 * and one that asks phones for the camera. The inputs are always rendered, so browser tests can
 * set files on them.
 */
export function VideoPickButtons({ testID = "video-pick", disabled, onPicked }: VideoPickButtonsProps) {
  const { t } = useTranslation();
  const library = useRef<HTMLInputElement>(null);
  const camera = useRef<HTMLInputElement>(null);
  const handle = (event: { target: HTMLInputElement }) => {
    const file = event.target.files?.[0];
    event.target.value = "";
    if (file) onPicked(blobVideo(file, file.name));
  };
  return <View style={{ gap: 8 }}>
    <input ref={library} type="file" accept="video/mp4,video/quicktime" data-testid={`${testID}-library-input`} style={{ display: "none" }} onChange={handle} />
    <input ref={camera} type="file" accept="video/*" capture="environment" data-testid={`${testID}-record-input`} style={{ display: "none" }} onChange={handle} />
    <Button testID={`${testID}-library`} variant="secondary" label={t("video.add")} disabled={disabled} onPress={() => library.current?.click()} />
    <Button testID={`${testID}-record`} variant="secondary" label={t("video.record")} disabled={disabled} onPress={() => camera.current?.click()} />
  </View>;
}
