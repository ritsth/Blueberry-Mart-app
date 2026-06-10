import { Navigate, Route, Routes } from 'react-router-dom';
import { getRole, isAdmin, isAuthed } from './auth';
import Layout from './components/Layout';
import Login from './pages/Login';
import Dashboard from './pages/Dashboard';
import ItemsPage from './pages/ItemsPage';
import OrdersPage from './pages/OrdersPage';
import ReportsPage from './pages/ReportsPage';
import UsersPage from './pages/UsersPage';
import ReviewsPage from './pages/ReviewsPage';
import SettingsPage from './pages/SettingsPage';

function RequireAuth({ children }: { children: React.ReactNode }) {
  return isAuthed() ? <>{children}</> : <Navigate to="/login" replace />;
}

// Admin-only pages: anyone else signed in is sent back to the dashboard.
function RequireAdmin({ children }: { children: React.ReactNode }) {
  return isAdmin() ? <>{children}</> : <Navigate to="/" replace />;
}

// Manager or admin (e.g. reports): staff are sent back to the dashboard.
function RequireManager({ children }: { children: React.ReactNode }) {
  const role = getRole();
  return role === 'manager' || role === 'admin' ? <>{children}</> : <Navigate to="/" replace />;
}

export default function App() {
  return (
    <Routes>
      <Route path="/login" element={<Login />} />
      <Route
        path="/"
        element={
          <RequireAuth>
            <Layout />
          </RequireAuth>
        }
      >
        <Route index element={<Dashboard />} />
        <Route path="items" element={<ItemsPage />} />
        <Route path="orders" element={<OrdersPage />} />
        <Route path="reports" element={<RequireManager><ReportsPage /></RequireManager>} />
        <Route path="users" element={<RequireAdmin><UsersPage /></RequireAdmin>} />
        <Route path="reviews" element={<RequireAdmin><ReviewsPage /></RequireAdmin>} />
        <Route path="settings" element={<RequireAdmin><SettingsPage /></RequireAdmin>} />
      </Route>
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  );
}
