import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { clsx } from 'clsx';
import { CalendarClock, Clock, MapPin, Sparkles, Users } from 'lucide-react';
import { useState } from 'react';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router';
import { api, ApiError, toQuery } from '@/api/client';
import type { ExperienceBooking, ExperienceCard, ExperienceDetail } from '@/api/types';
import { EmptyState, ErrorState, PageHeader, SafeImg, Skeleton } from '@/components/ui';
import { useAuthStore } from '@/lib/auth-store';
import { dateTime, humanize, money } from '@/lib/format';
import { useSeo } from '@/lib/seo';
import { toast } from '@/lib/toast';

export const experienceCategories = ['Food', 'Culture', 'Outdoors', 'Wellness', 'Art', 'Nightlife'] as const;

const duration = (minutes: number) => (minutes % 60 === 0 ? `${minutes / 60} h` : minutes > 60 ? `${Math.floor(minutes / 60)} h ${minutes % 60} min` : `${minutes} min`);

export function ExperiencesPage() {
  const [params, setParams] = useSearchParams();
  const city = params.get('city') ?? '';
  const category = params.get('category') ?? '';
  const [cityInput, setCityInput] = useState(city);
  useSeo({ title: 'Experiences', description: 'Tours, classes and activities hosted by locals.' });
  const list = useQuery({
    queryKey: ['experiences', city, category],
    queryFn: () => api<ExperienceCard[]>(`/experiences${toQuery({ city: city || undefined, category: category || undefined })}`),
  });
  const set = (key: string, value: string) => { const next = new URLSearchParams(params); if (value) next.set(key, value); else next.delete(key); setParams(next); };

  return (
    <div className="container-page py-10">
      <PageHeader title="Experiences" subtitle="Tours, classes and activities hosted by locals." />
      <form className="mb-4 flex flex-wrap gap-2" onSubmit={(e) => { e.preventDefault(); set('city', cityInput.trim()); }}>
        <input className="input max-w-xs" placeholder="City" aria-label="City" value={cityInput} onChange={(e) => setCityInput(e.target.value)} />
        <button className="btn-secondary">Search</button>
      </form>
      <div className="mb-8 flex flex-wrap gap-2" role="group" aria-label="Category">
        <button className={clsx('chip', !category && 'chip-active')} onClick={() => set('category', '')}>All</button>
        {experienceCategories.map((c) => <button key={c} className={clsx('chip', category === c && 'chip-active')} aria-pressed={category === c} onClick={() => set('category', c)}>{c}</button>)}
      </div>
      {list.isLoading ? <div className="grid gap-6 sm:grid-cols-2 lg:grid-cols-3">{Array.from({ length: 6 }, (_, i) => <Skeleton key={i} className="h-72" />)}</div>
        : list.isError ? <ErrorState error={list.error} onRetry={() => list.refetch()} />
        : list.data!.length === 0 ? <EmptyState icon={<Sparkles className="h-7 w-7" />} title="No experiences found" description="Try another city or category." />
        : (
          <div className="grid gap-6 sm:grid-cols-2 lg:grid-cols-3">
            {list.data!.map((x) => (
              <Link key={x.id} to={`/experiences/${x.id}`} className="group block">
                <article>
                  <SafeImg src={x.imageUrl ?? undefined} alt={x.title} className="aspect-[4/3] w-full rounded-2xl object-cover transition group-hover:opacity-90" />
                  <p className="mt-3 text-xs font-semibold uppercase tracking-wide text-slate-500">{x.category} · {x.city}</p>
                  <h2 className="font-semibold">{x.title}</h2>
                  <p className="text-sm text-slate-600">{duration(x.durationMinutes)} · hosted by {x.hostName}</p>
                  <p className="mt-1 text-sm"><span className="font-semibold">From {money(x.pricePerGuest, x.currency)}</span> / guest</p>
                  {x.nextSlotAt && <p className="text-xs text-emerald-700">Next: {dateTime(x.nextSlotAt)}</p>}
                </article>
              </Link>
            ))}
          </div>
        )}
    </div>
  );
}

