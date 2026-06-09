import { Navigate, Route, Routes } from 'react-router-dom';
import { isAuthed } from './auth';
import Layout from './components/Layout';
import Login from './pages/Login';
import UsersPage from './pages/UsersPage';
import ReviewsPage from './pages/ReviewsPage';
import SettingsPage from './pages/SettingsPage';

function RequireAdmin({ children }: { children: React.ReactNode }) {
  return isAuthed() ? <>{children}</> : <Navigate to="/login" replace />;
}

export default function App() {
  return (
    <Routes>
      <Route path="/login" element={<Login />} />
      <Route
        path="/"
        element={
          <RequireAdmin>
            <Layout />
          </RequireAdmin>
        }
      >
        <Route index element={<Navigate to="/users" replace />} />
        <Route path="users" element={<UsersPage />} />
        <Route path="reviews" element={<ReviewsPage />} />
        <Route path="settings" element={<SettingsPage />} />
      </Route>
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  );
}
