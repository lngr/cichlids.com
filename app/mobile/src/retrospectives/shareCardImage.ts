import type { RefObject } from "react";
import type { View } from "react-native";
import { captureRef } from "react-native-view-shot";
import * as Sharing from "expo-sharing";
import { SHARE_CARD_HEIGHT, SHARE_CARD_WIDTH } from "./shareCardModel";
import type { ShareCardItem } from "./shareCardLayout";

/** What happened to the image: handed to the share options, saved as a download, or not possible. */
export type ShareCardOutcome = "shared" | "downloaded" | "unavailable";

/** Whether the platform can also save the image as a file on its own. */
export const CAN_DOWNLOAD_CARD = false;

/** What an image needs: the visible card view (native) or its layout and photo (web). */
export type ShareCardRequest = {
  view: RefObject<View | null>;
  items: ShareCardItem[];
  photoUrl: string | null;
  rtl: boolean;
  fileName: string;
  dialogTitle: string;
};

/**
 * Renders the card view to a PNG of the shared size and opens the share options of the operating
 * system. The photo is part of the view that is captured.
 */
export async function shareCardImage(request: ShareCardRequest): Promise<{ outcome: ShareCardOutcome; photoIncluded: boolean }> {
  if (!(await Sharing.isAvailableAsync())) return { outcome: "unavailable", photoIncluded: false };
  const uri = await captureRef(request.view, {
    format: "png",
    quality: 1,
    result: "tmpfile",
    width: SHARE_CARD_WIDTH,
    height: SHARE_CARD_HEIGHT,
    fileName: request.fileName.replace(/\.png$/, ""),
  });
  await Sharing.shareAsync(uri, { mimeType: "image/png", UTI: "public.png", dialogTitle: request.dialogTitle });
  return { outcome: "shared", photoIncluded: request.photoUrl !== null };
}

/** Saving as a file is only offered where CAN_DOWNLOAD_CARD is true. */
export async function downloadCardImage(request: ShareCardRequest): Promise<{ outcome: ShareCardOutcome; photoIncluded: boolean }> {
  return shareCardImage(request);
}