export function ExperiencePage() {
  const { id } = useParams();
  const navigate = useNavigate();
  const qc = useQueryClient();
  const user = useAuthStore((s) => s.user);
  const [slotId, setSlotId] = useState<string | null>(null);
  const [guests, setGuests] = useState(1);
  const [bookingKey, setBookingKey] = useState(() => crypto.randomUUID());
  const detail = useQuery({ queryKey: ['experience', id], queryFn: () => api<ExperienceDetail>(`/experiences/${id}`) });
  useSeo({ title: detail.data?.title ?? 'Experience', description: detail.data?.description.slice(0, 160), image: detail.data?.imageUrl ?? undefined });
  const book = useMutation({
    mutationFn: () => api<ExperienceBooking>(`/experiences/${id}/bookings`, { method: 'POST', idempotencyKey: bookingKey, body: { slotId, guests } }),
    onSuccess: (b) => {
      toast.success(`Booked for ${dateTime(b.startsAt)}. Pay ${money(b.total, b.currency)} to your host on the day.`);
      void qc.invalidateQueries({ queryKey: ['experience', id] });
      void qc.invalidateQueries({ queryKey: ['experience-bookings'] });
      navigate('/trips/experiences');
    },
    onError: (e) => {
      toast.error(e instanceof ApiError ? e.message : 'Could not book');
      setBookingKey(crypto.randomUUID());
      void qc.invalidateQueries({ queryKey: ['experience', id] });
    },
  });

  if (detail.isLoading) return <div className="container-page py-10"><Skeleton className="h-96" /></div>;
  if (detail.isError) return <div className="container-page py-10"><ErrorState error={detail.error} onRetry={() => detail.refetch()} /></div>;
  const x = detail.data!;
  const slot = x.slots.find((s) => s.id === slotId);
  const maxForSlot = Math.min(x.maxGuests, slot?.remaining ?? x.maxGuests);
  const isOwner = user?.id === x.hostId;

  return (
    <div className="container-page grid gap-10 py-10 lg:grid-cols-[1fr_380px]">
      <div>
        <p className="text-xs font-semibold uppercase tracking-wide text-slate-500">{x.category} · {x.city}</p>
        <h1 className="text-3xl font-semibold">{x.title}</h1>
        <p className="mt-2 flex flex-wrap gap-x-4 text-slate-600">
          <span className="inline-flex items-center gap-1"><Clock className="h-4 w-4" /> {duration(x.durationMinutes)}</span>
          <span className="inline-flex items-center gap-1"><Users className="h-4 w-4" /> Up to {x.maxGuests} guests</span>
          <span className="inline-flex items-center gap-1"><MapPin className="h-4 w-4" /> {x.meetingPoint}</span>
        </p>
        <SafeImg src={x.imageUrl ?? undefined} alt={x.title} className="mt-6 aspect-[16/9] w-full rounded-2xl object-cover" />
        <h2 className="mt-8 mb-2 text-xl font-semibold">What you'll do</h2>
        <p className="whitespace-pre-line text-slate-700">{x.description}</p>
        <p className="mt-6 text-sm text-slate-500">Hosted by {x.hostName}</p>
      </div>
      <aside className="card h-fit p-6 lg:sticky lg:top-24">
        <p className="text-xl"><span className="font-semibold">{money(x.pricePerGuest, x.currency)}</span> / guest</p>
        {x.status !== 'Published' && <p className="mt-2 rounded-lg bg-amber-50 p-2 text-sm text-amber-900">{humanize(x.status)}: only you can see this page.</p>}
        <h2 className="mt-5 mb-2 text-sm font-semibold">Choose a session</h2>
        {x.slots.length === 0 ? <p className="text-sm text-slate-500">No upcoming sessions.</p> : (
          <ul className="max-h-72 space-y-2 overflow-y-auto" role="radiogroup" aria-label="Sessions">
            {x.slots.map((s) => (
              <li key={s.id}>
                <button role="radio" aria-checked={slotId === s.id} disabled={s.remaining === 0}
                  className={clsx('flex w-full items-center justify-between rounded-xl border px-3 py-2 text-left text-sm disabled:opacity-50', slotId === s.id ? 'border-slate-900 bg-slate-50' : 'border-slate-200')}
                  onClick={() => { setSlotId(s.id); setGuests((g) => Math.min(g, s.remaining)); }}>
                  <span className="inline-flex items-center gap-2"><CalendarClock className="h-4 w-4" /> {dateTime(s.startsAt)}</span>
                  <span className="text-xs text-slate-500">{s.remaining === 0 ? 'Full' : `${s.remaining} left`}</span>
                </button>
              </li>
            ))}
          </ul>
        )}
        <label className="mt-4 block text-sm font-medium">Guests
          <select className="input mt-1" value={guests} onChange={(e) => setGuests(Number(e.target.value))}>
            {Array.from({ length: maxForSlot }, (_, i) => i + 1).map((n) => <option key={n} value={n}>{n}</option>)}
          </select>
        </label>
        {slot && <p className="mt-4 flex justify-between text-sm"><span>{money(x.pricePerGuest, x.currency)} × {guests}</span><span className="font-semibold">{money(x.pricePerGuest * guests, x.currency)}</span></p>}
        {!user ? <Link className="btn-primary mt-4 w-full justify-center" to={`/login?returnTo=/experiences/${x.id}`}>Log in to book</Link>
          : <button className="btn-primary mt-4 w-full justify-center" disabled={!slot || isOwner || book.isPending} onClick={() => book.mutate()}>
            {isOwner ? 'This is your experience' : book.isPending ? 'Booking…' : 'Book'}</button>}
        <p className="mt-3 text-xs text-slate-500">You pay the host at the experience. Free cancellation up to 24 hours before.</p>
      </aside>
    </div>
  );
}

