import { clsx } from 'clsx';
import { Bell, Globe, Heart, LayoutDashboard, LifeBuoy, LogOut, Menu, MessageSquare, Plane, Shield, User } from 'lucide-react';
import { Component, Suspense, useEffect, useRef, useState, type ErrorInfo, type ReactNode } from 'react';
import { Link, NavLink, Navigate, Outlet, useLocation, useNavigate } from 'react-router';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '@/api/client';
import { AssistantWidget } from '@/features/ai/AssistantWidget';
import { hasRole, useAuthStore } from '@/lib/auth-store';
import { useFeatureFlags } from '@/lib/hooks';
import { useRealtime } from '@/lib/realtime';
import { PageLoader, Toaster } from '@/components/ui';

export function Logo() {
  return (
    <Link to="/" className="flex items-center gap-2" aria-label="StaySphere home">
      <img src="/favicon.svg" alt="" className="h-8 w-8" />
      <span className="text-xl font-extrabold tracking-tight text-brand-700">staysphere</span>
    </Link>
  );
}

function UserMenu() {
  const user = useAuthStore((s) => s.user);
  const clear = useAuthStore((s) => s.clear);
  const navigate = useNavigate();
  const qc = useQueryClient();
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);
  const unread = useQuery({ queryKey: ['notifications', 'unread'], queryFn: () => api<number>('/notifications/unread-count'), enabled: !!user, refetchInterval: 60_000 });

  useEffect(() => {
    const close = (e: MouseEvent) => ref.current && !ref.current.contains(e.target as Node) && setOpen(false);
    document.addEventListener('mousedown', close);
    return () => document.removeEventListener('mousedown', close);
  }, []);

  const logout = async () => {
    await api('/auth/logout', { method: 'POST', headers: { 'X-Requested-With': 'staysphere' } }).catch(() => undefined);
    clear();
    qc.clear();
    navigate('/');
  };

  const item = 'flex items-center gap-3 px-4 py-2.5 text-sm hover:bg-slate-50';
  return (
    <div className="relative" ref={ref}>
      <button onClick={() => setOpen((o) => !o)} aria-expanded={open} aria-haspopup="menu" aria-label="Account menu"
        className="relative flex items-center gap-3 rounded-full border border-slate-300 py-1.5 pr-1.5 pl-3 transition hover:shadow-md">
        <Menu className="h-4 w-4" />
        {user?.avatarUrl ? <img src={user.avatarUrl} alt="" className="h-8 w-8 rounded-full object-cover" />
          : <span className="flex h-8 w-8 items-center justify-center rounded-full bg-slate-700 text-sm font-semibold text-white">{user ? user.displayName[0] : <User className="h-4 w-4" />}</span>}
        {!!unread.data && <span className="absolute -top-1 -right-1 flex h-5 min-w-5 items-center justify-center rounded-full bg-coral-500 px-1 text-xs font-bold text-white">{unread.data}</span>}
      </button>
      {open && (
        <div role="menu" className="absolute right-0 z-50 mt-2 w-64 overflow-hidden rounded-2xl border border-slate-200 bg-white py-2 shadow-xl" onClick={() => setOpen(false)}>
          {user ? (
            <>
              <div className="border-b border-slate-100 px-4 pb-2 text-sm"><p className="font-semibold">{user.displayName}</p><p className="truncate text-slate-500">{user.email}</p></div>
              <Link to="/trips" className={item} role="menuitem"><Plane className="h-4 w-4" /> Trips</Link>
              <Link to="/favorites" className={item} role="menuitem"><Heart className="h-4 w-4" /> Wishlists</Link>
              <Link to="/messages" className={item} role="menuitem"><MessageSquare className="h-4 w-4" /> Messages</Link>
              <Link to="/notifications" className={item} role="menuitem"><Bell className="h-4 w-4" /> Notifications {!!unread.data && <span className="ml-auto rounded-full bg-coral-500 px-2 text-xs text-white">{unread.data}</span>}</Link>
              <div className="my-2 border-t border-slate-100" />
              {hasRole(user, 'Host') ? <Link to="/host" className={item} role="menuitem"><LayoutDashboard className="h-4 w-4" /> Host dashboard</Link>
                : <Link to="/become-a-host" className={item} role="menuitem"><Globe className="h-4 w-4" /> Host your home</Link>}
              {hasRole(user, 'Admin') && <Link to="/admin" className={item} role="menuitem"><Shield className="h-4 w-4" /> Admin console</Link>}
              <Link to="/support" className={item} role="menuitem"><LifeBuoy className="h-4 w-4" /> Help & support</Link>
              <Link to="/profile" className={item} role="menuitem"><User className="h-4 w-4" /> Account</Link>
              <button onClick={logout} className={clsx(item, 'w-full')} role="menuitem"><LogOut className="h-4 w-4" /> Log out</button>
            </>
          ) : (
            <>
              <Link to="/login" className={clsx(item, 'font-semibold')} role="menuitem">Log in</Link>
              <Link to="/register" className={item} role="menuitem">Sign up</Link>
              <div className="my-2 border-t border-slate-100" />
              <Link to="/become-a-host" className={item} role="menuitem">Host your home</Link>
              <Link to="/support" className={item} role="menuitem">Help centre</Link>
            </>
          )}
        </div>
      )}
    </div>
  );
}

