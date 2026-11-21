const express = require('express');
const { readJsonFromData } = require('../lib/readJson');
const { verifyToken, requireAdmin } = require('../middleware/auth');

const router = express.Router();

function calculateInsights() {
  try {
    const ordersData = readJsonFromData('orders.json');
    const menuData = readJsonFromData('menu.json');
    const orders = ordersData.orders || [];
    const menuItems = menuData.menu || [];

    const totalOrders = orders.filter((o) => o.status !== "Cancelled").length;

    const totalRevenue = orders
      .filter((o) => o.status !== "Cancelled")
      .reduce((sum, o) => sum + (o.total || 0), 0);

    const uniqueCustomers = new Set(orders.map((o) => o.userId)).size;

    const today = new Date().toDateString();
    const todayOrders = orders.filter((o) => {
      const orderDate = new Date(o.createdAt).toDateString();
      return orderDate === today && o.status !== "Cancelled";
    }).length;

    return {
      totalOrders,
      totalRevenue,
      totalCustomers: uniqueCustomers,
      todayOrders,
      lastUpdated: new Date().toISOString(),
    };
  } catch (error) {
    console.error("Error calculating insights:", error);
    return readJsonFromData('admin-insights.json');
  }
}

router.get('/', verifyToken, requireAdmin, (_req, res) => {
  const insights = calculateInsights();
  res.json(insights);
});

module.exports = router;
