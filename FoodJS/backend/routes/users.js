const express = require('express');
const bcrypt = require('bcryptjs');
const jwt = require('jsonwebtoken');
const { readJsonFromData } = require('../lib/readJson');
const { writeJsonToData } = require('../lib/writeJson');
const { verifyToken, requireAdmin } = require('../middleware/auth');

const router = express.Router();

const JWT_SECRET = process.env.JWT_SECRET || 'dev-secret-change-me';

function isValidEmail(email) {
  const [local, domain, ...rest] = email.split('@');
  return email.length <= 254 && rest.length === 0 && local.length <= 64
    && /^[\w!#$%&'*+/=?^{}|`~-]+(?:\.[\w!#$%&'*+/=?^{}|`~-]+)*$/.test(local)
    && /^[a-z\d](?:[a-z\d-]{0,61}[a-z\d])?(?:\.[a-z\d](?:[a-z\d-]{0,61}[a-z\d])?)+$/i.test(domain ?? '');
}

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
function getNextUserId(users) {
  const maxId = Math.max(...users.map(u => u.id), 0);
  return maxId + 1;
}

// POST method, Create new user account
router.post('/register', async (req, res) => {
  const { email: rawEmail, password, firstName, lastName, mobileNumber, address } = req.body || {};
  const email = typeof rawEmail === 'string' ? rawEmail.trim() : '';
  const cleanFirstName = String(firstName || '').trim();
  const cleanLastName = String(lastName || '').trim();

  if (!email || typeof password !== 'string' || !password || !cleanFirstName || !cleanLastName) {
    return res
      .status(400)
      .json({ message: 'Email, password, first name, and last name are required.' });
  }

  if (!isValidEmail(email)) {
    return res.status(400).json({ message: 'Please enter a valid email.' });
  }

  if (password.length < 6) {
    return res.status(400).json({ message: 'Password must be at least 6 characters.' });
  }

  const passwordHash = await bcrypt.hash(password, 10);
  const data = readJsonFromData('users.json');
  
  const existingUser = (data.users || []).find(
    (user) => String(user.email).trim().toLowerCase() === email.toLowerCase()
  );

  if (existingUser) {
    return res.status(400).json({ message: 'Email already in use.' });
  }

  const newUser = {
    id: getNextUserId(data.users || []),
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
  const { email: rawEmail, password } = req.body || {};
  const email = typeof rawEmail === 'string' ? rawEmail.trim() : '';

  if (!email || typeof password !== 'string' || !password) {
    return res.status(400).json({ message: 'Email and password are required.' });
  }

  const data = readJsonFromData('users.json');
  const user = (data.users || []).find(
    (item) => String(item.email).trim().toLowerCase() === email.toLowerCase()
  );

  if (!user?.passwordHash || !(await bcrypt.compare(password, user.passwordHash))) {
    return res.status(401).json({ message: 'Invalid email or password.' });
  }

  const token = jwt.sign(
    { sub: user.id, email: user.email, role: user.role },
    JWT_SECRET,
    { expiresIn: '1h' }
  );

  return res.json({
    message: 'Login successful.',
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

router.patch('/:id', verifyToken, requireAdmin, (req, res) => {
  const { firstName, lastName, mobileNumber, address, profileImage } = req.body || {};
  const userId = Number(req.params.id);
  const data = readJsonFromData('users.json');
  const customer = (data.users || []).find((user) => user.id === userId);

  if (!customer || customer.role !== 'customer') {
    return res.status(404).json({ message: 'Customer not found.' });
  }

  if (firstName !== undefined) {
    const cleanFirstName = String(firstName).trim();
    if (!cleanFirstName) {
      return res.status(400).json({ message: 'First name cannot be empty.' });
    }
    customer.firstName = cleanFirstName;
  }

  if (lastName !== undefined) {
    const cleanLastName = String(lastName).trim();
    if (!cleanLastName) {
      return res.status(400).json({ message: 'Last name cannot be empty.' });
    }
    customer.lastName = cleanLastName;
  }

  if (firstName !== undefined || lastName !== undefined) {
    customer.name = `${customer.firstName} ${customer.lastName}`.trim();
  }

  if (mobileNumber !== undefined) {
    customer.mobileNumber = String(mobileNumber || '').trim();
  }

  if (address !== undefined) {
    customer.address = String(address || '').trim();
  }

  if (profileImage !== undefined) {
    customer.profileImage = String(profileImage || '').trim();
  }

  writeJsonToData('users.json', data);
  return res.json(buildSafeUser(customer));
});

module.exports = router;
