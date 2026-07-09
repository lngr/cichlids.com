import { Redirect } from "expo-router";

// Each tab keeps its own nested stack (app/(tabs)/<tab>/index.tsx), so there
// is no file that naturally answers the root "/" path; send it to the
// gallery, the app's primary landing screen.
export default function Index() {
  return <Redirect href="/gallery" />;
}
