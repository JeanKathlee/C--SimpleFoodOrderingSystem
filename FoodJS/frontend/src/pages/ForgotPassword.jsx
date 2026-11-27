import { useEffect, useRef, useState } from "react";
import { Link } from "react-router-dom";
import { requestPasswordReset } from "../lib/passwordRecovery";
import logo from "../../assets/logo.png";

export default function ForgotPassword() {
  const [email, setEmail] = useState("");
  const [message, setMessage] = useState("");
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(false);
  const [cooldown, setCooldown] = useState(0);
  const inFlight = useRef(false);

  useEffect(() => {
    if (!cooldown) return;
    const timer = setTimeout(() => setCooldown(seconds => Math.max(0, seconds - 1)), 1000);
    return () => clearTimeout(timer);
  }, [cooldown]);

  const submit = async event => {
    event.preventDefault();
    if (inFlight.current || cooldown) return;
    inFlight.current = true;
    setError("");
    setMessage("");
    setLoading(true);
    try {
      const payload = await requestPasswordReset(email);
      setMessage(payload.message);
      setCooldown(60);
    } catch (failure) {
      setError(failure.message);
    } finally {
      inFlight.current = false;
      setLoading(false);
    }
  };

  return (
    <div className="auth-page">
      <form className="auth-card" onSubmit={submit}>
        <div className="auth-brand"><img className="auth-logo" src={logo} alt="FoodJS logo" /></div>
        <h2 className="auth-title">RESET PASSWORD</h2>
        <p className="text">Enter your account email. We will send a link to choose a new password.</p>
        <div className="input-group">
          <input className="input" type="email" placeholder="Email" value={email}
            onChange={event => setEmail(event.target.value)} required maxLength={254} autoComplete="email" aria-label="Email address" />
        </div>
        {error && <p className="form-error" role="alert">{error}</p>}
        {message && <p className="text" role="status">{message}</p>}
        <button className="btn-primary" type="submit" disabled={loading || cooldown > 0}>
          {loading ? "Sending..." : cooldown ? `Request again in ${cooldown}s` : "Send reset link"}
        </button>
        <p className="text">Links expire after 30 minutes and can only be used once.</p>
        <p className="text"><Link to="/login" className="link">Back to login</Link></p>
      </form>
    </div>
  );
}
