import { Link } from "react-router-dom";
import { motion as Motion } from "framer-motion";
import { useNavigate } from "react-router-dom";
import { useState, useEffect } from "react";
import { isAuthenticated, getAuthSession } from "../lib/auth";
import { registerForVerification, verificationPath } from "../lib/emailVerification";
import { googleAuthEnabled, startGoogleAuth } from "../lib/googleAuth";
import logo from "../../assets/logo.png";

export default function Register() {
  const navigate = useNavigate();
  const [firstName, setFirstName] = useState("");
  const [lastName, setLastName] = useState("");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");

  useEffect(() => {
    if (isAuthenticated()) {
      const role = getAuthSession()?.user?.role;
      navigate(role === "admin" ? "/dashboard" : "/menu", { replace: true });
    }
  }, [navigate]);

  const handleRegister = async (event) => {
    event.preventDefault();
    setError("");

    if (password !== confirmPassword) {
      setError("Passwords do not match.");
      return;
    }

    if (password.length < 6) {
      setError("Password must be at least 6 characters.");
      return;
    }

    setLoading(true);

    try {
      const payload = await registerForVerification({
          firstName,
          lastName,
          email,
          password,
      });
      navigate("/verify-email", { replace: true, state: { email: payload.email, message: payload.message } });
    } catch (failure) {
      setError(failure.message || "Cannot connect to server. Please try again.");
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="auth-page">
      <Motion.form
        className="auth-card"
        onSubmit={handleRegister}
        initial={{ opacity: 0, y: 20 }}
        animate={{ opacity: 1, y: 0 }}
      >
        <div className="auth-brand">
          <img className="auth-logo" src={logo} alt="FoodJS logo" />
        </div>

        <h2 className="auth-title">CREATE ACCOUNT</h2>

        <div className="auth-name-row">
          <div className="input-group">
            <input
              className="input"
              placeholder="First Name"
              value={firstName}
              onChange={(e) => setFirstName(e.target.value)}
              required
            />
            <span className="icon">👤</span>
          </div>

          <div className="input-group">
            <input
              className="input"
              placeholder="Last Name"
              value={lastName}
              onChange={(e) => setLastName(e.target.value)}
              required
            />
            <span className="icon">👤</span>
          </div>
        </div>

        <div className="input-group">
          <input
            className="input"
            type="email"
            placeholder="Email"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            required
          />
          <span className="icon">✉️</span>
        </div>

        <div className="input-group">
          <input
            className="input"
            type="password"
            placeholder="Password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            required
          />
          <span className="icon">🔒</span>
        </div>

        <div className="input-group">
          <input
            className="input"
            type="password"
            placeholder="Confirm Password"
            value={confirmPassword}
            onChange={(e) => setConfirmPassword(e.target.value)}
            required
          />
          <span className="icon">🔒</span>
        </div>

        {error && <p className="form-error">{error}</p>}
        {error && <p className="text"><Link to={verificationPath(email)} className="link">Already registered? Verify email / Resend link</Link></p>}

        <button className="btn-primary" type="submit" disabled={loading}>
          {loading ? "Creating Account..." : "Sign-up"}
        </button>

        <button
          type="button"
          className="btn-primary"
          onClick={startGoogleAuth}
          disabled={!googleAuthEnabled}
          style={{ backgroundColor: "#4285F4" }}
        >
          {googleAuthEnabled ? "Sign-up with Google" : "Google sign-up unavailable in phone demo"}
        </button>

        <p className="text">
          Already have an account?{" "}
          <Link to="/login" className="link">
            Sign-in
          </Link>
        </p>
      </Motion.form>
    </div>
  );
}
