import { useMemo, useRef, useState } from "react";
import { Modal, Pressable, ScrollView, Switch, Text, useWindowDimensions, View } from "react-native";
import { useTranslation } from "react-i18next";
import type { RetrospectiveCard } from "@cichlids/client-core";
import { Button } from "../components/Button";
import { getDirection } from "../i18n";
import { minTouchTarget, useTheme } from "../theme";
import { CAN_DOWNLOAD_CARD, downloadCardImage, shareCardImage, type ShareCardOutcome } from "./shareCardImage";
import { layoutShareCard } from "./shareCardLayout";
import { buildShareCard, shareCardFileName } from "./shareCardModel";
import { shareCardTexts } from "./shareCardTexts";
import { ShareCardView } from "./ShareCardView";

type Notice = "downloaded" | "unavailable" | "failed" | "withoutPhoto" | null;

/**
 * The sheet that previews the shareable card of the retrospective, lets the member add the handle
 * and hands the card as an image to the share options or saves it as a download.
 */
export function ShareCardSheet({ visible, cards, handle, onClose }: {
  visible: boolean;
  cards: RetrospectiveCard[];
  handle: string | null;
  onClose: () => void;
}) {
  const theme = useTheme();
  const { t, i18n } = useTranslation();
  const { width: windowWidth } = useWindowDimensions();
  const view = useRef<View>(null);
  const [showHandle, setShowHandle] = useState(false);
  const [photoFailed, setPhotoFailed] = useState(false);
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState<Notice>(null);
  const rtl = getDirection();
  const model = useMemo(() => buildShareCard(cards, { handle, showHandle }), [cards, handle, showHandle]);
  const photoUrl = model.mostViewed?.imageUrl && !photoFailed ? model.mostViewed.imageUrl : null;
  const items = useMemo(
    () => layoutShareCard(shareCardTexts(model, key => t(key), i18n.language), { hasPhoto: photoUrl !== null }),
    [model, t, i18n.language, photoUrl]);
  const width = Math.min(windowWidth - 2 * theme.space[6], 300);

  const run = async (action: typeof shareCardImage) => {
    setBusy(true);
    setNotice(null);
    try {
      const result = await action({ view, items, photoUrl, rtl, fileName: shareCardFileName(), dialogTitle: t("retrospective.share.dialogTitle") });
      const outcome: ShareCardOutcome = result.outcome;
      if (photoUrl !== null && !result.photoIncluded) setNotice("withoutPhoto");
      else setNotice(outcome === "downloaded" ? "downloaded" : outcome === "unavailable" ? "unavailable" : null);
    } catch {
      setNotice("failed");
    } finally {
      setBusy(false);
    }
  };

  return <Modal visible={visible} animationType="slide" onRequestClose={onClose}>
    <View testID="share-card-sheet" style={{ flex: 1, backgroundColor: theme.colors.bg }}>
      <ScrollView contentContainerStyle={{ padding: theme.space[6], gap: theme.space[4], alignItems: "center" }}>
        <Text accessibilityRole="header" style={[theme.type.h1, { color: theme.colors.fg, alignSelf: "stretch" }]}>{t("retrospective.share.title")}</Text>
        <ShareCardView ref={view} items={items} photoUrl={photoUrl} width={width} onPhotoState={state => { if (state === "failed") setPhotoFailed(true); }} />
        {handle ? <Pressable testID="share-card-handle-toggle" accessibilityRole="switch" accessibilityState={{ checked: showHandle }}
          accessibilityLabel={t("retrospective.share.showHandle")} onPress={() => setShowHandle(value => !value)}
          style={{ flexDirection: "row", alignItems: "center", justifyContent: "space-between", alignSelf: "stretch", minHeight: minTouchTarget, gap: theme.space[3] }}>
          <Text style={[theme.type.body, { color: theme.colors.fg, flexShrink: 1 }]}>{t("retrospective.share.showHandle")}</Text>
          <Switch value={showHandle} onValueChange={setShowHandle} accessible={false} trackColor={{ true: theme.colors.accent, false: theme.colors.border }} />
        </Pressable> : null}
        {photoFailed && model.mostViewed?.imageUrl ? <Text testID="share-card-photo-note" style={[theme.type.meta, { color: theme.colors.muted, alignSelf: "stretch" }]}>{t("retrospective.share.withoutPhoto")}</Text> : null}
        {notice ? <Text testID="share-card-notice" accessibilityRole="alert" style={[theme.type.body, { color: notice === "failed" ? theme.colors.danger : theme.colors.fg, alignSelf: "stretch" }]}>
          {t(`retrospective.share.${notice}`)}</Text> : null}
        <View style={{ alignSelf: "stretch", gap: theme.space[2] }}>
          <Button testID="share-card-share" label={t("retrospective.share.share")} busy={busy} onPress={() => void run(shareCardImage)} />
          {CAN_DOWNLOAD_CARD ? <Button testID="share-card-download" variant="secondary" label={t("retrospective.share.download")} disabled={busy} onPress={() => void run(downloadCardImage)} /> : null}
          <Button testID="share-card-close" variant="secondary" label={t("retrospective.share.close")} onPress={onClose} />
        </View>
      </ScrollView>
    </View>
  </Modal>;
}
