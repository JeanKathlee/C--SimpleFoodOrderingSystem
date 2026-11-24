const DEFAULT_GOOGLE_CLIENT_ID = "383406826534-8al6042a0n42itk48fpis06m0r81ip8k.apps.googleusercontent.com";
export const GOOGLE_OAUTH_STATE_KEY = "foodjs_google_oauth_state";

export function getGoogleRedirectUri() {
  return import.meta.env.VITE_GOOGLE_REDIRECT_URI?.trim() || `${window.location.origin}/auth/callback`;
}

export function startGoogleSignIn() {
  const state = window.crypto.randomUUID();
  sessionStorage.setItem(GOOGLE_OAUTH_STATE_KEY, state);

  const params = new URLSearchParams({
    client_id: import.meta.env.VITE_GOOGLE_CLIENT_ID?.trim() || DEFAULT_GOOGLE_CLIENT_ID,
    redirect_uri: getGoogleRedirectUri(),
    response_type: "code",
    scope: "openid email profile",
    state,
  });

  window.location.href = `https://accounts.google.com/o/oauth2/v2/auth?${params}`;
}
