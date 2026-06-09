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
        <nav>
          <NavLink to="/" end className={({ isActive }) => (isActive ? 'active' : '')}>Dashboard</NavLink>
          {isAdmin() && (
            <>
              <div className="nav-section">Admin</div>
              <NavLink to="/users" className={({ isActive }) => (isActive ? 'active' : '')}>Users</NavLink>
              <NavLink to="/reviews" className={({ isActive }) => (isActive ? 'active' : '')}>Reviews</NavLink>
              <NavLink to="/settings" className={({ isActive }) => (isActive ? 'active' : '')}>Settings</NavLink>
            </>
          )}
        </nav>
        <div className={`role-tag role-${role}`}>{role}</div>
        <button className="signout" onClick={signOut}>Sign out</button>
      </aside>
      <main className="content">
        <Outlet />
      </main>
    </div>
  );
}
