import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { AlertTriangle, CalendarCheck2, Coins, Flag, Home, LifeBuoy, RotateCcw, Users } from 'lucide-react';
import { useState } from 'react';
import { Link } from 'react-router';
import { Area, AreaChart, Bar, BarChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';
import { format, parseISO } from 'date-fns';
import { api, toQuery } from '@/api/client';
import type { AdminDashboard, AdminProperty, AdminUser, AuditLog, FraudAlert, Paged, PaymentDto, PayoutDto, ReportDto, ReservationDto, ReviewDto } from '@/api/types';
import { Badge, DataTable, ErrorState, PageHeader, Pagination, Skeleton, StatCard, StatusBadge } from '@/components/ui';
import { dateTime, money, prettyDate, stayLabel } from '@/lib/format';
import { toast } from '@/lib/toast';

function usePaged<T>(key: string, path: string, params: Record<string, unknown>) {
  return useQuery({ queryKey: ['admin', key, params], queryFn: () => api<Paged<T>>(`${path}${toQuery(params)}`), placeholderData: keepPreviousData });
}

const day = (d: string) => format(parseISO(d), 'd MMM');

export function AdminDashboardPage() {
  const dash = useQuery({ queryKey: ['admin', 'dashboard'], queryFn: () => api<AdminDashboard>('/admin/dashboard') });
  if (dash.isLoading) return <div className="grid gap-4 sm:grid-cols-4">{Array.from({ length: 8 }, (_, i) => <Skeleton key={i} className="h-28" />)}</div>;
  if (dash.isError) return <ErrorState error={dash.error} onRetry={() => dash.refetch()} />;
  const d = dash.data!;
  return (
    <>
      <PageHeader title="Platform overview" subtitle="Last 30 days" />
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <StatCard label="Users" value={d.users.toLocaleString()} hint={`${d.hosts} hosts`} icon={<Users className="h-4 w-4" />} />
        <StatCard label="Listings" value={d.properties} hint={`${d.publishedProperties} published`} icon={<Home className="h-4 w-4" />} />
        <StatCard label="Bookings" value={d.bookings} hint={`Cancellation rate ${(d.cancellationRate * 100).toFixed(1)}%`} icon={<CalendarCheck2 className="h-4 w-4" />} />
        <StatCard label="Platform revenue" value={money(d.revenue, d.currency, true)} hint="Converted to USD at reference rates" icon={<Coins className="h-4 w-4" />} />
        <StatCard label="Open reports" value={d.openReports} icon={<Flag className="h-4 w-4" />} />
        <StatCard label="Open tickets" value={d.openTickets} icon={<LifeBuoy className="h-4 w-4" />} />
        <StatCard label="Fraud alerts" value={d.fraudAlerts} icon={<AlertTriangle className="h-4 w-4" />} />
        <StatCard label="Refunds" value={money(d.refunds, d.currency, true)} icon={<RotateCcw className="h-4 w-4" />} />
      </div>
      <div className="mt-8 grid gap-6 lg:grid-cols-2">
        <Chart title="Daily bookings" data={d.dailyBookings} kind="bar" />
        <Chart title="Daily platform revenue" data={d.dailyRevenue} kind="area" />
        <Chart title="New users" data={d.newUsers} kind="bar" />
        <Chart title="New listings" data={d.newProperties} kind="area" />
      </div>
    </>
  );
}

function Chart({ title, data, kind }: { title: string; data: { date: string; value: number }[]; kind: 'bar' | 'area' }) {
  const rows = data.map((p) => ({ d: day(p.date), v: p.value }));
  return (
    <div className="card p-5"><h2 className="mb-4 font-semibold">{title}</h2>
      <div className="h-56"><ResponsiveContainer>{kind === 'bar'
        ? <BarChart data={rows}><CartesianGrid strokeDasharray="3 3" vertical={false} /><XAxis dataKey="d" fontSize={11} interval={4} /><YAxis fontSize={11} allowDecimals={false} /><Tooltip /><Bar dataKey="v" fill="#0f766e" radius={[4, 4, 0, 0]} /></BarChart>
        : <AreaChart data={rows}><CartesianGrid strokeDasharray="3 3" vertical={false} /><XAxis dataKey="d" fontSize={11} interval={4} /><YAxis fontSize={11} /><Tooltip /><Area dataKey="v" stroke="#f43f5e" fill="#fecdd3" /></AreaChart>}
      </ResponsiveContainer></div>
    </div>
  );
}

export function AdminUsersPage() {
  const qc = useQueryClient();
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  const users = usePaged<AdminUser>('users', '/admin/users', { search, page, pageSize: 25 });
  const act = useMutation({ mutationFn: ({ id, action }: { id: string; action: 'suspend' | 'reinstate' }) => api(`/admin/users/${id}/${action}`, { method: 'POST' }),
    onSuccess: (_d, v) => { toast.success(v.action === 'suspend' ? 'User suspended and signed out' : 'User reinstated'); qc.invalidateQueries({ queryKey: ['admin', 'users'] }); }, onError: (e) => toast.error((e as Error).message) });
  return (
    <>
      <PageHeader title="Users" actions={<input className="input !w-64" placeholder="Search name or email" value={search} onChange={(e) => { setSearch(e.target.value); setPage(1); }} aria-label="Search users" />} />
      {users.isError ? <ErrorState error={users.error} /> : !users.data ? <Skeleton className="h-96" /> : (
        <>
          <DataTable rows={users.data.items} rowKey={(u) => u.id} columns={[
            { header: 'User', cell: (u) => <div><p className="font-medium">{u.displayName}</p><p className="text-xs text-slate-500">{u.email}</p></div> },
            { header: 'Roles', cell: (u) => <div className="flex gap-1">{u.roles.map((r) => <Badge key={r}>{r}</Badge>)}</div> },
            { header: 'Status', cell: (u) => <StatusBadge status={u.status} /> },
            { header: 'Joined', cell: (u) => prettyDate(u.createdAt) },
            { header: 'Last login', cell: (u) => (u.lastLoginAt ? dateTime(u.lastLoginAt) : '—') },
            { header: '', cell: (u) => u.roles.includes('Admin') ? null : u.status === 'Suspended'
              ? <button className="btn-secondary !py-1 text-xs" onClick={() => act.mutate({ id: u.id, action: 'reinstate' })}>Reinstate</button>
              : <button className="btn-ghost !py-1 text-xs text-red-700" onClick={() => confirm(`Suspend ${u.displayName}? Their sessions will be revoked.`) && act.mutate({ id: u.id, action: 'suspend' })}>Suspend</button> },
          ]} />
          <Pagination page={page} totalPages={Math.ceil(users.data.totalCount / 25)} onChange={setPage} />
        </>
      )}
    </>
  );
}

export function AdminPropertiesPage() {
  const qc = useQueryClient();
  const [search, setSearch] = useState('');
  const [status, setStatus] = useState('');
  const [page, setPage] = useState(1);
  const list = usePaged<AdminProperty>('properties', '/admin/properties', { search, status, page, pageSize: 25 });
  const suspend = useMutation({ mutationFn: (id: string) => api(`/admin/properties/${id}/suspend`, { method: 'POST' }), onSuccess: () => { toast.success('Listing suspended'); qc.invalidateQueries({ queryKey: ['admin', 'properties'] }); } });
  return (
    <>
      <PageHeader title="Listings" actions={<div className="flex gap-2"><input className="input !w-56" placeholder="Search title or city" value={search} onChange={(e) => { setSearch(e.target.value); setPage(1); }} aria-label="Search" />
        <select className="input !w-auto" value={status} onChange={(e) => setStatus(e.target.value)} aria-label="Status"><option value="">All statuses</option>{['Draft', 'Published', 'Unlisted', 'Suspended'].map((s) => <option key={s}>{s}</option>)}</select></div>} />
      {!list.data ? <Skeleton className="h-96" /> : (
        <>
          <DataTable rows={list.data.items} rowKey={(p) => p.id} columns={[
            { header: 'Listing', cell: (p) => <Link to={`/property/${p.id}`} className="font-medium hover:underline">{p.title || 'Untitled'}</Link> },
            { header: 'Host', cell: (p) => p.hostName }, { header: 'City', cell: (p) => p.city }, { header: 'Price', cell: (p) => money(p.basePrice, p.currency, true) },
            { header: 'Rating', cell: (p) => (p.reviewCount ? `${p.ratingAverage.toFixed(2)} (${p.reviewCount})` : '—') }, { header: 'Status', cell: (p) => <StatusBadge status={p.status} /> },
            { header: '', cell: (p) => p.status !== 'Suspended' && <button className="btn-ghost !py-1 text-xs text-red-700" onClick={() => confirm('Suspend this listing?') && suspend.mutate(p.id)}>Suspend</button> },
          ]} />
          <Pagination page={page} totalPages={Math.ceil(list.data.totalCount / 25)} onChange={setPage} />
        </>
      )}
    </>
  );
}

export function AdminReservationsPage() {
  const [status, setStatus] = useState('');
  const [page, setPage] = useState(1);
  const list = usePaged<ReservationDto>('reservations', '/admin/reservations', { status, page, pageSize: 25 });
  return (
    <>
      <PageHeader title="Reservations" actions={<select className="input !w-auto" value={status} onChange={(e) => setStatus(e.target.value)} aria-label="Status"><option value="">All</option>{['Held', 'PaymentPending', 'Confirmed', 'Completed', 'Cancelled', 'RefundPending', 'Refunded', 'Failed', 'Expired'].map((s) => <option key={s}>{s}</option>)}</select>} />
      {!list.data ? <Skeleton className="h-96" /> : (
        <>
          <DataTable rows={list.data.items} rowKey={(r) => r.id} columns={[
            { header: 'Listing', cell: (r) => <span className="line-clamp-1">{r.propertyTitle}</span> }, { header: 'Guest', cell: (r) => r.guestName }, { header: 'Host', cell: (r) => r.hostName },
            { header: 'Dates', cell: (r) => stayLabel(r.checkIn, r.checkOut) }, { header: 'Total', cell: (r) => money(r.totalAmount, r.currency) }, { header: 'Status', cell: (r) => <StatusBadge status={r.status} /> },
          ]} />
          <Pagination page={page} totalPages={Math.ceil(list.data.totalCount / 25)} onChange={setPage} />
        </>
      )}
    </>
  );
}

export function AdminPaymentsPage() {
  const qc = useQueryClient();
  const [page, setPage] = useState(1);
  const list = usePaged<PaymentDto>('payments', '/admin/payments', { page, pageSize: 25 });
  const refund = useMutation({
    mutationFn: (p: PaymentDto) => api(`/admin/payments/${p.id}/refund`, { method: 'POST', idempotencyKey: crypto.randomUUID(), body: { amount: p.amount - p.refundedAmount, reason: 'Admin goodwill refund' } }),
    onSuccess: () => { toast.success('Refund processed'); qc.invalidateQueries({ queryKey: ['admin', 'payments'] }); }, onError: (e) => toast.error((e as Error).message),
  });
  return (
    <>
      <PageHeader title="Payments" subtitle="Simulated provider — no real money moves." />
      {!list.data ? <Skeleton className="h-96" /> : (
        <>
          <DataTable rows={list.data.items} rowKey={(p) => p.id} columns={[
            { header: 'Date', cell: (p) => dateTime(p.createdAt) }, { header: 'Amount', cell: (p) => money(p.amount, p.currency) }, { header: 'Refunded', cell: (p) => (p.refundedAmount ? money(p.refundedAmount, p.currency) : '—') },
            { header: 'Card', cell: (p) => (p.cardLast4 ? `•••• ${p.cardLast4}` : '—') }, { header: 'Status', cell: (p) => <StatusBadge status={p.status} /> }, { header: 'Reservation', cell: (p) => <StatusBadge status={p.reservationStatus} /> },
            { header: '', cell: (p) => (p.status === 'Succeeded' || p.status === 'PartiallyRefunded') && <button className="btn-ghost !py-1 text-xs" onClick={() => confirm(`Refund ${money(p.amount - p.refundedAmount, p.currency)}?`) && refund.mutate(p)}>Refund</button> },
          ]} />
          <Pagination page={page} totalPages={Math.ceil(list.data.totalCount / 25)} onChange={setPage} />
        </>
      )}
      <AdminPayoutsSection />
    </>
  );
}

export function AdminPayoutsSection() {
  const list = usePaged<PayoutDto>('payouts', '/admin/payouts', { pageSize: 25 });
  return (
    <section className="mt-10">
      <h2 className="mb-3 font-semibold">Host payouts</h2>
      {!list.data ? <Skeleton className="h-40" /> : <DataTable rows={list.data.items} rowKey={(p) => p.id} empty="No payouts yet." columns={[
        { header: 'Date', cell: (p) => dateTime(p.createdAt) }, { header: 'Host', cell: (p) => p.hostName }, { header: 'Amount', cell: (p) => money(p.amount, p.currency) },
        { header: 'Stays', cell: (p) => p.reservationCount }, { header: 'Type', cell: (p) => (p.automatic ? 'Automatic' : 'Requested') },
        { header: 'Status', cell: (p) => <span title={p.failureReason ?? undefined}><StatusBadge status={p.status} /></span> },
      ]} />}
    </section>
  );
}

export function AdminReviewsPage() {
  const qc = useQueryClient();
  const [page, setPage] = useState(1);
  const list = usePaged<ReviewDto>('reviews', '/admin/reviews', { page, pageSize: 25 });
  const moderate = useMutation({ mutationFn: ({ id, hide }: { id: string; hide: boolean }) => api(`/admin/reviews/${id}/moderate`, { method: 'POST', body: { hide, note: hide ? 'Hidden by moderator' : null } }), onSuccess: () => qc.invalidateQueries({ queryKey: ['admin', 'reviews'] }) });
  return (
    <>
      <PageHeader title="Review moderation" />
      {!list.data ? <Skeleton className="h-96" /> : (
        <>
          <DataTable rows={list.data.items} rowKey={(r) => r.id} columns={[
            { header: 'Review', cell: (r) => <div className="max-w-md"><p className="text-xs text-slate-500">{r.guestName} on {r.propertyTitle}</p><p className="line-clamp-2">{r.comment}</p></div> },
            { header: 'Rating', cell: (r) => '★'.repeat(r.overall) }, { header: 'Date', cell: (r) => prettyDate(r.createdAt) }, { header: 'Status', cell: (r) => <StatusBadge status={r.status} /> },
            { header: '', cell: (r) => <button className="btn-ghost !py-1 text-xs" onClick={() => moderate.mutate({ id: r.id, hide: r.status === 'Published' })}>{r.status === 'Published' ? 'Hide' : 'Restore'}</button> },
          ]} />
          <Pagination page={page} totalPages={Math.ceil(list.data.totalCount / 25)} onChange={setPage} />
        </>
      )}
    </>
  );
}

export function AdminReportsPage() {
  const qc = useQueryClient();
  const reports = usePaged<ReportDto>('reports', '/admin/reports', { pageSize: 50 });
  const alerts = usePaged<FraudAlert>('fraud', '/admin/fraud-alerts', { pageSize: 50 });
  const resolve = useMutation({ mutationFn: ({ id, actioned }: { id: string; actioned: boolean }) => api(`/admin/reports/${id}/resolve?actioned=${actioned}`, { method: 'POST' }), onSuccess: () => qc.invalidateQueries({ queryKey: ['admin', 'reports'] }) });
  const ack = useMutation({ mutationFn: (id: string) => api(`/admin/fraud-alerts/${id}/acknowledge`, { method: 'POST' }), onSuccess: () => qc.invalidateQueries({ queryKey: ['admin', 'fraud'] }) });
  return (
    <>
      <PageHeader title="Reports & fraud alerts" subtitle="Rule-based risk signals (foundation only — not a production fraud system)." />
      <h2 className="mb-3 font-semibold">Fraud alerts</h2>
      {alerts.data && <DataTable rows={alerts.data.items} rowKey={(a) => a.id} empty="No alerts." columns={[
        { header: 'Rule', cell: (a) => <code className="text-xs">{a.ruleCode}</code> }, { header: 'Details', cell: (a) => a.details }, { header: 'Score', cell: (a) => a.score },
        { header: 'Decision', cell: (a) => <StatusBadge status={a.decision} /> }, { header: 'When', cell: (a) => dateTime(a.createdAt) },
        { header: '', cell: (a) => a.acknowledged ? <Badge tone="green">Acknowledged</Badge> : <button className="btn-secondary !py-1 text-xs" onClick={() => ack.mutate(a.id)}>Acknowledge</button> },
      ]} />}
      <h2 className="mt-10 mb-3 font-semibold">User reports</h2>
      {reports.data && <DataTable rows={reports.data.items} rowKey={(r) => r.id} empty="No reports." columns={[
        { header: 'Target', cell: (r) => `${r.targetType}` }, { header: 'Reason', cell: (r) => r.reason }, { header: 'Reporter', cell: (r) => r.reporterName },
        { header: 'Status', cell: (r) => <StatusBadge status={r.status} /> }, { header: 'When', cell: (r) => dateTime(r.createdAt) },
        { header: '', cell: (r) => r.status === 'Open' && <div className="flex gap-1"><button className="btn-secondary !py-1 text-xs" onClick={() => resolve.mutate({ id: r.id, actioned: true })}>Action</button><button className="btn-ghost !py-1 text-xs" onClick={() => resolve.mutate({ id: r.id, actioned: false })}>Dismiss</button></div> },
      ]} />}
    </>
  );
}

export function AdminAuditPage() {
  const [action, setAction] = useState('');
  const [page, setPage] = useState(1);
  const list = usePaged<AuditLog>('audit', '/admin/audit', { action, page, pageSize: 50 });
  return (
    <>
      <PageHeader title="Audit log" subtitle="Immutable record of security-relevant and business actions." actions={<input className="input !w-64" placeholder="Filter by action prefix (e.g. auth.)" value={action} onChange={(e) => { setAction(e.target.value); setPage(1); }} aria-label="Action filter" />} />
      {!list.data ? <Skeleton className="h-96" /> : (
        <>
          <DataTable rows={list.data.items} rowKey={(a) => String(a.id)} columns={[
            { header: 'When', cell: (a) => dateTime(a.at) }, { header: 'Actor', cell: (a) => a.actorName ?? 'system' }, { header: 'Action', cell: (a) => <code className="text-xs">{a.action}</code> },
            { header: 'Entity', cell: (a) => <span className="text-xs">{a.entityType} {a.entityId?.slice(0, 8)}</span> }, { header: 'IP', cell: (a) => <span className="text-xs">{a.ipAddress}</span> },
            { header: 'Trace', cell: (a) => <span className="font-mono text-xs text-slate-500">{a.correlationId?.slice(0, 12)}</span> },
          ]} />
          <Pagination page={page} totalPages={Math.ceil(list.data.totalCount / 50)} onChange={setPage} />
        </>
      )}
    </>
  );
}
