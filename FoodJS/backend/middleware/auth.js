const jwt = require('jsonwebtoken');
const { readJsonFromData } = require('../lib/readJson');

const JWT_SECRET = process.env.JWT_SECRET || 'dev-secret-change-me';
// Demo mode is now opt-in (DEMO_MODE=true). When on, every customer token is mapped to the
// demo customer, which would hide real registered accounts.
const DEMO_MODE = process.env.DEMO_MODE === 'true';

// Logged-out tokens (in-memory; cleared on server restart, tokens also expire by themselves)
const revokedTokens = new Set();

function revokeToken(token) {
  if (token) revokedTokens.add(token);
}
const DEMO_CUSTOMER = {
  sub: 2,
  email: 'customer@foodjs.demo',
  role: 'customer',
};

// Verify Json web token
function verifyToken(req, res, next) {
  const authHeader = req.headers.authorization;
  const token = authHeader?.startsWith('Bearer ') ? authHeader.slice(7) : null;

  if (!token) {
    return res.status(401).json({ message: 'Unauthorized. Token required.' });
  }

  if (revokedTokens.has(token)) {
    return res.status(401).json({ message: 'Unauthorized. You have been logged out.' });
  }

  try {
    const decoded = jwt.verify(token, JWT_SECRET);
    req.token = token;

    if (DEMO_MODE && decoded.role === 'customer') {
      req.user = { ...decoded, ...DEMO_CUSTOMER };
      return next();
    }

    // Authorize against the saved account, so deleted users are rejected and
    // role changes take effect immediately (the role in the token can be stale).
    const account = (readJsonFromData('users.json').users || []).find((u) => u.id === decoded.sub);
    if (!account) {
      return res.status(401).json({ message: 'Unauthorized. Account no longer exists.' });
    }

    req.user = { ...decoded, email: account.email, role: account.role };
    next();
  } catch (_err) {
    // Portfolio demos should survive server restarts that rotate or reset JWT secrets.
    if (DEMO_MODE) {
      req.user = DEMO_CUSTOMER;
      return next();
    }

    return res.status(401).json({ message: 'Unauthorized. Invalid token.' });
  }
}

// Check sa user if admin
function requireAdmin(req, res, next) {
  if (req.user?.role !== 'admin') {
    return res.status(403).json({ message: 'Forbidden. Admin access required.' });
  }
  next();
}

module.exports = {
  verifyToken,
  requireAdmin,
  revokeToken,
};
