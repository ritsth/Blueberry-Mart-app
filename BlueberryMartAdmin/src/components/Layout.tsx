import { NavLink, Outlet, useNavigate } from 'react-router-dom';
import { logout } from '../auth';

export default function Layout() {
  const navigate = useNavigate();
  function signOut() {
    logout();
    navigate('/login', { replace: true });
  }

  return (
    <div className="shell">
      <aside className="sidebar">
        <div className="brand">🫐 Blueberry Mart<span>Admin</span></div>
        <nav>
          <NavLink to="/users" className={({ isActive }) => (isActive ? 'active' : '')}>Users</NavLink>
          <NavLink to="/reviews" className={({ isActive }) => (isActive ? 'active' : '')}>Reviews</NavLink>
          <NavLink to="/settings" className={({ isActive }) => (isActive ? 'active' : '')}>Settings</NavLink>
        </nav>
        <button className="signout" onClick={signOut}>Sign out</button>
      </aside>
      <main className="content">
        <Outlet />
      </main>
    </div>
  );
}
