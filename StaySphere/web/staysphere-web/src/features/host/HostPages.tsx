import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { clsx } from 'clsx';
import { BedDouble, CalendarDays, Coins, Home, Percent, Plus, Star, TrendingUp } from 'lucide-react';
import { useState } from 'react';
import { Link, useNavigate } from 'react-router';
import { Bar, BarChart, CartesianGrid, Line, LineChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';
import { api, toQuery } from '@/api/client';
import type { AuthResponse, EarningsSummary, HostDashboard, HostPropertyListItem, Paged, ReservationDto, ReviewDto } from '@/api/types';
import { DataTable, EmptyState, ErrorState, Modal, PageHeader, PageLoader, Skeleton, StatCard, StatusBadge, SafeImg } from '@/components/ui';
import { hasRole, useAuthStore } from '@/lib/auth-store';
import { money, prettyDate, stayLabel } from '@/lib/format';
import { toast } from '@/lib/toast';
import { format, parseISO } from 'date-fns';

export function BecomeHostPage() {
  const user = useAuthStore((s) => s.user);
  const setSession = useAuthStore((s) => s.setSession);
  const navigate = useNavigate();
  const become = useMutation({
    mutationFn: () => api<AuthResponse>('/me/become-host', { method: 'POST' }),
    onSuccess: (auth) => { setSession(auth); toast.success("You're a host now! Let's create your first listing."); navigate('/host/properties/new'); },
    onError: (e) => toast.error((e as Error).message),
  });
  return (
    <div className="container-page grid items-center gap-12 py-16 lg:grid-cols-2">
      <div>
        <h1 className="text-4xl font-extrabold tracking-tight sm:text-5xl">Host your home on StaySphere</h1>
        <p className="mt-4 text-lg text-slate-600">Earn extra income by sharing your space. Set your own prices and availability, and get paid securely.</p>
        <ul className="mt-6 space-y-2 text-slate-700"><li>✓ A guided 12-step listing wizard with autosave</li><li>✓ Seasonal & weekend pricing, discounts and blocked dates</li><li>✓ Real-time messaging and booking notifications</li><li>✓ Earnings tracked in an immutable ledger</li></ul>
        <div className="mt-8">
          {!user ? <Link to="/register" className="btn-accent !px-6 !py-3 text-base">Sign up to start hosting</Link>
            : hasRole(user, 'Host') ? <Link to="/host/properties/new" className="btn-accent !px-6 !py-3 text-base">Create a listing</Link>
            : <button className="btn-accent !px-6 !py-3 text-base" disabled={become.isPending} onClick={() => become.mutate()}>Become a host</button>}
        </div>
      </div>
      <SafeImg src="https://picsum.photos/seed/staysphere-host/900/700" alt="" className="aspect-[9/7] w-full rounded-3xl object-cover shadow-card" />
    </div>
  );
}

const month = (d: string) => format(parseISO(d), 'MMM');

export function HostDashboardPage({ analytics }: { analytics?: boolean }) {
  const dash = useQuery({ queryKey: ['host', 'dashboard'], queryFn: () => api<HostDashboard>('/host/dashboard') });
  const upcoming = useQuery({ queryKey: ['host', 'reservations', 'upcoming'], queryFn: () => api<Paged<ReservationDto>>('/host/reservations?scope=upcoming&pageSize=5') });
  if (dash.isLoading) return <div className="grid gap-4 sm:grid-cols-3">{Array.from({ length: 6 }, (_, i) => <Skeleton key={i} className="h-28" />)}</div>;
  if (dash.isError) return <ErrorState error={dash.error} onRetry={() => dash.refetch()} />;
  const d = dash.data!;
  const user = useAuthStore.getState().user;
  return (
    <>
      <PageHeader title={analytics ? 'Analytics' : `Welcome back, ${user?.displayName.split(' ')[0]}`} subtitle={analytics ? 'Performance across your listings' : "Here's what's happening with your listings."} actions={<Link to="/host/properties/new" className="btn-primary"><Plus className="h-4 w-4" /> New listing</Link>} />
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-6">
        <StatCard label="Total bookings" value={d.totalBookings} icon={<CalendarDays className="h-4 w-4" />} />
        <StatCard label="Net revenue" value={money(d.revenue, d.currency, true)} icon={<Coins className="h-4 w-4" />} hint="From the earnings ledger" />
        <StatCard label="Upcoming" value={d.upcomingReservations} icon={<BedDouble className="h-4 w-4" />} />
        <StatCard label="Occupancy (90d)" value={`${Math.round(d.occupancyRate * 100)}%`} icon={<Percent className="h-4 w-4" />} />
        <StatCard label="Avg nightly" value={money(d.averageNightlyPrice, d.currency, true)} icon={<TrendingUp className="h-4 w-4" />} />
        <StatCard label="Rating" value={d.rating ? d.rating.toFixed(2) : '—'} icon={<Star className="h-4 w-4" />} />
      </div>
      <div className="mt-8 grid gap-6 lg:grid-cols-2">
        <div className="card p-5"><h2 className="mb-4 font-semibold">Revenue (last 12 months)</h2>
          <div className="h-64"><ResponsiveContainer><BarChart data={d.revenueByMonth.map((p) => ({ m: month(p.date), v: p.value }))}><CartesianGrid strokeDasharray="3 3" vertical={false} /><XAxis dataKey="m" fontSize={12} /><YAxis fontSize={12} /><Tooltip formatter={(v) => money(Number(v), d.currency)} /><Bar dataKey="v" fill="#0f766e" radius={[6, 6, 0, 0]} /></BarChart></ResponsiveContainer></div></div>
        <div className="card p-5"><h2 className="mb-4 font-semibold">Bookings by check-in month</h2>
          <div className="h-64"><ResponsiveContainer><LineChart data={d.bookingsByMonth.map((p) => ({ m: month(p.date), v: p.value }))}><CartesianGrid strokeDasharray="3 3" vertical={false} /><XAxis dataKey="m" fontSize={12} /><YAxis allowDecimals={false} fontSize={12} /><Tooltip /><Line dataKey="v" stroke="#f43f5e" strokeWidth={2.5} dot={false} /></LineChart></ResponsiveContainer></div></div>
      </div>
      <div className="mt-8 grid gap-6 lg:grid-cols-2">
        <section><h2 className="mb-3 font-semibold">Property performance</h2>
          <DataTable rows={d.properties} rowKey={(p) => p.propertyId} empty="Create a listing to see performance."
            columns={[{ header: 'Listing', cell: (p) => <Link to={`/property/${p.propertyId}`} className="font-medium hover:underline">{p.title || 'Untitled'}</Link> },
              { header: 'Bookings', cell: (p) => p.bookings }, { header: 'Revenue', cell: (p) => money(p.revenue, d.currency, true) },
              { header: 'Occupancy', cell: (p) => `${Math.round(p.occupancyRate * 100)}%` }, { header: 'Rating', cell: (p) => (p.rating ? p.rating.toFixed(2) : '—') }]} />
        </section>
        {!analytics && (
          <section><h2 className="mb-3 font-semibold">Upcoming reservations</h2>
            {upcoming.data && <DataTable rows={upcoming.data.items} rowKey={(r) => r.id} empty="No upcoming reservations."
              columns={[{ header: 'Guest', cell: (r) => r.guestName }, { header: 'Listing', cell: (r) => <span className="line-clamp-1">{r.propertyTitle}</span> }, { header: 'Dates', cell: (r) => stayLabel(r.checkIn, r.checkOut) }, { header: 'Status', cell: (r) => <StatusBadge status={r.status} /> }]} />}
          </section>
        )}
      </div>
    </>
  );
}

export function HostPropertiesPage() {
  const listings = useQuery({ queryKey: ['host', 'properties'], queryFn: () => api<HostPropertyListItem[]>('/host/properties') });
  return (
    <>
      <PageHeader title="Your listings" actions={<Link to="/host/properties/new" className="btn-primary"><Plus className="h-4 w-4" /> Create listing</Link>} />
      {listings.isLoading ? <Skeleton className="h-48" /> : listings.isError ? <ErrorState error={listings.error} />
        : listings.data!.length === 0 ? <EmptyState icon={<Home className="h-7 w-7" />} title="Create your first listing." description="It takes about 10 minutes — your progress is saved automatically." action={<Link to="/host/properties/new" className="btn-primary">Get started</Link>} />
        : (
          <DataTable rows={listings.data!} rowKey={(p) => p.id}
            columns={[
              { header: 'Listing', cell: (p) => <div className="flex items-center gap-3">{p.coverImageUrl ? <SafeImg src={p.coverImageUrl} alt="" className="h-12 w-16 rounded-lg object-cover" /> : <span className="h-12 w-16 rounded-lg bg-slate-100" />}<div><p className="font-medium">{p.title}</p><p className="text-xs text-slate-500">{p.city || 'No location yet'}</p></div></div> },
              { header: 'Status', cell: (p) => <StatusBadge status={p.status} /> },
              { header: 'Complete', cell: (p) => <div className="flex items-center gap-2"><div className="h-1.5 w-20 rounded bg-slate-200"><div className="h-1.5 rounded bg-brand-600" style={{ width: `${p.completionPercent}%` }} /></div><span className="text-xs">{p.completionPercent}%</span></div> },
              { header: 'Price', cell: (p) => money(p.basePrice, p.currency, true) },
              { header: 'Upcoming', cell: (p) => p.upcomingReservations },
              { header: 'Rating', cell: (p) => (p.reviewCount ? `${p.ratingAverage.toFixed(2)} (${p.reviewCount})` : '—') },
              { header: '', cell: (p) => <div className="flex gap-2"><Link to={`/host/properties/${p.id}/edit`} className="btn-secondary !py-1.5 text-xs">Edit</Link><Link to={`/property/${p.id}`} className="btn-ghost !py-1.5 text-xs">View</Link></div> },
            ]} />
        )}
    </>
  );
}

export function HostReservationsPage() {
  const [scope, setScope] = useState('upcoming');
  const qc = useQueryClient();
  const list = useQuery({ queryKey: ['host', 'reservations', scope], queryFn: () => api<Paged<ReservationDto>>(`/host/reservations${toQuery({ scope, pageSize: 50 })}`) });
  const navigate = useNavigate();
  const message = useMutation({ mutationFn: (r: ReservationDto) => api<{ id: string }>('/conversations', { method: 'POST', body: { propertyId: r.propertyId, reservationId: r.id, message: `Hi ${r.guestName}, looking forward to hosting you!` } }), onSuccess: (c) => navigate(`/messages/${c.id}`) });
  const cancel = useMutation({ mutationFn: (id: string) => api(`/reservations/${id}/cancel`, { method: 'POST', idempotencyKey: crypto.randomUUID(), body: { reason: 'Cancelled by host' } }), onSuccess: () => { toast.success('Cancelled — the guest receives a full refund.'); qc.invalidateQueries({ queryKey: ['host'] }); } });
  return (
    <>
      <PageHeader title="Reservations" />
      <div className="mb-4 flex gap-2">{['upcoming', 'past', 'cancelled', 'all'].map((s) => <button key={s} className={clsx('chip capitalize', scope === s && 'chip-active')} onClick={() => setScope(s)}>{s}</button>)}</div>
      {list.isLoading ? <Skeleton className="h-48" /> : list.isError ? <ErrorState error={list.error} /> : (
        <DataTable rows={list.data!.items} rowKey={(r) => r.id} empty="No reservations here yet."
          columns={[{ header: 'Guest', cell: (r) => r.guestName }, { header: 'Listing', cell: (r) => <span className="line-clamp-1">{r.propertyTitle}</span> },
            { header: 'Dates', cell: (r) => stayLabel(r.checkIn, r.checkOut) }, { header: 'Guests', cell: (r) => r.guests }, { header: 'Total', cell: (r) => money(r.totalAmount, r.currency) },
            { header: 'Status', cell: (r) => <StatusBadge status={r.status} /> },
            { header: '', cell: (r) => <div className="flex gap-2"><button className="btn-secondary !py-1.5 text-xs" onClick={() => message.mutate(r)}>Message</button>{r.canCancel && r.status === 'Confirmed' && <button className="btn-ghost !py-1.5 text-xs text-red-700" onClick={() => confirm('Cancel this reservation? The guest gets a full refund.') && cancel.mutate(r.id)}>Cancel</button>}</div> }]} />
      )}
    </>
  );
}

export function HostEarningsPage() {
  const earnings = useQuery({ queryKey: ['host', 'earnings'], queryFn: () => api<EarningsSummary>('/host/earnings') });
  if (earnings.isLoading) return <PageLoader />;
  if (earnings.isError) return <ErrorState error={earnings.error} />;
  const e = earnings.data!;
  return (
    <>
      <PageHeader title="Earnings" subtitle="Calculated from the immutable financial ledger (never from mutable reservation rows)." />
      <div className="grid gap-4 sm:grid-cols-4">
        <StatCard label="Gross bookings" value={money(e.gross, e.currency)} /><StatCard label="Platform fees" value={money(e.platformFees, e.currency)} />
        <StatCard label="Refunds" value={money(e.refunds, e.currency)} /><StatCard label="Net earnings" value={money(e.net, e.currency)} />
      </div>
      <h2 className="mt-8 mb-3 font-semibold">Ledger</h2>
      <DataTable rows={e.lines} rowKey={(l) => `${l.reservationId}-${l.account}-${l.occurredAt}-${l.amount}`} empty="No earnings yet."
        columns={[{ header: 'Date', cell: (l) => prettyDate(l.occurredAt) }, { header: 'Entry', cell: (l) => l.description }, { header: 'Account', cell: (l) => l.account },
          { header: 'Amount', cell: (l) => <span className={l.amount < 0 ? 'text-red-700' : 'text-emerald-700'}>{money(l.amount, l.currency)}</span>, className: 'text-right' }]} />
    </>
  );
}

export function HostReviewsPage() {
  const qc = useQueryClient();
  const [responding, setResponding] = useState<ReviewDto | null>(null);
  const [text, setText] = useState('');
  const reviews = useQuery({ queryKey: ['host', 'reviews'], queryFn: () => api<Paged<ReviewDto>>('/host/reviews?pageSize=50') });
  const respond = useMutation({ mutationFn: () => api(`/reviews/${responding!.id}/response`, { method: 'POST', body: { response: text } }), onSuccess: () => { toast.success('Response published'); setResponding(null); setText(''); qc.invalidateQueries({ queryKey: ['host', 'reviews'] }); }, onError: (e) => toast.error((e as Error).message) });
  return (
    <>
      <PageHeader title="Reviews" />
      {reviews.isLoading ? <Skeleton className="h-48" /> : reviews.isError ? <ErrorState error={reviews.error} />
        : reviews.data!.items.length === 0 ? <EmptyState icon={<Star className="h-7 w-7" />} title="No reviews yet" description="Reviews appear after guests complete their stays." />
        : (
          <div className="grid gap-4 md:grid-cols-2">
            {reviews.data!.items.map((r) => (
              <article key={r.id} className="card p-5">
                <div className="flex justify-between"><p className="font-semibold">{r.guestName}</p><span className="text-amber-500">{'★'.repeat(r.overall)}</span></div>
                <p className="text-xs text-slate-500">{r.propertyTitle} · {prettyDate(r.createdAt)}</p>
                <p className="mt-3 text-sm text-slate-700">{r.comment}</p>
                {r.hostResponse ? <p className="mt-3 rounded-xl bg-slate-50 p-3 text-sm"><span className="font-semibold">Your response: </span>{r.hostResponse}</p>
                  : <button className="btn-secondary mt-3 !py-1.5 text-xs" onClick={() => setResponding(r)}>Respond</button>}
              </article>
            ))}
          </div>
        )}
      <Modal open={!!responding} onClose={() => setResponding(null)} title={`Respond to ${responding?.guestName}`}>
        <textarea className="input min-h-28" value={text} onChange={(e) => setText(e.target.value)} maxLength={1000} aria-label="Response" />
        <button className="btn-primary mt-4 w-full" disabled={!text.trim() || respond.isPending} onClick={() => respond.mutate()}>Publish response</button>
      </Modal>
    </>
  );
}
