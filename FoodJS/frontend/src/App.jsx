import { lazy, Suspense } from "react";
import { BrowserRouter, Navigate, Routes, Route } from "react-router-dom";
import { NotificationProvider } from "./context/NotificationContext";
import { NotificationContainer } from "./components/NotificationContainer";
import { getUserRole, isAuthenticated } from "./lib/auth";

const Landing = lazy(() => import("./pages/Landing"));
const Login = lazy(() => import("./pages/Login"));
const Register = lazy(() => import("./pages/Register"));
const AuthCallback = lazy(() => import("./pages/AuthCallback"));
const Dashboard = lazy(() => import("./pages/Dashboard"));
const Menu = lazy(() => import("./pages/Menu"));
const ProductDetails = lazy(() => import("./pages/ProductDetails"));
const Cart = lazy(() => import("./pages/Cart"));
const Checkout = lazy(() => import("./pages/Checkout"));
const OrderTracking = lazy(() => import("./pages/OrderTracking"));
const OrderConfirmation = lazy(() => import("./pages/OrderConfirmation"));
const OrderHistory = lazy(() => import("./pages/OrderHistory"));
const Profile = lazy(() => import("./pages/Profile"));

function AdminRoute({ children }) {
  if (!isAuthenticated()) {
    return <Navigate to="/login" replace />;
  }

  if (getUserRole() !== "admin") {
    return <Navigate to="/menu" replace />;
  }

  return children;
}

function CustomerRoute({ children }) {
  if (!isAuthenticated()) {
    return <Navigate to="/login" replace />;
  }

  if (getUserRole() === "admin") {
    return <Navigate to="/dashboard" replace />;
  }

  return children;
}

export default function App() {
  return (
    <NotificationProvider>
      <BrowserRouter>
        <NotificationContainer />
      <Suspense fallback={<div className="app-loading" role="status">Loading FoodJS...</div>}>
      <Routes>
        <Route path="/" element={<Landing />} />
        <Route path="/login" element={<Login />} />
        <Route path="/register" element={<Register />} />
        <Route path="/auth/callback" element={<AuthCallback />} />
        <Route
          path="/dashboard"
          element={
            <AdminRoute>
              <Dashboard />
            </AdminRoute>
          }
        />
        <Route
          path="/menu"
          element={
            <CustomerRoute>
              <Menu />
            </CustomerRoute>
          }
        />
        <Route
          path="/cart"
          element={
            <CustomerRoute>
              <Cart />
            </CustomerRoute>
          }
        />
        <Route
          path="/checkout"
          element={
            <CustomerRoute>
              <Checkout />
            </CustomerRoute>
          }
        />
        <Route
          path="/product/:id"
          element={
            <CustomerRoute>
              <ProductDetails />
            </CustomerRoute>
          }
        />
        <Route
          path="/order-tracking/:orderId"
          element={
            <CustomerRoute>
              <OrderTracking />
            </CustomerRoute>
          }
        />
        <Route
          path="/order-confirmation/:orderId"
          element={
            <CustomerRoute>
              <OrderConfirmation />
            </CustomerRoute>
          }
        />
        <Route
          path="/order-history"
          element={
            <CustomerRoute>
              <OrderHistory />
            </CustomerRoute>
          }
        />
        <Route
          path="/profile"
          element={
            <CustomerRoute>
              <Profile />
            </CustomerRoute>
          }
        />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
      </Suspense>
      </BrowserRouter>
    </NotificationProvider>
  );
}
