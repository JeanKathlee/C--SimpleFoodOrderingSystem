const AUTH_STORAGE_KEY = 'foodjs_auth';

export function saveAuthSession(session) {
  localStorage.setItem(AUTH_STORAGE_KEY, JSON.stringify(session));
}

export function getAuthSession() {
  try {
    const raw = localStorage.getItem(AUTH_STORAGE_KEY);
    if (!raw) {
      return null;
    }

    return JSON.parse(raw);
  } catch (_error) {
    return null;
  }
}

export function clearAuthSession() {
  localStorage.removeItem(AUTH_STORAGE_KEY);
}

// Logout: tell the server to revoke the token, then clear the local session
export async function logoutUser() {
  const token = getAuthSession()?.token;
  try {
    if (token) {
      await fetch('/api/users/logout', {
        method: 'POST',
        headers: { Authorization: `Bearer ${token}` },
      });
    }
  } catch (_error) {
    // Still log out locally if the server is unreachable.
  } finally {
    clearAuthSession();
  }
}

export function isAuthenticated() {
  const session = getAuthSession();
  return Boolean(session?.token && session?.user);
}

export function getUserRole() {
  const session = getAuthSession();
  return session?.user?.role || null;
}

export function isAdmin() {
  return getUserRole() === 'admin';
}

export function isCustomer() {
  return getUserRole() === 'customer';
}
