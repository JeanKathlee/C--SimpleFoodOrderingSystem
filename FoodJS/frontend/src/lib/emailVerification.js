export function verificationPath(email = "") {
  return email ? `/verify-email?${new URLSearchParams({ email: email.trim() })}` : "/verify-email";
}

export function readVerificationLink(hash = "") {
  const params = new URLSearchParams(hash.replace(/^#/, ""));
  const email = params.get("email") || "";
  const token = params.get("token") || "";
  return { email, token: /^[a-f0-9]{64}$/i.test(token) ? token : "" };
}

async function post(path, body) {
  let response;
  try {
    response = await fetch(path, {
      method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(body),
    });
  } catch {
    throw new Error("Cannot connect to the FoodJS backend. Check that it is running, then try again.");
  }
  // Old API builds and proxy failures may return an empty or HTML error body.
  // Never let parsing hide the actual HTTP error, or expose raw server output.
  const raw = await response.text();
  let payload;
  try { payload = JSON.parse(raw); } catch { /* Use the HTTP status below. */ }
  if (response.status === 404)
    throw new Error("The email-verification API was not found. Restart the FoodJS backend to load the latest code, then try again.");
  if (!response.ok) {
    const fallback = response.status === 429 ? "Too many requests. Please wait before trying again."
      : response.status >= 500 ? "The FoodJS backend could not complete this request. Check its terminal for errors, then try again."
      : "Unable to complete this request. Please try again.";
    throw new Error(typeof payload?.message === "string" ? payload.message : fallback);
  }
  if (!payload || typeof payload.message !== "string")
    throw new Error("The backend returned an incomplete response. Restart it and try again; email delivery has not been confirmed.");
  return payload;
}

export async function registerForVerification(fields) {
  const result = await post("/api/users/register", fields);
  if (result.verificationRequired !== true || result.token || !result.email)
    throw new Error("The server did not return the expected email-verification response. Please contact the administrator.");
  return result;
}

export const verifyEmail = (email, token) => post("/api/auth/verify-email", { email: email.trim(), token });
export const resendVerification = email => post("/api/auth/resend-verification", { email: email.trim() });
