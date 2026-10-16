import { useLocalSearchParams } from "expo-router";
import { useAuth } from "../../src/auth/AuthProvider";
import { LoadingView } from "../../src/components/StatusView";
import { NeedsRegistration } from "../../src/registration/NeedsRegistration";
import { StoryEditorView } from "../../src/stories/StoryEditorView";

export default function StoryEditorScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const { status, accountId, provisional } = useAuth();
  if (status !== "authenticated") return <LoadingView />;
  if (provisional) return <NeedsRegistration reason="story" />;
  return <StoryEditorView key={`${accountId}-${id}`} storyId={Number(id)} />;
}
