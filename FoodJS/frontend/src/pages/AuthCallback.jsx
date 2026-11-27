import { useEffect, useRef } from "react";
import { useNavigate, useSearchParams } from "react-router-dom";
import { saveAuthSession } from "../lib/auth";
import { useNotification } from "../hooks/useNotification";
import { consumeGoogleState } from "../lib/googleAuth";

export default function AuthCallback() {
  const navigate = useNavigate();
  const { error: errorNotif } = useNotification();
  const [params] = useSearchParams();
  const hasRun = useRef(false);

  useEffect(() => {
    if (hasRun.current) return;
    hasRun.current = true;

    const handleCallback = async () => {
      const code = params.get("code");
      const state = params.get("state");
      const redirectUri = `${window.location.origin}/auth/callback`;

      if (!code || !consumeGoogleState(state)) {
        errorNotif("Google authentication could not be verified. Please try again.");
        navigate("/login");
        return;
      }

      try {
        const res = await fetch("/api/auth/google/callback", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ code, redirectUri }),
        });

        const data = await res.json();

        if (res.ok) {
          saveAuthSession({ token: data.token, user: data.user });
          navigate("/menu");
        } else {
          errorNotif(data.message || "Login failed");
          navigate("/login");
        }
      } catch (err) {
        errorNotif("Error: " + err.message);
        navigate("/login");
      }
    };

    handleCallback();
  }, [params, navigate, errorNotif]);

  return <p>Authenticating with Google...</p>;
}
