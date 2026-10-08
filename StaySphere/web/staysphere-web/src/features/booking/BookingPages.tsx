import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { clsx } from 'clsx';
import { CalendarCheck2, CheckCircle2, CreditCard, Hourglass, Lock, MessageSquare, Plane, Star, Timer } from 'lucide-react';
import { useEffect, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router';
import { api, ApiError, newIdempotencyKey, toQuery } from '@/api/client';
import type { CancellationPreview, Paged, PaymentDto, ReservationDto } from '@/api/types';
import { EmptyState, ErrorState, Modal, PageHeader, PageLoader, Skeleton, Spinner, StatusBadge, SafeImg } from '@/components/ui';
import { dateTime, money, pluralize, prettyDate, stayLabel } from '@/lib/format';
import { toast } from '@/lib/toast';

/** Maps a test card number to the simulator's token — the API never receives card numbers. */
export function tokenizeTestCard(card: string): string {
  const digits = card.replace(/\D/g, '');
  const map: Record<string, string> = { '4242424242424242': 'tok_4242', '4000000000000002': 'tok_0002', '4000000000009995': 'tok_9995', '4000000000000119': 'tok_0119', '4000000000000259': 'tok_0259' };
  return map[digits] ?? 'tok_invalid';
}

function useCountdown(until?: string | null) {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const t = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(t);
  }, []);
  if (!until) return null;
  const ms = Math.max(0, new Date(until).getTime() - now);
  return { ms, label: `${Math.floor(ms / 60000)}:${String(Math.floor((ms % 60000) / 1000)).padStart(2, '0')}` };
}

function StaySummary({ r }: { r: ReservationDto }) {
  return (
    <div className="card overflow-hidden">
      <div className="flex gap-4 border-b border-slate-100 p-5">
        {r.propertyImageUrl && <SafeImg src={r.propertyImageUrl} alt="" className="h-24 w-28 rounded-xl object-cover" />}
        <div>
          <p className="font-semibold">{r.propertyTitle}</p>
          <p className="text-sm text-slate-600">{r.city}, {r.country}</p>
          <p className="mt-1 text-sm text-slate-600">{stayLabel(r.checkIn, r.checkOut)} · {pluralize(r.guests, 'guest')}</p>
        </div>
      </div>
      <dl className="space-y-2 p-5 text-sm">
        <div className="flex justify-between"><dt>Accommodation ({pluralize(r.nights, 'night')})</dt><dd>{money(r.baseAmount, r.currency)}</dd></div>
        {r.discount > 0 && <div className="flex justify-between text-emerald-700"><dt>Discounts</dt><dd>−{money(r.discount, r.currency)}</dd></div>}
        <div className="flex justify-between"><dt>Cleaning fee</dt><dd>{money(r.cleaningFee, r.currency)}</dd></div>
        <div className="flex justify-between"><dt>Service fee</dt><dd>{money(r.serviceFee, r.currency)}</dd></div>
        <div className="flex justify-between"><dt>Taxes</dt><dd>{money(r.taxes, r.currency)}</dd></div>
        <div className="flex justify-between border-t border-slate-200 pt-3 text-base font-semibold"><dt>Total ({r.currency})</dt><dd>{money(r.totalAmount, r.currency)}</dd></div>
      </dl>
    </div>
  );
}

