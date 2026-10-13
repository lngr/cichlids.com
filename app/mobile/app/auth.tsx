import { Redirect } from "expo-router";

// Target of the Keycloak login redirect of the native app (cichlids://auth). The auth session
// consumes the redirect's query parameters; the router only needs a screen for the path, and it
// leads to the Me tab.
export default function AuthRedirect() {
  return <Redirect href="/me" />;
}