export function Navbar() {
  const user = useAuthStore((s) => s.user);
  const flags = useFeatureFlags();
  return (
    <header className="sticky top-0 z-[500] border-b border-slate-200 bg-white/95 backdrop-blur">
      <div className="container-page flex h-18 items-center justify-between gap-4 py-3">
        <Logo />
        <nav className="hidden items-center gap-1 md:flex" aria-label="Primary">
          <NavLink to="/search" className={({ isActive }) => clsx('rounded-full px-4 py-2 text-sm font-medium hover:bg-slate-100', isActive && 'text-slate-900')}>Stays</NavLink>
          {flags.data?.Experiences ? <NavLink to="/search" className="rounded-full px-4 py-2 text-sm text-slate-500 hover:bg-slate-100">Experiences</NavLink>
            : <span className="cursor-default rounded-full px-4 py-2 text-sm text-slate-400" title="Coming soon">Experiences <span className="text-xs">soon</span></span>}
        </nav>
        <div className="flex items-center gap-2">
          <Link to={hasRole(user, 'Host') ? '/host' : '/become-a-host'} className="hidden rounded-full px-4 py-2 text-sm font-semibold hover:bg-slate-100 sm:block">
            {hasRole(user, 'Host') ? 'Switch to hosting' : 'Host your home'}
          </Link>
          <UserMenu />
        </div>
      </div>
    </header>
  );
}

export function Footer() {
  return (
    <footer className="mt-20 border-t border-slate-200 bg-slate-50">
      <div className="container-page grid gap-8 py-12 text-sm sm:grid-cols-3">
        <div>
          <p className="mb-3 font-semibold">Support</p>
          <ul className="space-y-2 text-slate-600"><li><Link to="/support">Help centre</Link></li><li><Link to="/support">Cancellation options</Link></li><li><Link to="/support">Report a concern</Link></li></ul>
        </div>
        <div>
          <p className="mb-3 font-semibold">Hosting</p>
          <ul className="space-y-2 text-slate-600"><li><Link to="/become-a-host">Host your home</Link></li><li><Link to="/host">Host resources</Link></li><li><Link to="/host/earnings">Earnings</Link></li></ul>
        </div>
        <div>
          <p className="mb-3 font-semibold">StaySphere</p>
          <ul className="space-y-2 text-slate-600"><li>Portfolio project — not a real booking service</li><li>Map data © OpenStreetMap contributors</li><li>Weather by Open-Meteo</li></ul>
        </div>
      </div>
      <div className="border-t border-slate-200 py-6 text-center text-xs text-slate-500">© {new Date().getFullYear()} StaySphere · Demo data only · No real payments are processed</div>
    </footer>
  );
}

