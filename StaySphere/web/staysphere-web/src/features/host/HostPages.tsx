import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { clsx } from 'clsx';
import { Banknote, BedDouble, CalendarDays, Coins, Home, Inbox, Landmark, Percent, Plus, Star, TrendingUp } from 'lucide-react';
import { useEffect, useState } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router';
import { Bar, BarChart, CartesianGrid, Line, LineChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';
import { api, toQuery } from '@/api/client';
import type { AuthResponse, EarningsSummary, HostDashboard, HostPropertyListItem, Paged, PayoutDto, PayoutSummary, ReservationDto, ReviewDto } from '@/api/types';
import { DataTable, EmptyState, ErrorState, Modal, PageHeader, PageLoader, Skeleton, StatCard, StatusBadge, SafeImg } from '@/components/ui';
import { hasRole, useAuthStore } from '@/lib/auth-store';
import { money, pluralize, prettyDate, stayLabel } from '@/lib/format';
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
  const requests = useQuery({ queryKey: ['host', 'reservations', 'requests'], queryFn: () => api<Paged<ReservationDto>>('/host/reservations?scope=requests&pageSize=50') });
  if (dash.isLoading) return <div className="grid gap-4 sm:grid-cols-3">{Array.from({ length: 6 }, (_, i) => <Skeleton key={i} className="h-28" />)}</div>;
  if (dash.isError) return <ErrorState error={dash.error} onRetry={() => dash.refetch()} />;
  const d = dash.data!;
  const user = useAuthStore.getState().user;
  return (
    <>
      <PageHeader title={analytics ? 'Analytics' : `Welcome back, ${user?.displayName.split(' ')[0]}`} subtitle={analytics ? 'Performance across your listings' : "Here's what's happening with your listings."} actions={<Link to="/host/properties/new" className="btn-primary"><Plus className="h-4 w-4" /> New listing</Link>} />
      {!analytics && (requests.data?.totalCount ?? 0) > 0 && (
        <Link to="/host/reservations?scope=requests" className="mb-6 flex items-center gap-3 rounded-2xl bg-amber-50 p-4 text-amber-900 ring-1 ring-amber-200 hover:bg-amber-100">
          <Inbox className="h-5 w-5" /> <span><strong>{requests.data!.totalCount} booking request{requests.data!.totalCount === 1 ? '' : 's'}</strong> waiting for your response — requests expire after 24 hours.</span>
        </Link>
      )}
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

function useCountdownLabel(deadline?: string | null) {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const id = setInterval(() => setNow(Date.now()), 30_000);
    return () => clearInterval(id);
  }, []);
  if (!deadline) return '';
  const ms = new Date(deadline).getTime() - now;
  if (ms <= 0) return 'expired';
  const h = Math.floor(ms / 3_600_000);
  return h >= 1 ? `${h}h ${Math.floor((ms % 3_600_000) / 60_000)}m left` : `${Math.max(1, Math.floor(ms / 60_000))}m left`;
}

function RequestCard({ r, onDecline }: { r: ReservationDto; onDecline: (r: ReservationDto) => void }) {
  const qc = useQueryClient();
  const left = useCountdownLabel(r.approvalDeadline);
  const [key] = useState(() => crypto.randomUUID());
  const accept = useMutation({
    mutationFn: () => api<ReservationDto>(`/host/reservations/${r.id}/accept`, { method: 'POST', idempotencyKey: key, body: {} }),
    onSuccess: () => { toast.success(`Accepted — ${r.guestName}'s stay is confirmed and the payment captured.`); qc.invalidateQueries({ queryKey: ['host'] }); },
    onError: (e) => { toast.error((e as Error).message); qc.invalidateQueries({ queryKey: ['host'] }); },
  });
  return (
    <article className="card flex flex-wrap items-center gap-4 p-5">
      {r.propertyImageUrl && <SafeImg src={r.propertyImageUrl} alt="" className="h-16 w-20 rounded-xl object-cover" />}
      <div className="min-w-0 flex-1">
        <p className="font-semibold">{r.guestName} · {pluralize(r.guests, 'guest')}</p>
        <p className="truncate text-sm text-slate-600">{r.propertyTitle} · {stayLabel(r.checkIn, r.checkOut)}</p>
        <p className="text-sm">{money(r.totalAmount, r.currency)} authorized · <span className="font-medium text-amber-700">{left}</span></p>
      </div>
      <div className="flex gap-2">
        <button className="btn-secondary" onClick={() => onDecline(r)}>Decline</button>
        <button className="btn-primary" disabled={accept.isPending} onClick={() => accept.mutate()}>Accept</button>
      </div>
    </article>
  );
}

