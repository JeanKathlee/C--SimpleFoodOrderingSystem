import { useEffect, useRef, useState } from "react";
import { Link, useLocation, useNavigate } from "react-router-dom";
import { readPasswordResetLink, resetPassword } from "../lib/passwordRecovery";
import { clearAuthSession } from "../lib/auth";
import styles from "./ResetPassword.module.css";
import logo from "../../assets/logo.png";

export default function ResetPassword() {
  const navigate = useNavigate();
  const location = useLocation();
  const [link] = useState(() => readPasswordResetLink(location.hash));
  const validLink = !!(link.email && link.token);
  const [password, setPassword] = useState("");
  const [confirmation, setConfirmation] = useState("");
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(false);
  const [complete, setComplete] = useState(false);
  const inFlight = useRef(false);

  useEffect(() => {
    // Keep the token only in component memory, never in history or localStorage.
    if (location.hash || location.search) navigate(location.pathname, { replace: true });
  }, [location.hash, location.search, location.pathname, navigate]);

  const submit = async event => {
    event.preventDefault();
    if (inFlight.current || !validLink || complete) return;
    setError("");
    if (password.length < 6 || password.length > 128) {
      setError("Password must be between 6 and 128 characters.");
      return;
    }
    if (password !== confirmation) {
      setError("Passwords do not match.");
      return;
    }

    inFlight.current = true; setLoading(true);
    try {
      await resetPassword(link.email, link.token, password);
      // The backend already revoked old sessions; blocked browser storage must
      // not turn a completed password reset into a false failure.
      try { clearAuthSession(); } catch { /* Storage is unavailable in this browser. */ }
      setPassword(""); setConfirmation(""); setComplete(true);
    } catch (failure) {
      setError(failure.message);
    } finally {
      inFlight.current = false;
      setLoading(false);
    }
  };

  return (
    <div className="auth-page">
      <section className="auth-card" aria-labelledby="reset-password-title">
        <div className="auth-brand"><img className="auth-logo" src={logo} alt="FoodJS logo" /></div>
        <h2 className="auth-title" id="reset-password-title">{complete ? "PASSWORD RESET" : "CHOOSE A NEW PASSWORD"}</h2>
        {complete ? <>
          <p className="text" role="status">Password changed successfully! Sign in with your new password.</p>
          <button className="btn-primary" type="button" autoFocus onClick={() => navigate("/login", { replace: true })}>OK</button>
        </> : !validLink ? <p className="form-error" role="alert">This reset link is incomplete or invalid. Request a new link below.</p> : <form className={styles.form} onSubmit={submit}>
          <div className="input-group">
            <input className="input" type="password" placeholder="New Password" value={password}
              onChange={event => setPassword(event.target.value)} required maxLength={128} autoComplete="new-password" aria-label="New password" />
          </div>
          <div className="input-group">
            <input className="input" type="password" placeholder="Confirm Password" value={confirmation}
              onChange={event => setConfirmation(event.target.value)} required maxLength={128} autoComplete="new-password" aria-label="Confirm password" />
          </div>
          {error && <p className="form-error" role="alert">{error}</p>}
          <button className="btn-primary" type="submit" disabled={loading}>
            {loading ? "Saving..." : "Reset Password"}
          </button>
        </form>}
        {!complete && <p className="text"><Link to="/forgot-password" className="link">Request a new reset link</Link></p>}
        <p className="text"><Link to="/login" className="link">Back to login</Link></p>
      </section>
    </div>
  );
}
