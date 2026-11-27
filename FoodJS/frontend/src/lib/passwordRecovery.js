export function readPasswordResetLink(hash = "") {
  const params = new URLSearchParams(hash.replace(/^#/, ""));
  const email = params.get("email") || "";
  const token = params.get("token") || "";
  return { email, token: /^[a-f0-9]{64}$/i.test(token) ? token : "" };
}

async function post(path, fields) {
  let response;
  try {
    response = await fetch(path, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(fields) });
  } catch {
    throw new Error("Cannot connect to the FoodJS backend. Check that it is running, then try again.");
  }
  let payload;
  try { payload = JSON.parse(await response.text()); } catch { /* Empty/HTML proxy errors are not JSON. */ }
  if (!response.ok) {
    const fallback = response.status === 429 ? "Too many requests. Please wait before trying again."
      : response.status === 404 ? "The password-reset API was not found. Restart the FoodJS backend and try again."
      : response.status >= 500 ? "The FoodJS backend could not complete this request. Please try again later."
      : "This reset link is invalid, expired, or already used. Request a new link.";
    throw new Error(typeof payload?.message === "string" ? payload.message : fallback);
  }
  if (!payload || typeof payload.message !== "string" || payload.resetToken || payload.token)
    throw new Error("The backend returned an unexpected response. Restart it and try again.");
  return payload;
}

export const requestPasswordReset = email => post("/api/auth/forgot-password", { email: email.trim() });
export const resetPassword = (email, token, newPassword) => post("/api/auth/reset-password", { email, token, newPassword });
