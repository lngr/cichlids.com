import { Redirect } from "expo-router";

// Target of the Keycloak login redirect (cichlids://auth on native, <origin>/auth on the web).
// The auth session consumes the redirect's query parameters; the router only needs a screen for
// the path, and it leads to the Me tab.
export default function AuthRedirect() {
  return <Redirect href="/me" />;
}
