import { useState } from "react";
import { Link, useNavigate, useSearchParams } from "react-router-dom";
import logo from "../../assets/logo.png";

export default function ResetPassword() {
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const [password, setPassword] = useState("");
  const [confirmation, setConfirmation] = useState("");
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(false);

  const submit = async event => {
    event.preventDefault();
    setError("");
    if (password.length < 6) {
      setError("Password must be at least 6 characters.");
      return;
    }
    if (password !== confirmation) {
      setError("Passwords do not match.");
      return;
    }

    setLoading(true);
    try {
      const response = await fetch("/api/auth/reset-password", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          email: params.get("email"),
          token: params.get("token"),
          newPassword: password,
        }),
      });
      const payload = await response.json();
      if (!response.ok) {
        setError(payload?.message || "Unable to reset password.");
        return;
      }
      navigate("/login", { replace: true });
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
        <h2 className="auth-title">CHOOSE A NEW PASSWORD</h2>
        <div className="input-group">
          <input className="input" type="password" placeholder="New Password" value={password}
            onChange={event => setPassword(event.target.value)} required autoComplete="new-password" />
        </div>
        <div className="input-group">
          <input className="input" type="password" placeholder="Confirm Password" value={confirmation}
            onChange={event => setConfirmation(event.target.value)} required autoComplete="new-password" />
        </div>
        {error && <p className="form-error">{error}</p>}
        <button className="btn-primary" type="submit" disabled={loading}>
          {loading ? "Saving..." : "Reset Password"}
        </button>
        <p className="text"><Link to="/login" className="link">Back to login</Link></p>
      </form>
    </div>
  );
}
