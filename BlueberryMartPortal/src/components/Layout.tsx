import { NavLink, Outlet, useNavigate } from 'react-router-dom';
import { getRole, isAdmin, logout } from '../auth';

export default function Layout() {
  const navigate = useNavigate();
  const role = getRole();
  function signOut() {
    logout();
    navigate('/login', { replace: true });
  }

  return (
    <div className="shell">
      <aside className="sidebar">
        <div className="brand">🫐 Blueberry Mart<span>Portal</span></div>
        <div className={`role-tag role-${role}`}>{role}</div>
        <nav>
          <NavLink to="/" end className={({ isActive }) => (isActive ? 'active' : '')}>Dashboard</NavLink>
          <div className="nav-section">Operations</div>
          <NavLink to="/items" className={({ isActive }) => (isActive ? 'active' : '')}>Items</NavLink>
          <NavLink to="/orders" className={({ isActive }) => (isActive ? 'active' : '')}>Orders</NavLink>
          {(role === 'manager' || role === 'admin') && (
            <NavLink to="/reports" className={({ isActive }) => (isActive ? 'active' : '')}>Reports</NavLink>
          )}
          {isAdmin() && (
            <>
              <div className="nav-section">Admin</div>
              <NavLink to="/users" className={({ isActive }) => (isActive ? 'active' : '')}>Users</NavLink>
              <NavLink to="/reviews" className={({ isActive }) => (isActive ? 'active' : '')}>Reviews</NavLink>
              <NavLink to="/settings" className={({ isActive }) => (isActive ? 'active' : '')}>Settings</NavLink>
            </>
          )}
        </nav>
        <button className="signout" onClick={signOut}>Sign out</button>
      </aside>
      <main className="content">
        <Outlet />
      </main>
    </div>
  );
}
