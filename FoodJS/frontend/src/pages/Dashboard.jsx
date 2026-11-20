import { useEffect, useMemo, useState } from "react";
import { useNavigate } from "react-router-dom";
import { clearAuthSession, getAuthSession } from "../lib/auth";
import AdminSidebar from "../components/admin/AdminSidebar";
import AdminTopbar from "../components/admin/AdminTopbar";
import AnalyticsCards from "../components/admin/AnalyticsCards";
import RevenueChart from "../components/admin/RevenueChart";
import OrdersPanel from "../components/admin/OrdersPanel";
import MenuManagement from "../components/admin/MenuManagement";
import CategoryManagement from "../components/admin/CategoryManagement";
import CouponManagement from "../components/admin/CouponManagement";
import PopularItems from "../components/admin/PopularItems";
import ConfirmModal from "../components/admin/ConfirmModal";
import { useNotification } from "../hooks/useNotification";
import "../dashboard.css";

const DEFAULT_STATUS_OPTIONS = ["Pending", "Preparing", "Ready", "Cancelled", "Delivered"];
const DEFAULT_WEEKLY_REVENUE = [
  { day: "Mon", amount: 0 },
  { day: "Tue", amount: 0 },
  { day: "Wed", amount: 0 },
  { day: "Thu", amount: 0 },
  { day: "Fri", amount: 0 },
  { day: "Sat", amount: 0 },
  { day: "Sun", amount: 0 },
];

function matchesDate(dateValue, fromDate, toDate) {
  const value = new Date(dateValue).getTime();

  if (fromDate) {
    const from = new Date(`${fromDate}T00:00:00.000Z`).getTime();
    if (value < from) {
      return false;
    }
  }

  if (toDate) {
    const to = new Date(`${toDate}T23:59:59.000Z`).getTime();
    if (value > to) {
      return false;
    }
  }

  return true;
}

function compareOrdersNewestFirst(a, b) {
  return new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime();
}

function normalizeMenu(items = [], categories = []) {
  return items.map((item, index) => {
    const category = categories.find(
      (entry) => entry.id === item.categoryId || entry.name === item.category
    );

    return {
      id: String(item.id ?? `M-${1000 + index}`),
      name: item.name || "Unnamed Item",
      description: item.description ?? "",
      image: item.image ?? null,
      categoryId: item.categoryId || category?.id || categories[0]?.id || "",
      price: Number(item.price || 0),
      prepTime: Number(item.prepTime || 10),
      isAvailable: item.isAvailable !== false,
      soldCount: Number(item.soldCount || 0),
      category: item.category || category?.name || "",
    };
  });
}

function normalizeOrders(items = []) {
  return items.map((order, index) => {
    const itemCount =
      typeof order.itemCount === "number"
        ? order.itemCount
        : (order.items || []).reduce((total, item) => total + Number(item.quantity || 0), 0);
    const total =
      typeof order.total === "number"
        ? order.total
        : (order.items || []).reduce(
            (sum, item) => sum + Number(item.price || 0) * Number(item.quantity || 0),
            0
          );

    return {
      id: String(order.id ?? `ORD-${index + 1}`),
      customerName: order.customerName || `Customer ${index + 1}`,
      status: order.status || "Pending",
      total,
      itemCount,
      createdAt: order.createdAt || new Date().toISOString(),
      items: order.items || []
    };
  });
}

function calculateSoldCounts(items = [], allOrders = []) {
  return items.map((item) => {
    let soldCount = 0;
    
    allOrders.forEach((order) => {
      if (order.items && Array.isArray(order.items)) {
        order.items.forEach((orderItem) => {
          if (String(orderItem.menuItemId) === String(item.id)) {
            soldCount += Number(orderItem.quantity || 0);
          }
        });
      }
    });
    
    return {
      ...item,
      soldCount,
    };
  });
}