export function MyExperienceBookings() {
  const qc = useQueryClient();
  const list = useQuery({ queryKey: ['experience-bookings'], queryFn: () => api<ExperienceBooking[]>('/experience-bookings') });
  const cancel = useMutation({
    mutationFn: (id: string) => api<ExperienceBooking>(`/experience-bookings/${id}/cancel`, { method: 'POST', body: {} }),
    onSuccess: () => { toast.success('Booking cancelled'); void qc.invalidateQueries({ queryKey: ['experience-bookings'] }); },
    onError: (e) => toast.error((e as Error).message),
  });
  return (
    <div className="container-page py-10">
      <PageHeader title="Experience bookings" actions={<Link className="btn-ghost underline" to="/trips">Stays</Link>} />
      {list.isLoading ? <Skeleton className="h-40" /> : list.isError ? <ErrorState error={list.error} onRetry={() => list.refetch()} />
        : list.data!.length === 0 ? <EmptyState icon={<Sparkles className="h-7 w-7" />} title="No experiences booked" action={<Link className="btn-primary" to="/experiences">Find an experience</Link>} />
        : (
          <ul className="divide-y divide-slate-100 rounded-2xl border border-slate-200">
            {list.data!.map((b) => (
              <li key={b.id} className="flex flex-wrap items-center justify-between gap-3 p-4">
                <div>
                  <Link to={`/experiences/${b.experienceId}`} className="font-semibold hover:underline">{b.experienceTitle}</Link>
                  <p className="text-sm text-slate-600">{dateTime(b.startsAt)} · {b.guests} guest{b.guests === 1 ? '' : 's'} · {money(b.total, b.currency)} · meet at {b.meetingPoint}</p>
                </div>
                {b.status === 'Cancelled' ? <span className="text-sm text-slate-500">Cancelled</span>
                  : b.canCancel && <button className="btn-ghost text-red-700" disabled={cancel.isPending} onClick={() => confirm('Cancel this booking?') && cancel.mutate(b.id)}>Cancel</button>}
              </li>
            ))}
          </ul>
        )}
    </div>
  );
}