export function HostReservationsPage() {
  const [params, setParams] = useSearchParams();
  const scope = params.get('scope') ?? 'upcoming';
  const setScope = (s: string) => setParams({ scope: s });
  const qc = useQueryClient();
  const [declining, setDeclining] = useState<ReservationDto | null>(null);
  const [reason, setReason] = useState('');
  const list = useQuery({ queryKey: ['host', 'reservations', scope], queryFn: () => api<Paged<ReservationDto>>(`/host/reservations${toQuery({ scope, pageSize: 50 })}`) });
  const requests = useQuery({ queryKey: ['host', 'reservations', 'requests'], queryFn: () => api<Paged<ReservationDto>>('/host/reservations?scope=requests&pageSize=50') });
  const navigate = useNavigate();
  const message = useMutation({ mutationFn: (r: ReservationDto) => api<{ id: string }>('/conversations', { method: 'POST', body: { propertyId: r.propertyId, reservationId: r.id, message: `Hi ${r.guestName}, looking forward to hosting you!` } }), onSuccess: (c) => navigate(`/messages/${c.id}`) });
  const cancel = useMutation({ mutationFn: (id: string) => api(`/reservations/${id}/cancel`, { method: 'POST', idempotencyKey: crypto.randomUUID(), body: { reason: 'Cancelled by host' } }), onSuccess: () => { toast.success('Cancelled — the guest receives a full refund.'); qc.invalidateQueries({ queryKey: ['host'] }); } });
  const decline = useMutation({
    mutationFn: () => api(`/host/reservations/${declining!.id}/decline`, { method: 'POST', idempotencyKey: crypto.randomUUID(), body: { reason: reason.trim() || null } }),
    onSuccess: () => { toast.success('Request declined — the guest was not charged.'); setDeclining(null); setReason(''); qc.invalidateQueries({ queryKey: ['host'] }); },
    onError: (e) => toast.error((e as Error).message),
  });
  const pending = requests.data?.totalCount ?? 0;
  return (
    <>
      <PageHeader title="Reservations" />
      <div className="mb-4 flex flex-wrap gap-2">
        {['requests', 'upcoming', 'past', 'cancelled', 'all'].map((s) => (
          <button key={s} className={clsx('chip capitalize', scope === s && 'chip-active')} onClick={() => setScope(s)}>
            {s}{s === 'requests' && pending > 0 && <span className="ml-1 rounded-full bg-coral-500 px-1.5 text-xs text-white">{pending}</span>}
          </button>
        ))}
      </div>
      {scope === 'requests' ? (
        requests.isLoading ? <Skeleton className="h-32" /> : requests.isError ? <ErrorState error={requests.error} />
          : requests.data!.items.length === 0
            ? <EmptyState icon={<Inbox className="h-7 w-7" />} title="No pending requests" description="Listings with Instant Book off send booking requests here. You have 24 hours to respond to each one." />
            : <div className="space-y-3">{requests.data!.items.map((r) => <RequestCard key={r.id} r={r} onDecline={setDeclining} />)}</div>
      ) : list.isLoading ? <Skeleton className="h-48" /> : list.isError ? <ErrorState error={list.error} /> : (
        <DataTable rows={list.data!.items} rowKey={(r) => r.id} empty="No reservations here yet."
          columns={[{ header: 'Guest', cell: (r) => r.guestName }, { header: 'Listing', cell: (r) => <span className="line-clamp-1">{r.propertyTitle}</span> },
            { header: 'Dates', cell: (r) => stayLabel(r.checkIn, r.checkOut) }, { header: 'Guests', cell: (r) => r.guests }, { header: 'Total', cell: (r) => money(r.totalAmount, r.currency) },
            { header: 'Status', cell: (r) => <StatusBadge status={r.status} /> },
            { header: '', cell: (r) => <div className="flex gap-2"><button className="btn-secondary !py-1.5 text-xs" onClick={() => message.mutate(r)}>Message</button>{r.canCancel && r.status === 'Confirmed' && <button className="btn-ghost !py-1.5 text-xs text-red-700" onClick={() => confirm('Cancel this reservation? The guest gets a full refund.') && cancel.mutate(r.id)}>Cancel</button>}</div> }]} />
      )}
      <Modal open={!!declining} onClose={() => setDeclining(null)} title={`Decline ${declining?.guestName}'s request?`}>
        <p className="text-sm text-slate-600">The card authorization is released immediately and the dates reopen. Your response rate is visible to guests.</p>
        <label className="label mt-4" htmlFor="reason">Message to the guest (optional)</label>
        <textarea id="reason" className="input" rows={3} maxLength={500} value={reason} onChange={(e) => setReason(e.target.value)} />
        <div className="mt-5 flex justify-end gap-3"><button className="btn-secondary" onClick={() => setDeclining(null)}>Keep request</button><button className="btn-danger" disabled={decline.isPending} onClick={() => decline.mutate()}>Decline request</button></div>
      </Modal>
    </>
  );
}