export function CheckoutPage() {
  const { id = '' } = useParams();
  const qc = useQueryClient();
  const reservation = useQuery({ queryKey: ['reservation', id], queryFn: () => api<ReservationDto>(`/reservations/${id}`), refetchInterval: (q) => (q.state.data?.status === 'PaymentPending' ? 2000 : false) });
  const countdown = useCountdown(reservation.data?.holdExpiresAt);
  const [card, setCard] = useState('4242 4242 4242 4242');
  const [confirmOpen, setConfirmOpen] = useState(false);
  const [idempotencyKey, setIdempotencyKey] = useState(newIdempotencyKey);

  const pay = useMutation({
    mutationFn: () => api<PaymentDto>(`/reservations/${id}/payment`, { method: 'POST', idempotencyKey, body: { paymentMethodToken: tokenizeTestCard(card) } }),
    onSuccess: (p) => {
      setConfirmOpen(false);
      qc.invalidateQueries({ queryKey: ['reservation', id] });
      if (p.status === 'Pending') toast.info('Payment is processing — this page will update automatically.');
    },
    onError: (e) => {
      setConfirmOpen(false);
      setIdempotencyKey(newIdempotencyKey()); // a new attempt gets a new key
      qc.invalidateQueries({ queryKey: ['reservation', id] });
      toast.error(e instanceof ApiError ? e.message : 'Payment failed.');
    },
  });

  if (reservation.isLoading) return <PageLoader />;
  if (reservation.isError) return <div className="container-page py-16"><ErrorState error={reservation.error} /></div>;
  const r = reservation.data!;

  if (r.status === 'AwaitingApproval') {
    return (
      <div className="container-page max-w-2xl py-16 text-center">
        <Hourglass className="mx-auto h-16 w-16 text-amber-500" />
        <h1 className="mt-4 text-3xl font-bold">Request sent to {r.hostName}</h1>
        <p className="mt-2 text-slate-600">Your card is authorized for {money(r.totalAmount, r.currency)} but <strong>not charged</strong>. The host has until {r.approvalDeadline ? dateTime(r.approvalDeadline) : 'tomorrow'} to respond — if they decline or don't answer, the hold is released automatically.</p>
        <div className="mt-8 text-left"><StaySummary r={r} /></div>
        <div className="mt-6 flex justify-center gap-3"><Link to={`/trips/${r.id}`} className="btn-primary">View request</Link><Link to="/messages" className="btn-secondary">Message host</Link></div>
      </div>
    );
  }

  if (r.status === 'Confirmed') {
    return (
      <div className="container-page max-w-2xl py-16 text-center">
        <CheckCircle2 className="mx-auto h-16 w-16 text-emerald-600" />
        <h1 className="mt-4 text-3xl font-bold">You're going to {r.city}!</h1>
        <p className="mt-2 text-slate-600">Your reservation is confirmed. A confirmation email is on its way (local: Mailpit at localhost:8025).</p>
        <div className="mt-8 text-left"><StaySummary r={r} /></div>
        <div className="mt-6 flex justify-center gap-3"><Link to={`/trips/${r.id}`} className="btn-primary">View trip</Link><Link to="/messages" className="btn-secondary">Message host</Link></div>
      </div>
    );
  }

  const expired = r.status === 'Expired' || r.status === 'Failed' || r.status === 'Cancelled' || (countdown?.ms === 0 && r.status === 'Held');
  return (
    <div className="container-page max-w-5xl py-10">
      <PageHeader title="Confirm and pay" />
      <div className="grid gap-10 lg:grid-cols-[1fr_400px]">
        <div className="space-y-6">
          {r.status === 'Held' && countdown && !expired && (
            <div className={clsx('flex items-center gap-3 rounded-2xl p-4', countdown.ms < 120_000 ? 'bg-amber-50 text-amber-900' : 'bg-brand-50 text-brand-900')} role="timer" aria-live="off">
              <Timer className="h-5 w-5" /> These dates are held for you for <strong>{countdown.label}</strong>.
            </div>
          )}
          {r.status === 'PaymentPending' && <div className="flex items-center gap-3 rounded-2xl bg-sky-50 p-4 text-sky-900"><Spinner /> Waiting for the payment provider to confirm…</div>}
          {expired && (
            <div role="alert" className="rounded-2xl bg-red-50 p-5 text-red-900">
              <p className="font-semibold">{r.status === 'Failed' ? 'Payment failed' : 'This hold has expired'}</p>
              <p className="mt-1 text-sm">The dates were released. You can try booking again from the listing.</p>
              <Link to={`/property/${r.propertyId}${toQuery({ checkIn: r.checkIn, checkOut: r.checkOut, guests: r.guests })}`} className="btn-primary mt-4">Back to listing</Link>
            </div>
          )}

          {!expired && r.status === 'Held' && (
            <section className="card p-6">
              <h2 className="mb-1 flex items-center gap-2 text-lg font-semibold"><CreditCard className="h-5 w-5" /> Pay with card</h2>
              <p className="mb-4 text-sm text-slate-600">Test mode — no real money. Card details are converted to a token in your browser; StaySphere never stores card numbers.</p>
              <label className="label" htmlFor="card">Card number</label>
              <input id="card" className="input font-mono" inputMode="numeric" autoComplete="off" value={card} onChange={(e) => setCard(e.target.value)} />
              <div className="mt-3 grid grid-cols-2 gap-3">
                <div><label className="label" htmlFor="exp">Expiry</label><input id="exp" className="input" defaultValue="12/34" /></div>
                <div><label className="label" htmlFor="cvc">CVC</label><input id="cvc" className="input" defaultValue="123" /></div>
              </div>
              <details className="mt-4 text-sm text-slate-600">
                <summary className="cursor-pointer font-medium">Test cards</summary>
                <ul className="mt-2 space-y-1 font-mono text-xs">
                  <li>4242 4242 4242 4242 — success</li><li>4000 0000 0000 0002 — declined</li><li>4000 0000 0000 9995 — insufficient funds</li>
                  <li>4000 0000 0000 0119 — delayed (webhook confirms)</li><li>4000 0000 0000 0259 — success + duplicate webhook</li>
                </ul>
              </details>
              <div className="mt-6 rounded-xl bg-slate-50 p-4 text-sm text-slate-600">
                <p className="font-semibold text-slate-800">Cancellation policy: {r.cancellationPolicy}</p>
                <p>Free cancellation within 48 hours of booking if check-in is at least 24 hours away.</p>
              </div>
              <button className="btn-accent mt-6 w-full !py-3 text-base" onClick={() => setConfirmOpen(true)}><Lock className="h-4 w-4" /> {r.requiresApproval ? `Request to book · authorize ${money(r.totalAmount, r.currency)}` : `Confirm and pay ${money(r.totalAmount, r.currency)}`}</button>
              {r.requiresApproval && <p className="mt-2 text-center text-sm text-slate-500">This host approves each booking. You're only charged if they accept within 24 hours.</p>}
            </section>
          )}
        </div>
        <aside><StaySummary r={r} /></aside>
      </div>

      <Modal open={confirmOpen} onClose={() => setConfirmOpen(false)} title="Confirm your booking">
        <p className="text-slate-700">{r.requiresApproval ? <>We'll authorize <strong>{money(r.totalAmount, r.currency)}</strong> on your card and send your request to the host. You're charged only if they accept.</> : <>You're about to pay <strong>{money(r.totalAmount, r.currency)}</strong> for {r.propertyTitle}, {stayLabel(r.checkIn, r.checkOut)}.</>}</p>
        <div className="mt-6 flex justify-end gap-3">
          <button className="btn-secondary" onClick={() => setConfirmOpen(false)}>Go back</button>
          <button className="btn-accent" disabled={pay.isPending} onClick={() => pay.mutate()}>{pay.isPending && <Spinner className="h-4 w-4" />} {r.requiresApproval ? 'Send request' : 'Pay now'}</button>
        </div>
      </Modal>
    </div>
  );
}

export function TripsPage() {
  const [scope, setScope] = useState<'upcoming' | 'past' | 'cancelled'>('upcoming');
  const trips = useQuery({ queryKey: ['trips', scope], queryFn: () => api<Paged<ReservationDto>>(`/reservations${toQuery({ scope, pageSize: 50 })}`) });
  return (
    <div className="container-page py-10">
      <PageHeader title="Trips" />
      <div className="mb-6 flex gap-2" role="tablist">
        {(['upcoming', 'past', 'cancelled'] as const).map((s) => <button key={s} role="tab" aria-selected={scope === s} className={clsx('chip capitalize', scope === s && 'chip-active')} onClick={() => setScope(s)}>{s}</button>)}
      </div>
      {trips.isLoading ? <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">{Array.from({ length: 3 }, (_, i) => <Skeleton key={i} className="h-64" />)}</div>
        : trips.isError ? <ErrorState error={trips.error} onRetry={() => trips.refetch()} />
        : trips.data!.items.length === 0 ? (
          <EmptyState icon={<Plane className="h-7 w-7" />} title={scope === 'upcoming' ? 'Your upcoming stays will appear here.' : scope === 'past' ? 'No past trips yet.' : 'No cancelled trips.'}
            description="Time to dust off your bags and start planning your next adventure." action={<Link to="/search" className="btn-primary">Start searching</Link>} />
        ) : (
          <div className="grid gap-6 sm:grid-cols-2 lg:grid-cols-3">
            {trips.data!.items.map((r) => (
              <Link key={r.id} to={r.status === 'Held' ? `/book/${r.id}` : `/trips/${r.id}`} className="card group overflow-hidden transition hover:shadow-lg">
                {r.propertyImageUrl && <SafeImg src={r.propertyImageUrl} alt="" className="h-40 w-full object-cover" loading="lazy" />}
                <div className="p-4">
                  <div className="flex items-start justify-between gap-2"><p className="font-semibold">{r.city}</p><StatusBadge status={r.status} /></div>
                  <p className="line-clamp-1 text-sm text-slate-600">{r.propertyTitle}</p>
                  <p className="mt-2 text-sm">{stayLabel(r.checkIn, r.checkOut)}</p>
                  {r.canReview && <p className="mt-2 text-sm font-semibold text-brand-700">Leave a review →</p>}
                </div>
              </Link>
            ))}
          </div>
        )}
    </div>
  );
}

export function TripDetailPage() {
  const { id = '' } = useParams();
  const qc = useQueryClient();
  const navigate = useNavigate();
  const trip = useQuery({ queryKey: ['reservation', id], queryFn: () => api<ReservationDto>(`/reservations/${id}`) });
  const [cancelOpen, setCancelOpen] = useState(false);
  const [reviewOpen, setReviewOpen] = useState(false);
  const preview = useQuery({ queryKey: ['cancel-preview', id], queryFn: () => api<CancellationPreview>(`/reservations/${id}/cancel/preview`, { method: 'POST' }), enabled: cancelOpen });
  const [cancelKey] = useState(newIdempotencyKey);
  const cancel = useMutation({
    mutationFn: (reason: string) => api<ReservationDto>(`/reservations/${id}/cancel`, { method: 'POST', idempotencyKey: cancelKey, body: { reason } }),
    onSuccess: () => { toast.success('Reservation cancelled'); setCancelOpen(false); qc.invalidateQueries({ queryKey: ['reservation', id] }); qc.invalidateQueries({ queryKey: ['trips'] }); },
    onError: (e) => toast.error((e as Error).message),
  });
  const message = useMutation({
    mutationFn: () => api<{ id: string }>('/conversations', { method: 'POST', body: { propertyId: trip.data!.propertyId, reservationId: id, message: `Hi! I have a question about my stay ${stayLabel(trip.data!.checkIn, trip.data!.checkOut)}.` } }),
    onSuccess: (c) => navigate(`/messages/${c.id}`),
  });

  if (trip.isLoading) return <PageLoader />;
  if (trip.isError) return <div className="container-page py-16"><ErrorState error={trip.error} /></div>;
  const r = trip.data!;
  return (
    <div className="container-page max-w-5xl py-10">
      <PageHeader title={r.propertyTitle} subtitle={`${r.city}, ${r.country}`} actions={<StatusBadge status={r.status} />} />
      <div className="grid gap-10 lg:grid-cols-[1fr_380px]">
        <div className="space-y-6">
          <div className="card grid grid-cols-2 divide-x divide-slate-100">
            <div className="p-5"><p className="text-sm text-slate-500">Check-in</p><p className="text-lg font-semibold">{prettyDate(r.checkIn)}</p></div>
            <div className="p-5"><p className="text-sm text-slate-500">Checkout</p><p className="text-lg font-semibold">{prettyDate(r.checkOut)}</p></div>
          </div>
          <div className="card space-y-3 p-5">
            <p className="flex items-center gap-2"><CalendarCheck2 className="h-5 w-5 text-brand-700" /> Booked {prettyDate(r.createdAt)} · Host: {r.hostName}</p>
            {r.status === 'AwaitingApproval' && <p className="rounded-xl bg-amber-50 p-3 text-sm text-amber-900">Waiting for {r.hostName} to respond{r.approvalDeadline ? ` (by ${dateTime(r.approvalDeadline)})` : ''}. Your card is authorized but not charged.</p>}
            {r.status === 'Declined' && <p className="rounded-xl bg-slate-50 p-3 text-sm text-slate-700">This request wasn't accepted{r.declineReason ? `: “${r.declineReason}”` : '.'} You were not charged.</p>}
            {r.refundAmount > 0 && <p className="text-sm text-emerald-700">Refund: {money(r.refundAmount, r.currency)} ({r.status === 'Refunded' ? 'processed' : 'processing'})</p>}
            <div className="flex flex-wrap gap-3 pt-2">
              <Link to={`/property/${r.propertyId}`} className="btn-secondary">View listing</Link>
              <button className="btn-secondary" onClick={() => message.mutate()} disabled={message.isPending}><MessageSquare className="h-4 w-4" /> Message host</button>
              {r.canReview && <button className="btn-primary" onClick={() => setReviewOpen(true)}><Star className="h-4 w-4" /> Write a review</button>}
              {r.canCancel && <button className="btn-ghost text-red-700" onClick={() => setCancelOpen(true)}>{r.status === 'AwaitingApproval' ? 'Withdraw request' : 'Cancel reservation'}</button>}
              <Link to={`/support?reservationId=${r.id}`} className="btn-ghost">Get help</Link>
            </div>
          </div>
        </div>
        <aside><StaySummary r={r} /></aside>
      </div>

      <Modal open={cancelOpen} onClose={() => setCancelOpen(false)} title="Cancel reservation">
        {preview.isLoading ? <Skeleton className="h-20" /> : preview.data && (
          <>
            <p className="text-slate-700">{preview.data.explanation}</p>
            <p className="mt-3 text-lg">Refund: <strong>{money(preview.data.refundAmount, preview.data.currency)}</strong>{preview.data.nonRefundable > 0 && <span className="text-sm text-slate-500"> ({money(preview.data.nonRefundable, preview.data.currency)} non-refundable)</span>}</p>
            <form className="mt-4" onSubmit={(e) => { e.preventDefault(); cancel.mutate(String(new FormData(e.currentTarget).get('reason') ?? '')); }}>
              <label className="label" htmlFor="reason">Reason (optional)</label>
              <textarea id="reason" name="reason" className="input" maxLength={500} />
              <div className="mt-5 flex justify-end gap-3"><button type="button" className="btn-secondary" onClick={() => setCancelOpen(false)}>Keep reservation</button><button className="btn-danger" disabled={cancel.isPending}>Confirm cancellation</button></div>
            </form>
          </>
        )}
      </Modal>
      <ReviewModal open={reviewOpen} onClose={() => setReviewOpen(false)} reservationId={r.id} />
    </div>
  );
}

function ReviewModal({ open, onClose, reservationId }: { open: boolean; onClose: () => void; reservationId: string }) {
  const qc = useQueryClient();
  const categories = ['overall', 'cleanliness', 'accuracy', 'communication', 'location', 'checkIn', 'value'] as const;
  const [ratings, setRatings] = useState<Record<string, number>>(Object.fromEntries(categories.map((c) => [c, 5])));
  const [comment, setComment] = useState('');
  const submit = useMutation({
    mutationFn: () => api('/reviews', { method: 'POST', body: { reservationId, ...ratings, comment } }),
    onSuccess: () => { toast.success('Thanks for your review!'); onClose(); qc.invalidateQueries({ queryKey: ['reservation', reservationId] }); },
    onError: (e) => toast.error((e as Error).message),
  });
  return (
    <Modal open={open} onClose={onClose} title="How was your stay?">
      <div className="space-y-3">
        {categories.map((c) => (
          <div key={c} className="flex items-center justify-between">
            <span className="capitalize">{c === 'checkIn' ? 'Check-in' : c}</span>
            <div className="flex" role="radiogroup" aria-label={`${c} rating`}>
              {[1, 2, 3, 4, 5].map((n) => (
                <button key={n} type="button" role="radio" aria-checked={ratings[c] === n} aria-label={`${n} stars`} onClick={() => setRatings({ ...ratings, [c]: n })}>
                  <Star className={clsx('h-6 w-6', n <= ratings[c] ? 'fill-amber-400 text-amber-400' : 'text-slate-300')} />
                </button>
              ))}
            </div>
          </div>
        ))}
        <textarea className="input mt-2 min-h-28" placeholder="Share what you loved (min. 10 characters)" value={comment} onChange={(e) => setComment(e.target.value)} maxLength={2000} aria-label="Review" />
        <button className="btn-primary w-full" disabled={comment.trim().length < 10 || submit.isPending} onClick={() => submit.mutate()}>Submit review</button>
      </div>
    </Modal>
  );
}
