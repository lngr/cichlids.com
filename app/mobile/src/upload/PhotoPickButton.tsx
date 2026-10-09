import * as ImagePicker from "expo-image-picker";
import type { UploadFile } from "@cichlids/client-core";
import { Button } from "../components/Button";
import { toUploadFile } from "./uploadForm";

export interface PhotoPickButtonProps {
  label: string;
  disabled?: boolean;
  onPicked(file: UploadFile): void;
  onError(error: unknown): void;
}

/**
 * Opens the photo library and hands the chosen image to onPicked. A quality below 1 makes the
 * picker return JPEG, so formats the API rejects (such as HEIC) never reach the upload.
 */
export function PhotoPickButton({ label, disabled, onPicked, onError }: PhotoPickButtonProps) {
  const pick = async () => {
    try {
      const result = await ImagePicker.launchImageLibraryAsync({
        mediaTypes: ["images"],
        quality: 0.9,
        allowsEditing: false,
      });
      if (result.canceled || result.assets.length === 0) return;
      onPicked(toUploadFile(result.assets[0]));
    } catch (error) {
      onError(error);
    }
  };

  return <Button testID="upload-pick" label={label} onPress={() => void pick()} disabled={disabled} />;
}
