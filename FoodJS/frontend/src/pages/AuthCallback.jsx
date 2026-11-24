import { useEffect, useRef } from "react";
import { useNavigate, useSearchParams } from "react-router-dom";
import { saveAuthSession } from "../lib/auth";
import { GOOGLE_OAUTH_STATE_KEY, getGoogleRedirectUri } from "../lib/googleOAuth";
import { useNotification } from "../hooks/useNotification";

export default function AuthCallback() {
  const navigate = useNavigate();
  const { error: errorNotif } = useNotification();
  const [params] = useSearchParams();
  const hasRun = useRef(false);

  useEffect(() => {
    if (hasRun.current) return;
    hasRun.current = true;

    const handleCallback = async () => {
      try {
        const expectedState = sessionStorage.getItem(GOOGLE_OAUTH_STATE_KEY);
        sessionStorage.removeItem(GOOGLE_OAUTH_STATE_KEY);
        if (!expectedState || params.get("state") !== expectedState) {
          throw new Error("Google sign-in expired. Please start again.");
        }

        if (params.get("error") === "access_denied") {
          throw new Error("Google sign-in was cancelled.");
        }

        const code = params.get("code");
        if (!code) {
          throw new Error("Google did not return an authorization code.");
        }
        const res = await fetch("/api/auth/google/callback", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ code, redirectUri: getGoogleRedirectUri() }),
        });

        const data = await res.json().catch(() => null);
        if (!res.ok || !data?.token || !data?.user) {
          throw new Error(data?.message || "Google sign-in failed. Please try again.");
        }

        saveAuthSession({ token: data.token, user: data.user });
        navigate(data.user.role === "admin" ? "/dashboard" : "/menu", { replace: true });
      } catch (err) {
        errorNotif(err.message);
        navigate("/login", { replace: true });
      }
    };

    handleCallback();
  }, [params, navigate, errorNotif]);

  return <p>Authenticating with Google...</p>;
}