export function HostEarningsPage() {
  const qc = useQueryClient();
  const earnings = useQuery({ queryKey: ['host', 'earnings'], queryFn: () => api<EarningsSummary>('/host/earnings') });
  const payouts = useQuery({ queryKey: ['host', 'payouts'], queryFn: () => api<PayoutSummary>('/host/payouts') });
  const [editing, setEditing] = useState(false);
  const [account, setAccount] = useState({ accountHolder: '', iban: '', country: '' });
  const saveAccount = useMutation({
    mutationFn: () => api('/host/payout-account', { method: 'PUT', body: account }),
    onSuccess: () => { toast.success('Payout account saved'); setEditing(false); setAccount({ accountHolder: '', iban: '', country: '' }); qc.invalidateQueries({ queryKey: ['host', 'payouts'] }); },
    onError: (e) => toast.error((e as Error).message),
  });
  const [payoutKey, setPayoutKey] = useState(() => crypto.randomUUID());
  const requestPayout = useMutation({
    mutationFn: () => api<PayoutDto[]>('/host/payouts', { method: 'POST', idempotencyKey: payoutKey, body: {} }),
    onSuccess: (list) => {
      const failed = list.filter((p) => p.status === 'Failed');
      if (failed.length) toast.error(`Transfer failed: ${failed[0].failureReason}`);
      else toast.success(`Payout sent: ${list.map((p) => money(p.amount, p.currency)).join(', ')}`);
      setPayoutKey(crypto.randomUUID());
      qc.invalidateQueries({ queryKey: ['host'] });
    },
    onError: (e) => { toast.error((e as Error).message); setPayoutKey(crypto.randomUUID()); },
  });
  if (earnings.isLoading || payouts.isLoading) return <PageLoader />;
  if (earnings.isError) return <ErrorState error={earnings.error} />;
  if (payouts.isError) return <ErrorState error={payouts.error} />;
  const e = earnings.data!;
  const p = payouts.data!;
  const available = p.balances.filter((b) => b.available > 0);
  return (
    <>
      <PageHeader title="Earnings & payouts" subtitle="Calculated from the immutable financial ledger (never from mutable reservation rows)." />
      <div className="grid gap-4 sm:grid-cols-4">
        <StatCard label="Gross bookings" value={money(e.gross, e.currency)} /><StatCard label="Platform fees" value={money(e.platformFees, e.currency)} />
        <StatCard label="Refunds" value={money(e.refunds, e.currency)} /><StatCard label="Net earnings" value={money(e.net, e.currency)} />
      </div>

      <section className="mt-8 grid gap-6 lg:grid-cols-[1fr_360px]">
        <div className="card p-6">
          <div className="flex flex-wrap items-start justify-between gap-3">
            <div><h2 className="text-lg font-semibold">Balance</h2><p className="text-sm text-slate-500">{p.schedule}</p></div>
            <button className="btn-primary" disabled={!p.account || available.length === 0 || requestPayout.isPending} onClick={() => requestPayout.mutate()}>
              <Banknote className="h-4 w-4" /> Pay out now
            </button>
          </div>
          {p.balances.length === 0 ? <p className="mt-6 text-sm text-slate-500">No earnings yet.</p> : (
            <table className="mt-5 w-full text-sm">
              <thead className="text-left text-slate-500"><tr><th className="py-2 font-medium">Currency</th><th className="font-medium">Available</th><th className="font-medium">Pending (before check-in + 24h)</th><th className="font-medium">Paid out</th></tr></thead>
              <tbody className="divide-y divide-slate-100">
                {p.balances.map((b) => <tr key={b.currency}><td className="py-3 font-semibold">{b.currency}</td><td className="font-semibold text-emerald-700">{money(b.available, b.currency)}</td><td>{money(b.pending, b.currency)}</td><td>{money(b.paidOut, b.currency)}</td></tr>)}
              </tbody>
            </table>
          )}
        </div>
        <div className="card p-6">
          <h2 className="mb-3 flex items-center gap-2 text-lg font-semibold"><Landmark className="h-5 w-5" /> Payout account</h2>
          {p.account && !editing ? (
            <>
              <p className="font-medium">{p.account.accountHolder}</p>
              <p className="font-mono text-sm text-slate-600">{p.account.maskedAccount} · {p.account.country}</p>
              <button className="btn-ghost mt-3 !px-0 underline" onClick={() => setEditing(true)}>Change</button>
            </>
          ) : (
            <form className="space-y-3" onSubmit={(ev) => { ev.preventDefault(); saveAccount.mutate(); }}>
              {!p.account && <p className="rounded-xl bg-amber-50 p-3 text-sm text-amber-900">Add an account to receive payouts. Test IBANs are fine (one ending in 0000 simulates a rejected transfer).</p>}
              <input className="input" placeholder="Account holder" aria-label="Account holder" value={account.accountHolder} onChange={(x) => setAccount({ ...account, accountHolder: x.target.value })} required />
              <input className="input font-mono" placeholder="IBAN, e.g. PT50 0002 0123 1234 5678 9015 4" aria-label="IBAN" value={account.iban} onChange={(x) => setAccount({ ...account, iban: x.target.value })} required />
              <input className="input" placeholder="Country (e.g. PT)" aria-label="Country" maxLength={2} value={account.country} onChange={(x) => setAccount({ ...account, country: x.target.value.toUpperCase() })} required />
              <p className="text-xs text-slate-500">Only a masked reference is stored.</p>
              <div className="flex gap-2"><button className="btn-primary" disabled={saveAccount.isPending}>Save</button>{p.account && <button type="button" className="btn-ghost" onClick={() => setEditing(false)}>Cancel</button>}</div>
            </form>
          )}
        </div>
      </section>

      <h2 className="mt-10 mb-3 font-semibold">Payout history</h2>
      <DataTable rows={p.payouts} rowKey={(x) => x.id} empty="No payouts yet."
        columns={[{ header: 'Date', cell: (x) => prettyDate(x.createdAt) }, { header: 'Amount', cell: (x) => money(x.amount, x.currency) }, { header: 'To', cell: (x) => <span className="font-mono text-xs">{x.destination}</span> },
          { header: 'Stays', cell: (x) => x.reservationCount }, { header: 'Type', cell: (x) => (x.automatic ? 'Automatic' : 'Requested') },
          { header: 'Status', cell: (x) => <span title={x.failureReason ?? undefined}><StatusBadge status={x.status} /></span> }]} />

      <h2 className="mt-10 mb-3 font-semibold">Ledger</h2>
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
