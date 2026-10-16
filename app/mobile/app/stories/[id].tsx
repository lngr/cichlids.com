import { useLocalSearchParams } from "expo-router";
import { useAuth } from "../../src/auth/AuthProvider";
import { LoadingView } from "../../src/components/StatusView";
import { StoryEditorView } from "../../src/stories/StoryEditorView";

export default function StoryEditorScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const { status, accountId } = useAuth();
  if (status !== "authenticated") return <LoadingView />;
  return <StoryEditorView key={`${accountId}-${id}`} storyId={Number(id)} />;
}