export default function Dashboard() {
  const navigate = useNavigate();
  const { success, error: errorNotif } = useNotification();
  const session = useMemo(() => getAuthSession(), []);
  const [activeSection, setActiveSection] = useState("dashboard");
  const [categories, setCategories] = useState([]);
  const [menuItems, setMenuItems] = useState([]);
  const [menuRevision, setMenuRevision] = useState(0);
  const [menuCategories, setMenuCategories] = useState([]);
  const [menuDraft, setMenuDraft] = useState({ dirty: false, busy: false });
  const [menuCategoryLoading, setMenuCategoryLoading] = useState(true);
  const [menuCategoryError, setMenuCategoryError] = useState("");
  const [coupons, setCoupons] = useState([]);
  const [allOrders, setAllOrders] = useState([]);
  const [weeklyRevenue, setWeeklyRevenue] = useState(DEFAULT_WEEKLY_REVENUE);
  const [statusOptions, setStatusOptions] = useState(DEFAULT_STATUS_OPTIONS);
  const [filters, setFilters] = useState({
    fromDate: "",
    toDate: "",
    status: "All",
    customer: "",
    search: "",
  });
  const [syncLabel, setSyncLabel] = useState("Live sync active");
  const [logoutConfirm, setLogoutConfirm] = useState(false);
  const [modalConfig, setModalConfig] = useState({
    open: false,
    title: "",
    message: "",
    confirmLabel: "Delete",
    cancelLabel: "Cancel",
    hideCancel: false,
    confirmVariant: "danger",
    onConfirm: () => {},
  });

  const bumpSyncState = (source) => {
    const time = new Date().toLocaleTimeString("en-PH", {
      hour: "2-digit",
      minute: "2-digit",
    });
    setSyncLabel(`Synced from ${source} at ${time}`);
  };

  const loadAdminData = async () => {
    try {
      const [categoriesResponse, menuResponse, ordersResponse, insightsResponse, couponsResponse] =
        await Promise.all([
          fetch("/api/categories"),
          fetch("/api/menu"),
          fetch("/api/orders"),
          fetch("/api/admin-insights"),
          fetch("/api/coupons"),
        ]);

      const [categoryData, menuData, , insightData, couponData] = await Promise.all([
        categoriesResponse.json(),
        menuResponse.json(),
        ordersResponse.json(),
        insightsResponse.json(),
        couponsResponse.json(),
      ]);

      const nextCategories = Array.isArray(categoryData) ? categoryData : [];
      setCategories(nextCategories);
      setMenuItems(normalizeMenu(Array.isArray(menuData) ? menuData : [], nextCategories));
      setCoupons(Array.isArray(couponData) ? couponData : []);
      setWeeklyRevenue(
        Array.isArray(insightData?.weeklyRevenue) && insightData.weeklyRevenue.length > 0
          ? insightData.weeklyRevenue
          : DEFAULT_WEEKLY_REVENUE
      );
      setStatusOptions(
        Array.isArray(insightData?.statusOptions) && insightData.statusOptions.length > 0
          ? insightData.statusOptions
          : DEFAULT_STATUS_OPTIONS
      );
      bumpSyncState("server data");
    } catch (_error) {
      setSyncLabel("Live sync unavailable");
    }
  };

  const loadMenuCategories = async () => {
    try {
      const response = await fetch(import.meta.env.VITE_MENU_CATEGORY_URL || "/api/categories");
      if (!response.ok) throw new Error("Could not load menu categories. Please retry.");
      const data = await response.json();
      if (!Array.isArray(data)) throw new Error("The categories response was incomplete.");
      setMenuCategories(data); setMenuCategoryError("");
    } catch (error) { setMenuCategoryError(error.message); }
    finally { setMenuCategoryLoading(false); }
  };

  const loadAllOrders = async () => {
    try {
      const response = await fetch("/api/orders/admin/all", {
        headers: { Authorization: `Bearer ${session?.token}` },
      });
      if (response.ok) {
        const data = await response.json();
        setAllOrders(normalizeOrders(Array.isArray(data) ? data : []));
      }
    } catch (_error) {
      // silent fail
    }
  };

  const handleUpdateOrderStatus = async (orderId, newStatus) => {
    try {
      const response = await fetch(`/api/orders/${orderId}/status`, {
        method: "PATCH",
        headers: {
          "Content-Type": "application/json",
          Authorization: `Bearer ${session?.token}`,
        },
        body: JSON.stringify({ status: newStatus }),
      });

      if (response.ok) {
        const updated = await response.json();
        const normalized = normalizeOrders([updated]);
        setAllOrders((prev) =>
          prev.map((order) => (order.id === orderId ? normalized[0] : order))
        );
        success("Order status updated successfully!");
      } else {
        errorNotif("Failed to update order status");
      }
    } catch (err) {
      console.error("Error:", err);
      errorNotif("Error updating order status");
    }
  };

  const handleDeleteOrder = (orderId) => {
    const order = allOrders.find((item) => item.id === orderId);

    setModalConfig({
      open: true,
      title: "Delete order",
      message: `Order ${orderId}${order?.customerName ? ` for ${order.customerName}` : ""} will be permanently removed from the records.`,
      confirmLabel: "Delete Order",
      onConfirm: async () => {
        try {
          const response = await fetch(`/api/orders/${orderId}`, {
            method: "DELETE",
            headers: {
              Authorization: `Bearer ${session?.token}`,
            },
          });

          if (response.ok) {
            setAllOrders((prev) => prev.filter((item) => item.id !== orderId));
            setModalConfig((current) => ({ ...current, open: false }));
            success("Order deleted successfully!");
            bumpSyncState("orders");
          } else {
            errorNotif("Failed to delete order");
            setModalConfig((current) => ({ ...current, open: false }));
          }
        } catch (err) {
          console.error("Error:", err);
          errorNotif("Error deleting order");
          setModalConfig((current) => ({ ...current, open: false }));
        }
      },
    });
  };

  useEffect(() => {
    if (activeSection === "menu") {
      loadMenuCategories();
      const menuPollingId = setInterval(loadMenuCategories, 15000);
      return () => clearInterval(menuPollingId);
    }
    loadAdminData();
    loadAllOrders();
    const pollingId = setInterval(() => {
      loadAdminData();
      loadAllOrders();
    }, 15000);

    return () => clearInterval(pollingId);
    // The loaders intentionally retain the authenticated session captured on mount.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [activeSection]);

  const filteredAdminOrders = useMemo(() => {
    const customerFilter = filters.customer.trim().toLowerCase();
    const searchFilter = filters.search.trim().toLowerCase();

    return allOrders
      .filter((order) => {
        const statusMatch = filters.status === "All" || order.status === filters.status;
        const dateMatch = matchesDate(order.createdAt, filters.fromDate, filters.toDate);
        const customerMatch =
          !customerFilter || order.customerName.toLowerCase().includes(customerFilter);

        const searchMatch =
          !searchFilter ||
          order.id.toLowerCase().includes(searchFilter) ||
          order.customerName.toLowerCase().includes(searchFilter);

        return statusMatch && dateMatch && customerMatch && searchMatch;
      })
      .sort(compareOrdersNewestFirst);
  }, [filters, allOrders]);

  const dashboardStats = useMemo(() => {
    const statsData = filteredAdminOrders;
    
    const completedOrders = statsData.filter((order) => order.status === "Delivered").length;
    const cancelledOrders = statsData.filter((order) => order.status === "Cancelled").length;
    const pendingOrders = statsData.filter((order) => order.status === "Pending").length;
    const preparingOrders = statsData.filter((order) => order.status === "Preparing").length;
    
    const revenueTotal = statsData.reduce((sum, order) => {
      if (order.status === "Cancelled") return sum;
      return sum + order.total;
    }, 0);

    return {
      totalOrdersFiltered: statsData.length,
      revenueFiltered: revenueTotal,
      completedOrders,
      cancelledOrders,
      pendingOrders,
      preparingOrders,
    };
  }, [filteredAdminOrders]);

  const topItems = useMemo(() => {
    const itemsWithRealCounts = calculateSoldCounts(menuItems, allOrders);
    return [...itemsWithRealCounts].sort((a, b) => b.soldCount - a.soldCount).slice(0, 5);
  }, [menuItems, allOrders]);

  const handleLogout = () => {
    setLogoutConfirm(true);
  };

  const confirmLogout = () => {
    clearAuthSession();
    setLogoutConfirm(false);
    navigate("/");
  };

  const handleOrderFilter = (field, value) => {
    setFilters((current) => ({ ...current, [field]: value }));
  };

  const handleAddMenuItem = async (payload) => {
    try {
      const session = getAuthSession();
      const response = await fetch("/api/menu", {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          Authorization: `Bearer ${session?.token}`,
        },
        body: JSON.stringify(payload),
      });

      if (!response.ok) {
        const error = await response.json();
        return { ok: false, message: error?.message || "Failed to add menu item" };
      }

      const newItem = await response.json();
      setMenuItems((current) => [newItem, ...current]);
      success("Menu item added successfully!");
      bumpSyncState("menu");
      setMenuRevision(value => value + 1);
      return { ok: true };
    } catch (err) {
      return { ok: false, message: "Could not connect to save the item. Please retry." };
    }
  };

  const handleUpdateMenuItem = async (id, payload) => {
    try {
      const session = getAuthSession();
      const response = await fetch(`/api/menu/${id}`, {
        method: "PUT",
        headers: {
          "Content-Type": "application/json",
          Authorization: `Bearer ${session?.token}`,
        },
        body: JSON.stringify(payload),
      });

      if (!response.ok) {
        const error = await response.json();
        return { ok: false, message: error?.message || "Failed to update menu item" };
      }

      const updatedItem = await response.json();
      setMenuItems((current) =>
        current.map((item) => (item.id === id ? updatedItem : item))
      );
      success("Menu item updated successfully!");
      bumpSyncState("menu");
      setMenuRevision(value => value + 1);
      return { ok: true };
    } catch (err) {
      return { ok: false, message: "Could not connect to save your changes. Please retry." };
    }
  };

  const handleDeleteMenuItem = (item, onDeleted) => {
    const id = item.id;
    setModalConfig({
      open: true,
      title: "Delete menu item",
      message: `Delete “${item.name}”? This permanently removes it from the menu. Items used in existing orders must be marked unavailable instead.`,
      confirmLabel: "Delete Item",
      onConfirm: async () => {
        try {
          const session = getAuthSession();
          const response = await fetch(`/api/menu/${id}`, {
            method: "DELETE",
            headers: {
              Authorization: `Bearer ${session?.token}`,
            },
          });

          if (!response.ok) {
            const error = await response.json();
            throw new Error(error?.message || "Failed to delete menu item");
          }

          setMenuItems((current) => current.filter((item) => item.id !== id));
          setModalConfig((current) => ({ ...current, open: false }));
          success("Menu item deleted successfully!");
          bumpSyncState("menu");
          setMenuRevision(value => value + 1);
          onDeleted?.();
        } catch (err) {
          throw new Error(err.message || "Could not delete the menu item. Please retry.");
        }
      },
    });
  };

  const handleSetMenuAvailability = async (id, isAvailable) => {
    try {
      const response = await fetch(`/api/menu/${id}/availability`, {
        method: "PATCH", headers: { "Content-Type": "application/json", Authorization: `Bearer ${session?.token}` },
        body: JSON.stringify({ isAvailable }),
      });
      const data = await response.json();
      if (!response.ok) return { ok: false, message: data.message || "Could not update availability." };
      setMenuItems(current => current.map(item => item.id === id ? data : item));
      setMenuRevision(value => value + 1);
      success(isAvailable ? "Item is now available." : "Item marked unavailable.");
      return { ok: true };
    } catch (_error) { return { ok: false, message: "Could not connect to update availability. Please retry." }; }
  };

  const handleAddCategory = async (payload) => {
    try {
      const session = getAuthSession();
      const response = await fetch("/api/categories", {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          Authorization: `Bearer ${session?.token}`,
        },
        body: JSON.stringify(payload),
      });

      if (!response.ok) {
        const error = await response.json();
        errorNotif(error?.message || "Failed to add category");
        return;
      }

      const newCategory = await response.json();
      setCategories((current) => [newCategory, ...current]);
      success("Category added successfully!");
      bumpSyncState("categories");
    } catch (err) {
      errorNotif("Error adding category");
    }
  };

  const handleUpdateCategory = async (id, payload) => {
    try {
      const session = getAuthSession();
      const response = await fetch(`/api/categories/${id}`, {
        method: "PUT",
        headers: {
          "Content-Type": "application/json",
          Authorization: `Bearer ${session?.token}`,
        },
        body: JSON.stringify(payload),
      });

      if (!response.ok) {
        const error = await response.json();
        errorNotif(error?.message || "Failed to update category");
        return;
      }

      const updatedCategory = await response.json();
      setCategories((current) =>
        current.map((category) => (category.id === id ? updatedCategory : category))
      );
      success("Category updated successfully!");
      bumpSyncState("categories");
    } catch (err) {
      errorNotif("Error updating category");
    }
  };

  const handleDeleteCategory = async (id, itemCount) => {
    const category = categories.find((item) => item.id === id);

    if (itemCount > 0) {
      setModalConfig({
        open: true,
        title: "Category still has items",
        message: `${category?.name || "This category"} has ${itemCount} menu item${itemCount === 1 ? "" : "s"}. Delete or move those items before deleting the category.`,
        confirmLabel: "Manage Items",
        hideCancel: true,
        confirmVariant: "primary",
        onConfirm: () => {
          setModalConfig((current) => ({ ...current, open: false }));
          setActiveSection("menu");
        },
      });
      return;
    }

    setModalConfig({
      open: true,
      title: "Delete category",
      message: `${category?.name || "This category"} will be removed from your menu organization.`,
      confirmLabel: "Delete Category",
      cancelLabel: "Keep Category",
      hideCancel: false,
      confirmVariant: "danger",
      onConfirm: async () => {
        try {
          const session = getAuthSession();
          const response = await fetch(`/api/categories/${id}`, {
            method: "DELETE",
            headers: {
              Authorization: `Bearer ${session?.token}`,
            },
          });

          if (!response.ok) {
            const error = await response.json();
            errorNotif(error?.message || "Failed to delete category");
            setModalConfig((current) => ({ ...current, open: false }));
            return;
          }

          setCategories((current) => current.filter((category) => category.id !== id));
          setModalConfig({
            open: true,
            title: "Category deleted",
            message: `${category?.name || "The category"} was removed successfully.`,
            confirmLabel: "Done",
            hideCancel: true,
            confirmVariant: "primary",
            onConfirm: () => setModalConfig((current) => ({ ...current, open: false })),
          });
          success("Category deleted successfully!");
          bumpSyncState("categories");
        } catch (err) {
          errorNotif("Error deleting category");
          setModalConfig((current) => ({ ...current, open: false }));
        }
      },
    });
  };

  const handleAddCoupon = async (payload) => {
    try {
      const session = getAuthSession();
      const response = await fetch("/api/coupons", {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          Authorization: `Bearer ${session?.token}`,
        },
        body: JSON.stringify(payload),
      });

      if (!response.ok) {
        const error = await response.json();
        errorNotif(error?.message || "Failed to add coupon");
        return;
      }

      const newCoupon = await response.json();
      setCoupons((current) => [newCoupon, ...current]);
      success("Coupon added successfully!");
      bumpSyncState("coupons");
    } catch (err) {
      errorNotif("Error adding coupon");
    }
  };

  const handleUpdateCoupon = async (id, payload) => {
    try {
      const session = getAuthSession();
      const response = await fetch(`/api/coupons/${id}`, {
        method: "PUT",
        headers: {
          "Content-Type": "application/json",
          Authorization: `Bearer ${session?.token}`,
        },
        body: JSON.stringify(payload),
      });

      if (!response.ok) {
        const error = await response.json();
        errorNotif(error?.message || "Failed to update coupon");
        return;
      }

      const updatedCoupon = await response.json();
      setCoupons((current) =>
        current.map((coupon) => (coupon.id === id ? updatedCoupon : coupon))
      );
      success("Coupon updated successfully!");
      bumpSyncState("coupons");
    } catch (err) {
      errorNotif("Error updating coupon");
    }
  };

  const handleDeleteCoupon = async (id) => {
    setModalConfig({
      open: true,
      title: "Delete coupon",
      message: "This coupon will be permanently deleted.",
      confirmLabel: "Delete Coupon",
      onConfirm: async () => {
        try {
          const session = getAuthSession();
          const response = await fetch(`/api/coupons/${id}`, {
            method: "DELETE",
            headers: {
              Authorization: `Bearer ${session?.token}`,
            },
          });

          if (!response.ok) {
            const error = await response.json();
            errorNotif(error?.message || "Failed to delete coupon");
            setModalConfig((current) => ({ ...current, open: false }));
            return;
          }

          setCoupons((current) => current.filter((coupon) => coupon.id !== id));
          setModalConfig((current) => ({ ...current, open: false }));
          success("Coupon deleted successfully!");
          bumpSyncState("coupons");
        } catch (err) {
          errorNotif("Error deleting coupon");
          setModalConfig((current) => ({ ...current, open: false }));
        }
      },
    });
  };

  const closeModal = () => {
    setModalConfig((current) => ({ ...current, open: false }));
  };

  const handleSectionChange = section => {
    if (section === activeSection || menuDraft.busy) return;
    if (activeSection === "menu" && menuDraft.dirty) {
      setModalConfig({ open: true, title: "Leave Menu Management?", message: "Your menu draft has not been saved. Discard it to leave this page?",
        confirmLabel: "Discard changes", onConfirm: () => { setActiveSection(section); closeModal(); } });
    } else setActiveSection(section);
  };

  return (
    <div className={`admin-layout ${activeSection === "menu" ? "admin-menu-view" : ""}`}>
      <AdminSidebar activeSection={activeSection} onSectionChange={handleSectionChange} disabled={menuDraft.busy} />

      <main className="admin-main">
        <AdminTopbar
          onLogout={handleLogout}
          adminName={session?.user?.name || "Admin"}
          syncLabel={activeSection === "menu" ? null : syncLabel}
          title={activeSection === "menu" ? "Menu Management" : undefined}
          subtitle={activeSection === "menu" ? "Manage your items, prices, and availability." : undefined}
          disabled={menuDraft.busy}
        />

        {activeSection === "dashboard" && (
          <>
            <AnalyticsCards stats={dashboardStats} topItems={topItems} />

            <div className="admin-content-grid">
              <RevenueChart 
                weeklyRevenue={weeklyRevenue}
                orders={allOrders.length > 0 ? allOrders : filteredAdminOrders}
                dateRange={filters}
              />
              <PopularItems topItems={topItems} />
            </div>

            <OrdersPanel
              title="Recent Client Orders"
              orders={[...allOrders]
                .sort((a, b) => new Date(b.createdAt) - new Date(a.createdAt))
                .slice(0, 6)}
              statusOptions={statusOptions}
              filters={filters}
              onFilterChange={handleOrderFilter}
              showFilters={false}
            />
          </>
        )}

        {activeSection === "orders" && (
          <OrdersPanel
            orders={filteredAdminOrders}
            statusOptions={statusOptions}
            filters={filters}
            onFilterChange={handleOrderFilter}
          />
        )}

        {activeSection === "manage-orders" && (
          <OrdersPanel
            title="All Orders - Admin Management"
            orders={filteredAdminOrders}
            statusOptions={statusOptions}
            filters={filters}
            onFilterChange={handleOrderFilter}
            isAdmin={true}
            onUpdateStatus={handleUpdateOrderStatus}
            onDeleteOrder={handleDeleteOrder}
          />
        )}

        {activeSection === "menu" && (
          <MenuManagement
            categories={menuCategories}
            categoryLoading={menuCategoryLoading}
            categoryError={menuCategoryError}
            onRetryCategories={() => { setMenuCategoryLoading(true); loadMenuCategories(); }}
            revision={menuRevision}
            onDraftChange={setMenuDraft}
            onAddMenuItem={handleAddMenuItem}
            onUpdateMenuItem={handleUpdateMenuItem}
            onDeleteMenuItem={handleDeleteMenuItem}
            onSetAvailability={handleSetMenuAvailability}
          />
        )}

        {activeSection === "categories" && (
          <CategoryManagement
            categories={categories}
            menuItems={menuItems}
            onAddCategory={handleAddCategory}
            onUpdateCategory={handleUpdateCategory}
            onDeleteCategory={handleDeleteCategory}
          />
        )}

        {activeSection === "coupons" && (
          <CouponManagement
            coupons={coupons}
            onAddCoupon={handleAddCoupon}
            onUpdateCoupon={handleUpdateCoupon}
            onDeleteCoupon={handleDeleteCoupon}
          />
        )}
      </main>

      <ConfirmModal
        open={logoutConfirm}
        title="Confirm Logout"
        message={menuDraft.dirty ? "Your menu draft has not been saved. Logging out will discard it. Continue?" : "Are you sure you want to logout?"}
        confirmLabel="Logout"
        onCancel={() => setLogoutConfirm(false)}
        onConfirm={confirmLogout}
      />
      <ConfirmModal
        open={modalConfig.open}
        title={modalConfig.title}
        message={modalConfig.message}
        confirmLabel={modalConfig.confirmLabel}
        cancelLabel={modalConfig.cancelLabel}
        hideCancel={modalConfig.hideCancel}
        confirmVariant={modalConfig.confirmVariant}
        onCancel={closeModal}
        onConfirm={modalConfig.onConfirm}
      />
    </div>
  );
}
