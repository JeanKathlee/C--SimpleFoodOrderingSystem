const GOOGLE_STATE_KEY = "foodjs_google_oauth_state";
const DEFAULT_GOOGLE_CLIENT_ID =
  "383406826534-8al6042a0n42itk48fpis06m0r81ip8k.apps.googleusercontent.com";

function randomState() {
  const bytes = new Uint8Array(24);
  crypto.getRandomValues(bytes);
  return Array.from(bytes, byte => byte.toString(16).padStart(2, "0")).join("");
}

export function startGoogleAuth() {
  const state = randomState();
  sessionStorage.setItem(GOOGLE_STATE_KEY, state);

  const params = new URLSearchParams({
    client_id: import.meta.env.VITE_GOOGLE_CLIENT_ID || DEFAULT_GOOGLE_CLIENT_ID,
    redirect_uri: `${window.location.origin}/auth/callback`,
    response_type: "code",
    scope: "email profile",
    state,
  });

  window.location.assign(`https://accounts.google.com/o/oauth2/v2/auth?${params}`);
}

export function consumeGoogleState(receivedState) {
  const expectedState = sessionStorage.getItem(GOOGLE_STATE_KEY);
  sessionStorage.removeItem(GOOGLE_STATE_KEY);
  return Boolean(receivedState && expectedState && receivedState === expectedState);
}