export function RootLayout() {
  useRealtime();
  const location = useLocation();
  // Block body: newer browsers make scroll methods return a Promise, which React would treat as a cleanup function.
  useEffect(() => { window.scrollTo(0, 0); }, [location.pathname]);
  return (
    <div className="flex min-h-screen flex-col">
      <a href="#main" className="sr-only focus:not-sr-only focus:absolute focus:top-2 focus:left-2 focus:z-[999] focus:rounded focus:bg-white focus:p-2">Skip to content</a>
      <Navbar />
      <main id="main" className="flex-1">
        <RouteErrorBoundary>
          <Suspense fallback={<PageLoader />}>
            <Outlet />
          </Suspense>
        </RouteErrorBoundary>
      </main>
      <Footer />
      <AssistantWidget />
      <Toaster />
    </div>
  );
}

function SideNav({ links }: { links: [string, string][] }) {
  return (
    <nav className="mb-6 flex gap-1 overflow-x-auto border-b border-slate-200 pb-px" aria-label="Section">
      {links.map(([to, label]) => (
        <NavLink key={to} to={to} end className={({ isActive }) => clsx('border-b-2 px-3 py-2.5 text-sm font-medium whitespace-nowrap', isActive ? 'border-slate-900 text-slate-900' : 'border-transparent text-slate-500 hover:text-slate-800')}>
          {label}
        </NavLink>
      ))}
    </nav>
  );
}

export function HostLayout() {
  return (
    <RequireRole role="Host">
      <div className="container-page py-8">
        <SideNav links={[['/host', 'Today'], ['/host/properties', 'Listings'], ['/host/calendar', 'Calendar'], ['/host/reservations', 'Reservations'], ['/host/earnings', 'Earnings'], ['/host/reviews', 'Reviews'], ['/host/analytics', 'Analytics']]} />
        <Outlet />
      </div>
    </RequireRole>
  );
}

export function AdminLayout() {
  return (
    <RequireRole role="Admin">
      <div className="container-page py-8">
        <SideNav links={[['/admin', 'Overview'], ['/admin/users', 'Users'], ['/admin/properties', 'Listings'], ['/admin/reservations', 'Reservations'], ['/admin/payments', 'Payments'], ['/admin/reviews', 'Reviews'], ['/admin/reports', 'Reports & fraud'], ['/admin/audit', 'Audit log']]} />
        <Outlet />
      </div>
    </RequireRole>
  );
}

/** UX guard only — the API enforces every permission independently. */
export function RequireAuth({ children }: { children: ReactNode }) {
  const { user, initialized } = useAuthStore();
  const location = useLocation();
  if (!initialized) return <PageLoader />;
  if (!user) return <Navigate to={`/login?returnTo=${encodeURIComponent(location.pathname + location.search)}`} replace />;
  return <>{children}</>;
}

export function RequireRole({ role, children }: { role: 'Host' | 'Admin' | 'Support'; children: ReactNode }) {
  const user = useAuthStore((s) => s.user);
  return (
    <RequireAuth>
      {hasRole(user, role) || hasRole(user, 'Admin') ? children : role === 'Host' ? <Navigate to="/become-a-host" replace /> : <Forbidden />}
    </RequireAuth>
  );
}

function Forbidden() {
  return (
    <div className="container-page py-24 text-center">
      <p className="text-6xl font-black text-slate-200">403</p>
      <h1 className="mt-4 text-2xl font-bold">You don't have access to this page</h1>
      <Link to="/" className="btn-primary mt-6">Go home</Link>
    </div>
  );
}

export class RouteErrorBoundary extends Component<{ children: ReactNode }, { error: Error | null }> {
  state = { error: null as Error | null };

  static getDerivedStateFromError(error: Error) {
    return { error };
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    console.error('UI error', error, info.componentStack);
  }

  render() {
    if (!this.state.error) return this.props.children;
    return (
      <div className="container-page py-24 text-center" role="alert">
        <h1 className="text-2xl font-bold">Something went wrong</h1>
        <p className="mt-2 text-slate-600">An unexpected error occurred while showing this page.</p>
        <button className="btn-primary mt-6" onClick={() => { this.setState({ error: null }); location.reload(); }}>Reload</button>
      </div>
    );
  }
}
