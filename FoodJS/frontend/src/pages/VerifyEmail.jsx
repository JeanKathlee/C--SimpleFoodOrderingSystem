import { useEffect, useRef, useState } from "react";
import { Link, useLocation, useNavigate } from "react-router-dom";
import { readVerificationLink, verifyEmail, resendVerification } from "../lib/emailVerification";
import styles from "./VerifyEmail.module.css";
import logo from "../../assets/logo.png";

export default function VerifyEmail() {
  const location = useLocation();
  const navigate = useNavigate();
  const [link] = useState(() => readVerificationLink(location.hash));
  const hasConfirmationLink = !!(link.email && link.token);
  const [email, setEmail] = useState(() => link.email || location.state?.email || new URLSearchParams(location.search).get("email") || "");
  const [message, setMessage] = useState(hasConfirmationLink ? "Verifying your email..." : location.state?.message || "Verify your email before signing in. Check your inbox and spam folder, or request a new link below.");
  const [error, setError] = useState(location.hash && !hasConfirmationLink ? "This verification link is incomplete or invalid. Request a new verification email." : "");
  const [busy, setBusy] = useState(hasConfirmationLink ? "verify" : "");
  const [verified, setVerified] = useState(false);
  const [cooldown, setCooldown] = useState(0);
  const inFlight = useRef(false);
  const verificationRequest = useRef(null);
  const formRef = useRef(null);

  useEffect(() => {
    // Keep the one-time token in component memory only, not URL/history/storage.
    if (location.hash) navigate(location.pathname + location.search, { replace: true, state: location.state });
  }, [location.hash, location.pathname, location.search, location.state, navigate]);

  useEffect(() => {
    if (!hasConfirmationLink) return;
    let active = true;
    // Reuse the pending POST during StrictMode's effect setup/cleanup replay.
    // Each setup subscribes anew, so a cleaned-up effect cannot swallow success.
    verificationRequest.current ??= verifyEmail(link.email, link.token);
    verificationRequest.current.then(() => {
      if (!active) return;
      setMessage("Email verified successfully!");
      setVerified(true);
      setBusy("");
    }, failure => {
      if (!active) return;
      setMessage("We could not verify this link. Request a new email below.");
      setError(failure.message || "Cannot connect to the server. Please try again.");
      setBusy("");
    });
    return () => { active = false; };
  }, [hasConfirmationLink, link.email, link.token]);

  useEffect(() => {
    if (!cooldown) return;
    const timer = setTimeout(() => setCooldown(seconds => Math.max(0, seconds - 1)), 1000);
    return () => clearTimeout(timer);
  }, [cooldown]);

  const resend = async () => {
    if (inFlight.current || busy || cooldown || !formRef.current?.reportValidity()) return;
    inFlight.current = true; setBusy("resend"); setError("");
    try {
      const result = await resendVerification(email);
      setMessage(result.message);
      setCooldown(60);
    } catch (failure) {
      setError(failure.message || "Cannot connect to the server. Please try again.");
    } finally { inFlight.current = false; setBusy(""); }
  };

  return <div className="auth-page">
    <section className={["auth-card", styles.card].join(" ")} aria-labelledby="verify-title">
      <div className="auth-brand"><img className="auth-logo" src={logo} alt="FoodJS logo" /></div>
      <h2 className="auth-title" id="verify-title">{verified ? "EMAIL VERIFIED" : busy === "verify" ? "VERIFYING YOUR EMAIL" : "VERIFY YOUR EMAIL"}</h2>
      <p className={styles.message} role="status">{message}</p>
      {verified && <button className="btn-primary" type="button" autoFocus onClick={() => navigate("/login", { replace: true })}>OK</button>}
      {!verified && busy !== "verify" && <form ref={formRef} onSubmit={event => { event.preventDefault(); resend(); }}>
        <label className={styles.label} htmlFor="verification-email">Email address *</label>
        <div className="input-group"><input id="verification-email" className="input" type="email" required maxLength={254}
          autoComplete="email" value={email} readOnly={!!link.token} onChange={event => setEmail(event.target.value)} /></div>
        {error && <p className="form-error" role="alert">{error}</p>}
        <button className="btn-primary" type="submit"
          disabled={!!busy || cooldown > 0}>
          {busy === "resend" ? "Sending..." : cooldown ? `Resend in ${cooldown}s` : "Resend verification email"}</button>
        <p className={styles.hint}>Links expire after 30 minutes. Each link can be used only once.</p>
      </form>}
      {!verified && <p className="text"><Link to="/login" className="link">Back to sign in</Link></p>}
    </section>
  </div>;
}
