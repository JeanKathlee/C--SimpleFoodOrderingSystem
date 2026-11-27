import { useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import logo from "../../assets/logo.png";

export default function ForgotPassword() {
  const navigate = useNavigate();
  const [email, setEmail] = useState("");
  const [message, setMessage] = useState("");
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(false);

  const submit = async event => {
    event.preventDefault();
    setError("");
    setMessage("");
    setLoading(true);
    try {
      const response = await fetch("/api/auth/forgot-password", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ email }),
      });
      const payload = await response.json();
      if (!response.ok) {
        setError(payload?.message || "Unable to start password reset.");
        return;
      }

      if (payload.resetToken) {
        const query = new URLSearchParams({ email, token: payload.resetToken });
        navigate(`/reset-password?${query}`);
        return;
      }
      setMessage(payload.message);
    } catch {
      setError("Cannot connect to server. Please try again.");
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="auth-page">
      <form className="auth-card" onSubmit={submit}>
        <div className="auth-brand"><img className="auth-logo" src={logo} alt="FoodJS logo" /></div>
        <h2 className="auth-title">RESET PASSWORD</h2>
        <p className="text">Enter the email address connected to your account.</p>
        <div className="input-group">
          <input className="input" type="email" placeholder="Email" value={email}
            onChange={event => setEmail(event.target.value)} required autoComplete="email" />
        </div>
        {error && <p className="form-error">{error}</p>}
        {message && <p className="text">{message}</p>}
        <button className="btn-primary" type="submit" disabled={loading}>
          {loading ? "Preparing Reset..." : "Continue"}
        </button>
        <p className="text"><Link to="/login" className="link">Back to login</Link></p>
      </form>
    </div>
  );
}
