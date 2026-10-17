import { mirrorForRtl, type ShareCardItem } from "./shareCardLayout";
import { SHARE_CARD_COLORS, SHARE_CARD_HEIGHT, SHARE_CARD_UNITS, SHARE_CARD_WIDTH } from "./shareCardModel";
import { wrapText } from "./shareCardText";
import type { ShareCardOutcome, ShareCardRequest } from "./shareCardImage";

/** Whether the platform can also save the image as a file on its own. */
export const CAN_DOWNLOAD_CARD = true;

const FONT_FAMILY = 'system-ui, -apple-system, "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif';

/**
 * Loads the photo through a CORS request, so that drawing it leaves the canvas readable. A photo
 * the server does not release to this page is null and the card is drawn without it.
 */
async function loadPhoto(url: string | null): Promise<ImageBitmap | null> {
  if (!url) return null;
  try {
    const response = await fetch(url, { mode: "cors" });
    if (!response.ok) return null;
    return await createImageBitmap(await response.blob());
  } catch {
    return null;
  }
}

function drawText(context: CanvasRenderingContext2D, item: Extract<ShareCardItem, { kind: "text" }>, scale: number) {
  const font = (size: number) => `${item.weight} ${size * scale}px ${FONT_FAMILY}`;
  let size = item.size;
  context.font = font(size);
  if (item.maxLines === 1) {
    while (size > item.size * 0.6 && context.measureText(item.text).width > item.width * scale) {
      size -= 0.5;
      context.font = font(size);
    }
  }
  const lines = wrapText(item.text, item.width * scale, item.maxLines, text => context.measureText(text).width);
  const lineHeight = item.maxLines === 1 ? item.height * scale : size * 1.25 * scale;
  context.fillStyle = item.color;
  context.textBaseline = "middle";
  lines.forEach((line, index) => {
    context.fillText(line, (item.x + item.width / 2) * scale, item.y * scale + lineHeight * (index + 0.5));
  });
}

function drawPhoto(context: CanvasRenderingContext2D, item: Extract<ShareCardItem, { kind: "photo" }>, photo: ImageBitmap, scale: number) {
  const { x, y, width, height } = { x: item.x * scale, y: item.y * scale, width: item.width * scale, height: item.height * scale };
  context.save();
  context.beginPath();
  context.roundRect(x, y, width, height, 12 * scale);
  context.clip();
  const cover = Math.max(width / photo.width, height / photo.height);
  const drawWidth = photo.width * cover;
  const drawHeight = photo.height * cover;
  context.drawImage(photo, x + (width - drawWidth) / 2, y + (height - drawHeight) / 2, drawWidth, drawHeight);
  context.restore();
}

/** Draws the card on a canvas of the shared size. */
async function renderCard(items: ShareCardItem[], photo: ImageBitmap | null, rtl: boolean): Promise<Blob> {
  const canvas = document.createElement("canvas");
  canvas.width = SHARE_CARD_WIDTH;
  canvas.height = SHARE_CARD_HEIGHT;
  const context = canvas.getContext("2d")!;
  const scale = SHARE_CARD_WIDTH / SHARE_CARD_UNITS;
  context.fillStyle = SHARE_CARD_COLORS.background;
  context.fillRect(0, 0, canvas.width, canvas.height);
  context.direction = rtl ? "rtl" : "ltr";
  context.textAlign = "center";
  for (const item of items) {
    if (item.kind === "text") drawText(context, item, scale);
    else if (photo) drawPhoto(context, item, photo, scale);
  }
  return await new Promise<Blob>((resolve, reject) =>
    canvas.toBlob(blob => (blob ? resolve(blob) : reject(new Error("The card could not be rendered"))), "image/png"));
}

async function render(request: ShareCardRequest): Promise<{ blob: Blob; photoIncluded: boolean }> {
  const photo = await loadPhoto(request.photoUrl);
  const items = mirrorForRtl(request.items.filter(item => item.kind !== "photo" || photo), request.rtl);
  return { blob: await renderCard(items, photo, request.rtl), photoIncluded: photo !== null };
}

function saveBlob(blob: Blob, fileName: string) {
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = fileName;
  document.body.appendChild(link);
  link.click();
  link.remove();
  setTimeout(() => URL.revokeObjectURL(url), 10000);
}

/** Saves the image as a PNG download. */
export async function downloadCardImage(request: ShareCardRequest): Promise<{ outcome: ShareCardOutcome; photoIncluded: boolean }> {
  const { blob, photoIncluded } = await render(request);
  saveBlob(blob, request.fileName);
  return { outcome: "downloaded", photoIncluded };
}

/**
 * Hands the image to the share options of the browser where it shares files, and saves it as a
 * download otherwise. A member who closes the share options without choosing leaves no download.
 */
export async function shareCardImage(request: ShareCardRequest): Promise<{ outcome: ShareCardOutcome; photoIncluded: boolean }> {
  const { blob, photoIncluded } = await render(request);
  const file = new File([blob], request.fileName, { type: "image/png" });
  if (typeof navigator.canShare === "function" && navigator.canShare({ files: [file] })) {
    try {
      await navigator.share({ files: [file], title: request.dialogTitle });
      return { outcome: "shared", photoIncluded };
    } catch (error) {
      if (error instanceof DOMException && error.name === "AbortError") return { outcome: "shared", photoIncluded };
    }
  }
  saveBlob(blob, request.fileName);
  return { outcome: "downloaded", photoIncluded };
}
