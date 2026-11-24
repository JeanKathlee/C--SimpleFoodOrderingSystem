import { Link } from "react-router-dom";
import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { motion as Motion } from "framer-motion";
import { getAuthSession, isAuthenticated, saveAuthSession } from "../lib/auth";
import { startGoogleSignIn } from "../lib/googleOAuth";
import logo from "../../assets/logo.png";

function getRoleRoute(role) {
  return role === "admin" ? "/dashboard" : "/menu";
}

export default function Login() {
  const navigate = useNavigate();
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");
  const [showPassword, setShowPassword] = useState(false);

  useEffect(() => {
    if (isAuthenticated()) {
      const role = getAuthSession()?.user?.role;
      navigate(getRoleRoute(role), { replace: true });
    }
  }, [navigate]);

  const handleLogin = async (event) => {
    event.preventDefault();
    setError("");
    setLoading(true);

    try {
      const response = await fetch("/api/users/login", {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
        },
        body: JSON.stringify({ email, password }),
      });

      const payload = await response.json();

      if (!response.ok) {
        setError(payload?.message || "Unable to login right now.");
        return;
      }

      saveAuthSession({ token: payload.token, user: payload.user });
      navigate(getRoleRoute(payload?.user?.role), { replace: true });
    } catch (_networkError) {
      setError("Cannot connect to server. Please try again.");
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="auth-page">
      <Motion.form
        className="auth-card"
        onSubmit={handleLogin}
        initial={{ opacity: 0, y: 20 }}
        animate={{ opacity: 1, y: 0 }}
        transition={{ duration: 0.5 }}
      >
        <div className="auth-brand">
          <img className="auth-logo" src={logo} alt="FoodJS logo" />
        </div>

        <h2 className="auth-title">LOGIN</h2>

        <Motion.div 
          className="input-group" 
          initial={{ opacity: 0, y: 10 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ delay: 0.1, duration: 0.4 }}
        >
          <input
            className="input"
            type="email"
            placeholder="Email"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            required
          />
          <span className="icon">📧</span>
        </Motion.div>

        <Motion.div 
          className="input-group" 
          initial={{ opacity: 0, y: 10 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ delay: 0.2, duration: 0.4 }}
        >
          <input
            className="input"
            type={showPassword ? "text" : "password"}
            placeholder="Password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            required
          />
          <button
            type="button"
            className="password-toggle"
            onClick={() => setShowPassword(!showPassword)}
            title={showPassword ? "Hide password" : "Show password"}
          >
            {showPassword ? "👁️" : "👁️‍🗨️"}
          </button>
        </Motion.div>

        {error && (
          <Motion.p 
            className="form-error"
            initial={{ opacity: 0, y: -10 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.3 }}
          >
            {error}
          </Motion.p>
        )}

        <Motion.div 
          className="forgot-container"
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          transition={{ delay: 0.3, duration: 0.4 }}
        >
          <a href="#forgot" className="forgot">Forgot Password?</a>
        </Motion.div>

        <Motion.button 
          className="btn-primary" 
          type="submit" 
          disabled={loading}
          initial={{ opacity: 0, y: 10 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ delay: 0.4, duration: 0.4 }}
        >
          {loading ? "Logging in..." : "Login"}
        </Motion.button>

        <Motion.button
          className="btn-secondary"
          type="button"
          onClick={startGoogleSignIn}
          initial={{ opacity: 0, y: 10 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ delay: 0.5, duration: 0.4 }}
        >
          Login with Google
        </Motion.button>

        <Motion.p 
          className="text"
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          transition={{ delay: 0.6, duration: 0.4 }}
        >
          Don't have an account?{" "}
          <Link to="/register" className="link">
            Sign Up
          </Link>
        </Motion.p>
      </Motion.form>
    </div>
  );
}
