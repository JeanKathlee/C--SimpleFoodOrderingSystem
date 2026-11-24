const AUTH_STORAGE_KEY = 'foodjs_auth';
const AUTH_CHANGE_EVENT = 'foodjs-auth-change';

export function saveAuthSession(session) {
  localStorage.setItem(AUTH_STORAGE_KEY, JSON.stringify(session));
  window.dispatchEvent(new Event(AUTH_CHANGE_EVENT));
}

function readAuthSession() {
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

function expiresAt(token) {
  try {
    const payload = token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/');
    const { exp } = JSON.parse(atob(payload));
    return Number.isFinite(exp) ? exp * 1000 : 0;
  } catch {
    return 0;
  }
}

export function getAuthSession() {
  const session = readAuthSession();
  return session?.user && expiresAt(session.token) > Date.now() ? session : null;
}

export function getAuthToken() {
  return getAuthSession()?.token ?? null;
}

export function subscribeToAuthSession(onChange) {
  let expiryTimer;

  const check = () => {
    window.clearTimeout(expiryTimer);
    const session = readAuthSession();
    if (session) {
      const remaining = expiresAt(session.token) - Date.now();
      if (session.user && remaining > 0) {
        expiryTimer = window.setTimeout(check, Math.min(remaining, 2_147_483_647));
      } else {
        localStorage.removeItem(AUTH_STORAGE_KEY);
      }
    }
    onChange();
  };

  const onStorage = (event) => {
    if (event.key === null || event.key === AUTH_STORAGE_KEY) check();
  };

  window.addEventListener(AUTH_CHANGE_EVENT, check);
  window.addEventListener('storage', onStorage);
  window.addEventListener('focus', check);
  document.addEventListener('visibilitychange', check);
  check();
  return () => {
    window.clearTimeout(expiryTimer);
    window.removeEventListener(AUTH_CHANGE_EVENT, check);
    window.removeEventListener('storage', onStorage);
    window.removeEventListener('focus', check);
    document.removeEventListener('visibilitychange', check);
  };
}

export function clearAuthSession() {
  localStorage.removeItem(AUTH_STORAGE_KEY);
  window.dispatchEvent(new Event(AUTH_CHANGE_EVENT));
}

export function isAuthenticated() {
  return getAuthSession() !== null;
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
