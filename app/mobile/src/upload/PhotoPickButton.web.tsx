import { useRef } from "react";
import { Button } from "../components/Button";
import type { PhotoPickButtonProps } from "./PhotoPickButton";

/**
 * The web variant opens the browser's file dialog through a hidden file input and hands the
 * chosen File to onPicked as is. The input is always rendered, so it is a stable handle for
 * browser tests to set files on.
 */
export function PhotoPickButton({ label, disabled, onPicked }: PhotoPickButtonProps) {
  const inputRef = useRef<HTMLInputElement>(null);

  return (
    <>
      <input
        ref={inputRef}
        type="file"
        accept="image/jpeg,image/png,image/webp"
        data-testid="upload-file-input"
        style={{ display: "none" }}
        onChange={(event) => {
          const file = event.target.files?.[0];
          // Clearing the value lets the same file be picked a second time, e.g. after an error.
          event.target.value = "";
          if (file) onPicked(file);
        }}
      />
      <Button testID="upload-pick" label={label} onPress={() => inputRef.current?.click()} disabled={disabled} />
    </>
  );
}
