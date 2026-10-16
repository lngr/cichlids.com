import { useEffect, useState } from "react";
import { Platform, ScrollView, Text, View } from "react-native";
import { Image } from "expo-image";
import * as ImagePicker from "expo-image-picker";
import { useTranslation } from "react-i18next";
import type { LibraryItem, ProfileDetail, ProfileImageRole, UploadFile } from "@cichlids/client-core";
import { apiClient } from "../api/client";
import { Button } from "../components/Button";
import { ErrorView, LoadingView } from "../components/StatusView";
import { translateApiError } from "../lib/apiErrors";
import { useTheme } from "../theme";
import { PhotoPickButton } from "../upload/PhotoPickButton";
import { toUploadFile } from "../upload/uploadForm";
import { LibraryPhotoPicker } from "./LibraryPhotoPicker";
import { AVATAR_PREVIEW_SIZE, HEADER_RATIO, PROFILE_IMAGE_ROLES, profileImageNoneKey, profileImageUrl } from "./profileImages";

/**
 * Edits the avatar and the header of the own profile. Each image comes from a photo of the media
 * library or from a fresh photo, shows as a preview and can be removed; removing keeps the photo in
 * the library.
 */
export function ProfileImagesView() {
  const theme = useTheme();
  const { t } = useTranslation();
  const [profile, setProfile] = useState<ProfileDetail | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    let cancelled = false;
    setLoadError(null);
    apiClient.me.get()
      .then((me) => !cancelled && setProfile(me.profile))
      .catch((error) => !cancelled && setLoadError(translateApiError(error, t, "common.loadError")));
    return () => {
      cancelled = true;
    };
  }, [attempt, t]);

  if (loadError) return <ErrorView message={loadError} onRetry={() => setAttempt((n) => n + 1)} />;
  if (!profile) return <LoadingView />;
  return <ScrollView style={{ backgroundColor: theme.colors.bg }} contentContainerStyle={{ padding: theme.space[4], gap: theme.space[6] }}>
    <Text style={[theme.type.body, { color: theme.colors.muted }]}>{t("profileImages.intro")}</Text>
    {PROFILE_IMAGE_ROLES.map((role) => <ImageEditor key={role} role={role} profile={profile} onChanged={setProfile} />)}
    <Text testID="profile-images-keeps-file" style={[theme.type.meta, { color: theme.colors.muted }]}>{t("profileImages.keepsFile")}</Text>
  </ScrollView>;
}

function ImageEditor({ role, profile, onChanged }: { role: ProfileImageRole; profile: ProfileDetail; onChanged: (profile: ProfileDetail) => void }) {
  const theme = useTheme();
  const { t } = useTranslation();
  const url = profileImageUrl(profile, role);
  const [picking, setPicking] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const run = async (fallbackKey: string, action: () => Promise<ProfileDetail>) => {
    if (busy) return;
    setBusy(true);
    setError(null);
    try {
      onChanged(await action());
    } catch (caught) {
      setError(translateApiError(caught, t, fallbackKey));
    } finally {
      setBusy(false);
    }
  };

  const choose = (item: LibraryItem) => {
    setPicking(false);
    void run("profileImages.saveError", () => apiClient.me.setProfileImage(role, item.id));
  };
  const upload = (file: UploadFile) => void run("profileImages.saveError", () => apiClient.me.uploadProfileImage(role, file));
  const takePhoto = async () => {
    try {
      const permission = await ImagePicker.requestCameraPermissionsAsync();
      if (!permission.granted) return;
      const result = await ImagePicker.launchCameraAsync({ mediaTypes: ["images"], quality: 0.9, allowsEditing: true, aspect: role === "avatar" ? [1, 1] : [HEADER_RATIO, 1] });
      if (result.canceled || result.assets.length === 0) return;
      upload(toUploadFile(result.assets[0]));
    } catch {
      setError(t("profileImages.pickError"));
    }
  };

  const title = t(`profileImages.${role}Title`);
  const previewLabel = t(`profileImages.${role}Preview`);
  return <View testID={`profile-images-${role}`} style={{ gap: theme.space[3] }}>
    <Text accessibilityRole="header" style={[theme.type.h2, { color: theme.colors.fg }]}>{title}</Text>
    <View accessible accessibilityRole="image" accessibilityLabel={previewLabel} testID={`profile-images-${role}-preview`}
      style={role === "avatar"
        ? { width: AVATAR_PREVIEW_SIZE, height: AVATAR_PREVIEW_SIZE, borderRadius: AVATAR_PREVIEW_SIZE / 2, overflow: "hidden", backgroundColor: theme.colors.placeholder }
        : { width: "100%", aspectRatio: HEADER_RATIO, borderRadius: theme.radius.md, overflow: "hidden", backgroundColor: theme.colors.placeholder }}>
      {url ? <Image testID={`profile-images-${role}-image`} source={url} style={{ width: "100%", height: "100%" }} contentFit="cover" /> : null}
    </View>
    {url ? null : <Text testID={`profile-images-${role}-none`} style={[theme.type.body, { color: theme.colors.muted }]}>{t(profileImageNoneKey(role))}</Text>}
    {error ? <Text testID={`profile-images-${role}-error`} accessibilityRole="alert" style={[theme.type.body, { color: theme.colors.danger }]}>{error}</Text> : null}
    <View style={{ gap: theme.space[2] }}>
      <Button testID={`profile-images-${role}-library`} variant="secondary" label={t("profileImages.chooseLibrary")} disabled={busy} onPress={() => setPicking(true)} />
      <PhotoPickButton testID={`profile-images-${role}-upload`} variant="secondary" label={busy ? t("profileImages.saving") : t("profileImages.upload")} disabled={busy}
        onPicked={upload} onError={() => setError(t("profileImages.pickError"))} />
      {Platform.OS === "web" ? null : <Button testID={`profile-images-${role}-camera`} variant="secondary" label={t("profileImages.takePhoto")} disabled={busy} onPress={() => void takePhoto()} />}
      {url ? <Button testID={`profile-images-${role}-remove`} variant="secondary" label={t("profileImages.remove")} accessibilityLabel={`${t("profileImages.remove")}: ${title}`} disabled={busy}
        onPress={() => void run("profileImages.removeError", () => apiClient.me.removeProfileImage(role))} /> : null}
    </View>
    <LibraryPhotoPicker visible={picking} onClose={() => setPicking(false)} onPicked={choose} />
  </View>;
}
