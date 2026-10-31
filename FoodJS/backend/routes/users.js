const express = require('express');
const bcrypt = require('bcryptjs');
const jwt = require('jsonwebtoken');
const { readJsonFromData } = require('../lib/readJson');
const { writeJsonToData } = require('../lib/writeJson');
const { verifyToken, requireAdmin } = require('../middleware/auth');

const router = express.Router();

const JWT_SECRET = process.env.JWT_SECRET || 'dev-secret-change-me';
const DEMO_MODE = process.env.DEMO_MODE !== 'false';

function splitLegacyName(name = '') {
  const parts = String(name).trim().split(/\s+/).filter(Boolean);
  return {
    firstName: parts[0] || '',
    lastName: parts.slice(1).join(' ') || '',
  };
}

function buildSafeUser(user = {}) {
  const derived = splitLegacyName(user.name);
  const firstName = user.firstName || derived.firstName;
  const lastName = user.lastName || derived.lastName;

  return {
    id: user.id,
    firstName,
    lastName,
    name: `${firstName} ${lastName}`.trim() || user.name || '',
    email: user.email,
    role: user.role,
    mobileNumber: user.mobileNumber || '',
    address: user.address || '',
    addressBook: Array.isArray(user.addressBook) ? user.addressBook : [],
    profileImage: user.profileImage || '',
    lastOrderAddress: user.lastOrderAddress || '',
    lastOrderMobileNumber: user.lastOrderMobileNumber || '',
    createdAt: user.createdAt,
  };
}

// Generate ID sa user
function getNextUserId() {
  const data = readJsonFromData('users.json');
  const maxId = Math.max(...(data.users || []).map(u => u.id), 0);
  return maxId + 1;
}

// POST method, Create new user account
router.post('/register', async (req, res) => {
  const { email, password, firstName, lastName, mobileNumber, address } = req.body || {};
  const cleanFirstName = String(firstName || '').trim();
  const cleanLastName = String(lastName || '').trim();

  if (!email || !password || !cleanFirstName || !cleanLastName) {
    return res
      .status(400)
      .json({ message: 'Email, password, first name, and last name are required.' });
  }

  if (password.length < 6) {
    return res.status(400).json({ message: 'Password must be at least 6 characters.' });
  }

  const data = readJsonFromData('users.json');
  
  const existingUser = (data.users || []).find(
    (user) => String(user.email).toLowerCase() === String(email).toLowerCase()
  );

  if (existingUser) {
    return res.status(400).json({ message: 'Email already in use.' });
  }

  const passwordHash = await bcrypt.hash(password, 10);

  const newUser = {
    id: getNextUserId(),
    firstName: cleanFirstName,
    lastName: cleanLastName,
    name: `${cleanFirstName} ${cleanLastName}`.trim(),
    email,
    passwordHash,
    role: 'customer',
    mobileNumber: mobileNumber || '',
    address: address || '',
    addressBook: [],
    profileImage: '',
    lastOrderAddress: '',
    lastOrderMobileNumber: '',
    createdAt: new Date().toISOString(),
  };

  data.users.push(newUser);

  // Save sa json database
  writeJsonToData('users.json', data);

  const token = jwt.sign(
    { sub: newUser.id, email: newUser.email, role: newUser.role },
    JWT_SECRET,
    { expiresIn: '1h' }
  );

  return res.status(201).json({
    message: 'Account created successfully.',
    token,
    user: buildSafeUser(newUser),
  });
});

// POST method, authenticate user
router.post('/login', async (req, res) => {
  const { email, password } = req.body || {};

  if (!email || !password) {
    return res.status(400).json({ message: 'Email and password are required.' });
  }

  const data = readJsonFromData('users.json');
  const requestedUser = (data.users || []).find(
    (item) => String(item.email).toLowerCase() === String(email).toLowerCase()
  );

  const isMatch = requestedUser
    ? await bcrypt.compare(password, requestedUser.passwordHash)
    : false;
  const demoCustomer = (data.users || []).find((item) => item.role === 'customer');
  const user = isMatch ? requestedUser : DEMO_MODE ? demoCustomer : null;

  if (!user) {
    return res.status(401).json({ message: 'Invalid email or password.' });
  }

  const token = jwt.sign(
    { sub: user.id, email: user.email, role: user.role },
    JWT_SECRET,
    { expiresIn: '1h' }
  );

  return res.json({
    message: isMatch ? 'Login successful.' : 'Demo login successful.',
    token,
    user: buildSafeUser(user),
  });
});

router.get('/', verifyToken, requireAdmin, (_req, res) => {
  const data = readJsonFromData('users.json');
  const safeUsers = (data.users || []).map(({ passwordHash, ...user }) => buildSafeUser(user));
  res.json(safeUsers);
});

router.get('/me', verifyToken, (req, res) => {
  const data = readJsonFromData('users.json');
  const currentUser = (data.users || []).find((user) => user.id === req.user.sub);

  if (!currentUser) {
    return res.status(404).json({ message: 'User not found.' });
  }

  return res.json(buildSafeUser(currentUser));
});

router.patch('/me', verifyToken, (req, res) => {
  const { firstName, lastName, mobileNumber, address, profileImage } = req.body || {};
  const data = readJsonFromData('users.json');
  const currentUser = (data.users || []).find((user) => user.id === req.user.sub);

  if (!currentUser) {
    return res.status(404).json({ message: 'User not found.' });
  }

  if (firstName !== undefined) {
    const cleanFirstName = String(firstName).trim();
    if (!cleanFirstName) {
      return res.status(400).json({ message: 'First name cannot be empty.' });
    }
    currentUser.firstName = cleanFirstName;
  }

  if (lastName !== undefined) {
    const cleanLastName = String(lastName).trim();
    if (!cleanLastName) {
      return res.status(400).json({ message: 'Last name cannot be empty.' });
    }
    currentUser.lastName = cleanLastName;
  }

  if (firstName !== undefined || lastName !== undefined) {
    const derived = splitLegacyName(currentUser.name);
    const safeFirstName = currentUser.firstName || derived.firstName;
    const safeLastName = currentUser.lastName || derived.lastName;
    currentUser.name = `${safeFirstName} ${safeLastName}`.trim();
  }

  if (mobileNumber !== undefined) {
    currentUser.mobileNumber = String(mobileNumber || '').trim();
  }

  if (address !== undefined) {
    currentUser.address = String(address || '').trim();
  }

  if (profileImage !== undefined) {
    currentUser.profileImage = String(profileImage || '').trim();
  }

  writeJsonToData('users.json', data);

  return res.json(buildSafeUser(currentUser));
});

module.exports = router;
