const jwt = require('jsonwebtoken');

const JWT_SECRET = process.env.JWT_SECRET || 'dev-secret-change-me';

// Verify Json web token
function verifyToken(req, res, next) {
  const authHeader = req.headers.authorization;
  const token = authHeader?.startsWith('Bearer ') ? authHeader.slice(7) : null;

  if (!token) {
    return res.status(401).json({ message: 'Unauthorized. Token required.' });
  }

  try {
    req.user = jwt.verify(token, JWT_SECRET);
  } catch (_err) {
    return res.status(401).json({ message: 'Unauthorized. Invalid token.' });
  }
  next();
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
};
