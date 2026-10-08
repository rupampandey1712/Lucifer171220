import { useMutation, useQuery } from '@tanstack/react-query';
import { clsx } from 'clsx';
import { Bath, BedDouble, CalendarX2, Clock, CloudSun, DoorOpen, Heart, MessageSquare, PawPrint, Share, ShieldCheck, Users, Zap } from 'lucide-react';
import { lazy, Suspense, useEffect, useMemo, useState } from 'react';
import { useNavigate, useParams, useSearchParams } from 'react-router';
import { api, ApiError, newIdempotencyKey, toQuery } from '@/api/client';
import type { AvailabilityDto, Paged, PropertyCard as Card, PropertyDetail, QuoteDto, RatingSummary, ReservationDto, ReviewDto, WeatherResult } from '@/api/types';
import { AvailabilityCalendar } from '@/components/AvailabilityCalendar';
import { PriceBreakdown } from '@/components/PriceBreakdown';
import { PropertyCard } from '@/components/PropertyCard';
import { GuestSelector } from '@/components/SearchBar';
import { ImageGallery } from '@/components/ImageGallery';
import { ErrorState, Modal, PageLoader, Rating, Skeleton, Spinner } from '@/components/ui';
import { useAuthStore } from '@/lib/auth-store';
import { humanize, money, prettyDate, shortDate } from '@/lib/format';
import { useFavorites } from '@/lib/hooks';
import { useSeo } from '@/lib/seo';
import { toast } from '@/lib/toast';

const MapView = lazy(() => import('@/components/MapView').then((m) => ({ default: m.MapView })));

const POLICY_TEXT: Record<string, string> = {
  Flexible: 'Full refund up to 24 hours before check-in.',
  Moderate: 'Full refund up to 5 days before check-in; 50% of the accommodation after that.',
  Strict: 'Full refund up to 14 days before check-in; 50% up to 7 days before; non-refundable after.',
};

export function PropertyPage() {
  const { id = '' } = useParams();
  const [params, setParams] = useSearchParams();
  const property = useQuery({ queryKey: ['property', id], queryFn: () => api<PropertyDetail>(`/properties/${id}`) });

  if (property.isLoading) return <PageLoader />;
  if (property.isError) return <div className="container-page py-16"><ErrorState error={property.error} onRetry={() => property.refetch()} /></div>;
  return <PropertyView p={property.data!} params={params} setParams={setParams} />;
}

function PropertyView({ p, params, setParams }: { p: PropertyDetail; params: URLSearchParams; setParams: (p: URLSearchParams) => void }) {
  const { isSaved, toggle } = useFavorites();
  const [messageOpen, setMessageOpen] = useState(false);
  const checkIn = params.get('checkIn') ?? '';
  const checkOut = params.get('checkOut') ?? '';
  const setDates = (ci: string, co: string) => {
    const next = new URLSearchParams(params);
    if (ci) next.set('checkIn', ci); else next.delete('checkIn');
    if (co) next.set('checkOut', co); else next.delete('checkOut');
    setParams(next);
  };

  const jsonLd = useMemo(() => ({
    '@context': 'https://schema.org', '@type': 'LodgingBusiness', name: p.title, description: p.description.slice(0, 300), image: p.images.map((i) => i.url),
    address: p.address && { '@type': 'PostalAddress', addressLocality: p.address.city, addressCountry: p.address.countryCode },
    aggregateRating: p.reviewCount ? { '@type': 'AggregateRating', ratingValue: p.ratingAverage, reviewCount: p.reviewCount } : undefined,
  }), [p]);
  useSeo({ title: p.title, description: p.description.slice(0, 160), image: p.images[0]?.url, jsonLd });

  const share = async () => {
    if (navigator.share) await navigator.share({ title: p.title, url: location.href }).catch(() => undefined);
    else { await navigator.clipboard.writeText(location.href); toast.success('Link copied to clipboard'); }
  };

  return (
    <div className="container-page py-6">
      <div className="mb-4 flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 className="text-2xl font-bold sm:text-3xl">{p.title}</h1>
          <p className="mt-1 flex flex-wrap items-center gap-2 text-sm text-slate-700">
            <Rating value={p.ratingAverage} count={p.reviewCount} /> · <span className="font-medium underline">{p.address?.city}, {p.address?.country}</span>
            {p.status !== 'Published' && <span className="rounded-full bg-amber-100 px-2 py-0.5 text-xs font-semibold text-amber-800">{p.status} — only you can see this</span>}
          </p>
        </div>
        <div className="flex gap-2">
          <button className="btn-ghost" onClick={share}><Share className="h-4 w-4" /> Share</button>
          <button className="btn-ghost" onClick={() => toggle(p.id)} aria-pressed={isSaved(p.id)}><Heart className={clsx('h-4 w-4', isSaved(p.id) && 'fill-coral-500 text-coral-500')} /> {isSaved(p.id) ? 'Saved' : 'Save'}</button>
        </div>
      </div>

      <ImageGallery images={p.images} title={p.title} />

      <div className="mt-10 grid gap-12 lg:grid-cols-[1fr_380px]">
        <div className="min-w-0">
          <section className="flex items-center justify-between border-b border-slate-200 pb-6">
            <div>
              <h2 className="text-xl font-semibold">{humanize(p.roomType)} {humanize(p.propertyType).toLowerCase()} hosted by {p.host.displayName}</h2>
              <p className="mt-1 flex flex-wrap gap-x-4 text-slate-600">
                <span className="inline-flex items-center gap-1"><Users className="h-4 w-4" /> {p.maxGuests} guests</span>
                <span className="inline-flex items-center gap-1"><DoorOpen className="h-4 w-4" /> {p.bedrooms} bedrooms</span>
                <span className="inline-flex items-center gap-1"><BedDouble className="h-4 w-4" /> {p.beds} beds</span>
                <span className="inline-flex items-center gap-1"><Bath className="h-4 w-4" /> {p.bathrooms} baths</span>
              </p>
            </div>
            {p.host.avatarUrl ? <img src={p.host.avatarUrl} alt="" className="h-14 w-14 rounded-full object-cover" />
              : <span className="flex h-14 w-14 items-center justify-center rounded-full bg-slate-800 text-xl font-semibold text-white">{p.host.displayName[0]}</span>}
          </section>

          <section className="space-y-5 border-b border-slate-200 py-6">
            {p.instantBook && <Feature icon={<Zap className="h-6 w-6" />} title="Instant book" text="Book without waiting for host approval." />}
            <Feature icon={<ShieldCheck className="h-6 w-6" />} title={`${p.host.displayName} hosts ${p.host.listingCount} place${p.host.listingCount === 1 ? '' : 's'}`} text={`Joined ${prettyDate(p.host.joinedAt)}${p.host.rating ? ` · ${p.host.rating.toFixed(2)} average rating` : ''}`} />
            <Feature icon={<CalendarX2 className="h-6 w-6" />} title={`${p.cancellationPolicy} cancellation`} text={POLICY_TEXT[p.cancellationPolicy]} />
          </section>

          <section className="border-b border-slate-200 py-6">
            <h2 className="mb-3 text-xl font-semibold">About this place</h2>
            <p className="whitespace-pre-line leading-relaxed text-slate-700">{p.description}</p>
          </section>

          <section className="border-b border-slate-200 py-6">
            <h2 className="mb-4 text-xl font-semibold">What this place offers</h2>
            <ul className="grid gap-3 sm:grid-cols-2">
              {p.amenities.map((a) => <li key={a.code} className="flex items-center gap-3 text-slate-700"><span className="h-2 w-2 rounded-full bg-brand-600" />{a.name}</li>)}
            </ul>
          </section>

          <section className="border-b border-slate-200 py-6">
            <h2 className="mb-1 text-xl font-semibold">{checkIn && checkOut ? `${shortDate(checkIn)} – ${shortDate(checkOut)}` : 'Select check-in date'}</h2>
            <p className="mb-4 text-sm text-slate-600">{p.minNights > 1 ? `Minimum stay: ${p.minNights} nights` : 'Add your travel dates for exact pricing'}</p>
            <Availability propertyId={p.id} checkIn={checkIn} checkOut={checkOut} onChange={setDates} />
          </section>

          <Reviews propertyId={p.id} />

          <section className="border-b border-slate-200 py-6">
            <h2 className="mb-4 text-xl font-semibold">House rules</h2>
            <div className="grid gap-3 text-slate-700 sm:grid-cols-2">
              <p className="flex items-center gap-2"><Clock className="h-4 w-4" /> Check-in after {p.checkInTime}</p>
              <p className="flex items-center gap-2"><Clock className="h-4 w-4" /> Checkout before {p.checkOutTime}</p>
              <p className="flex items-center gap-2"><PawPrint className="h-4 w-4" /> {p.petsAllowed ? 'Pets allowed' : 'No pets'}</p>
              <p>{p.smokingAllowed ? 'Smoking allowed' : 'No smoking'} · {p.eventsAllowed ? 'Events allowed' : 'No parties or events'}</p>
            </div>
            {p.houseRules && <p className="mt-3 text-sm text-slate-600">{p.houseRules}</p>}
          </section>

          {p.latitude != null && (
            <section className="border-b border-slate-200 py-6">
              <h2 className="mb-1 text-xl font-semibold">Where you'll be</h2>
              <p className="mb-4 text-sm text-slate-600">{p.address?.city}, {p.address?.region ? `${p.address.region}, ` : ''}{p.address?.country}. The exact address is shared after booking.</p>
              <div className="h-80"><Suspense fallback={<Skeleton className="h-full w-full" />}><MapView single points={[{ id: p.id, lat: p.latitude, lng: p.longitude!, title: p.title }]} /></Suspense></div>
              <Weather propertyId={p.id} />
            </section>
          )}

          <section className="py-6">
            <button className="btn-secondary" onClick={() => setMessageOpen(true)}><MessageSquare className="h-4 w-4" /> Message the host</button>
          </section>
        </div>

        <aside className="lg:sticky lg:top-24 lg:self-start">
          <BookingWidget p={p} checkIn={checkIn} checkOut={checkOut} onDates={setDates} />
        </aside>
      </div>

      <Similar propertyId={p.id} />
      <MessageHostModal open={messageOpen} onClose={() => setMessageOpen(false)} propertyId={p.id} hostName={p.host.displayName} />
    </div>
  );
}

function Feature({ icon, title, text }: { icon: React.ReactNode; title: string; text?: string }) {
  return <div className="flex gap-4"><span className="text-slate-800">{icon}</span><div><p className="font-semibold">{title}</p>{text && <p className="text-sm text-slate-600">{text}</p>}</div></div>;
}

function Availability({ propertyId, checkIn, checkOut, onChange }: { propertyId: string; checkIn: string; checkOut: string; onChange: (a: string, b: string) => void }) {
  const availability = useQuery({ queryKey: ['availability', propertyId], queryFn: () => api<AvailabilityDto>(`/properties/${propertyId}/availability`) });
  if (availability.isLoading) return <Skeleton className="h-72 w-full" />;
  if (availability.isError) return <ErrorState error={availability.error} />;
  return <AvailabilityCalendar unavailable={availability.data!.unavailableDates} checkIn={checkIn} checkOut={checkOut} onChange={onChange} />;
}

function BookingWidget({ p, checkIn, checkOut, onDates }: { p: PropertyDetail; checkIn: string; checkOut: string; onDates: (a: string, b: string) => void }) {
  const [params] = useSearchParams();
  const [guests, setGuests] = useState(Math.min(Number(params.get('guests') ?? 1), p.maxGuests));
  const [coupon, setCoupon] = useState('');
  const [appliedCoupon, setAppliedCoupon] = useState<string | null>(null);
  const user = useAuthStore((s) => s.user);
  const navigate = useNavigate();
  const ready = !!checkIn && !!checkOut;

  const quote = useQuery({
    queryKey: ['quote', p.id, checkIn, checkOut, guests, appliedCoupon],
    queryFn: () => api<QuoteDto>(`/properties/${p.id}/quote`, { method: 'POST', body: { checkIn, checkOut, guests, couponCode: appliedCoupon } }),
    enabled: ready,
    retry: false,
  });

  const [idempotencyKey] = useState(newIdempotencyKey);
  const reserve = useMutation({
    mutationFn: () => api<ReservationDto>('/reservations', { method: 'POST', idempotencyKey, body: { propertyId: p.id, checkIn, checkOut, guests, quoteToken: quote.data!.quoteToken, couponCode: appliedCoupon } }),
    onSuccess: (r) => navigate(`/book/${r.id}`),
    onError: (e) => {
      toast.error(e instanceof ApiError ? e.message : 'Could not reserve. Please try again.');
      quote.refetch();
    },
  });

  const onReserve = () => {
    if (!user) {
      navigate(`/login?returnTo=${encodeURIComponent(location.pathname + location.search)}`);
      return;
    }
    reserve.mutate();
  };

  return (
    <div className="card p-6">
      <p className="mb-4"><span className="text-2xl font-bold">{money(quote.data?.averageNightly ?? p.basePrice, p.currency)}</span> <span className="text-slate-600">night</span></p>
      <div className="overflow-hidden rounded-xl border border-slate-300">
        <div className="grid grid-cols-2 divide-x divide-slate-300">
          <label className="p-3"><span className="block text-[10px] font-bold uppercase">Check-in</span><input type="date" className="w-full text-sm outline-none" value={checkIn} onChange={(e) => onDates(e.target.value, checkOut > e.target.value ? checkOut : '')} /></label>
          <label className="p-3"><span className="block text-[10px] font-bold uppercase">Checkout</span><input type="date" className="w-full text-sm outline-none" min={checkIn} value={checkOut} onChange={(e) => onDates(checkIn, e.target.value)} /></label>
        </div>
        <div className="flex items-center justify-between border-t border-slate-300 p-3">
          <span><span className="block text-[10px] font-bold uppercase">Guests</span><span className="text-sm">{guests} guest{guests === 1 ? '' : 's'}</span></span>
          <GuestSelector value={guests} onChange={setGuests} max={p.maxGuests} />
        </div>
      </div>

      {ready && quote.isError && <p role="alert" className="mt-3 rounded-xl bg-red-50 px-3 py-2 text-sm text-red-800">{(quote.error as Error).message}</p>}
      {ready && quote.data && !quote.data.available && <p role="alert" className="mt-3 rounded-xl bg-amber-50 px-3 py-2 text-sm text-amber-900">Those dates aren't available. Please pick different dates.</p>}

      <button className="btn-accent mt-4 w-full !py-3 text-base" disabled={!ready || !quote.data?.available || reserve.isPending || quote.isFetching} onClick={onReserve}>
        {reserve.isPending && <Spinner className="h-4 w-4" />} {ready ? 'Reserve' : 'Check availability'}
      </button>
      {ready && <p className="mt-2 text-center text-sm text-slate-500">You won't be charged yet — dates are held for 10 minutes</p>}

      {ready && quote.isLoading && <div className="mt-4 space-y-2"><Skeleton className="h-4 w-full" /><Skeleton className="h-4 w-4/5" /><Skeleton className="h-4 w-3/5" /></div>}
      {quote.data && <div className="mt-5"><PriceBreakdown quote={quote.data} /></div>}

      {ready && (
        <form className="mt-4 flex gap-2" onSubmit={(e) => { e.preventDefault(); setAppliedCoupon(coupon.trim() || null); }}>
          <input className="input !py-2" placeholder="Coupon code (try WELCOME10)" value={coupon} onChange={(e) => setCoupon(e.target.value)} aria-label="Coupon code" />
          <button className="btn-secondary !py-2">Apply</button>
        </form>
      )}
    </div>
  );
}

function Reviews({ propertyId }: { propertyId: string }) {
  const [page, setPage] = useState(1);
  const summary = useQuery({ queryKey: ['reviews', propertyId, 'summary'], queryFn: () => api<RatingSummary>(`/properties/${propertyId}/reviews/summary`) });
  const reviews = useQuery({ queryKey: ['reviews', propertyId, page], queryFn: () => api<Paged<ReviewDto>>(`/properties/${propertyId}/reviews${toQuery({ page, pageSize: 6 })}`) });
  const s = summary.data;
  return (
    <section className="border-b border-slate-200 py-6" aria-labelledby="reviews">
      <h2 id="reviews" className="mb-4 flex items-center gap-2 text-xl font-semibold">{s && s.count > 0 ? <><Rating value={s.overall} size="lg" /> · {s.count} reviews</> : 'No reviews yet'}</h2>
      {s && s.count > 0 && (
        <div className="mb-6 grid gap-x-10 gap-y-2 sm:grid-cols-2">
          {([['Cleanliness', s.cleanliness], ['Accuracy', s.accuracy], ['Communication', s.communication], ['Location', s.location], ['Check-in', s.checkIn], ['Value', s.value]] as const).map(([l, v]) => (
            <div key={l} className="flex items-center justify-between gap-4 text-sm"><span>{l}</span><span className="flex items-center gap-2"><span className="h-1 w-28 rounded bg-slate-200"><span className="block h-1 rounded bg-slate-900" style={{ width: `${(v / 5) * 100}%` }} /></span>{v.toFixed(1)}</span></div>
          ))}
        </div>
      )}
      <div className="grid gap-8 sm:grid-cols-2">
        {reviews.data?.items.map((r) => (
          <article key={r.id}>
            <div className="flex items-center gap-3">
              <span className="flex h-10 w-10 items-center justify-center rounded-full bg-slate-200 font-semibold">{r.guestName[0]}</span>
              <div><p className="font-semibold">{r.guestName}</p><p className="text-xs text-slate-500">{prettyDate(r.createdAt)} · {'★'.repeat(r.overall)}</p></div>
            </div>
            <p className="mt-3 text-sm leading-relaxed text-slate-700">{r.comment}</p>
            {r.hostResponse && <p className="mt-2 rounded-xl bg-slate-50 p-3 text-sm text-slate-600"><span className="font-semibold">Host response: </span>{r.hostResponse}</p>}
          </article>
        ))}
      </div>
      {reviews.data && reviews.data.totalCount > reviews.data.items.length * page && <button className="btn-secondary mt-6" onClick={() => setPage((x) => x + 1)}>Show more reviews</button>}
    </section>
  );
}

function Weather({ propertyId }: { propertyId: string }) {
  const weather = useQuery({ queryKey: ['weather', propertyId], queryFn: () => api<WeatherResult | undefined>(`/properties/${propertyId}/weather`), staleTime: 30 * 60_000, retry: false });
  // Graceful degradation: no weather → no widget; the page still works.
  if (!weather.data?.daily?.length) return null;
  return (
    <div className="mt-6 rounded-2xl bg-sky-50 p-4">
      <p className="mb-3 flex items-center gap-2 text-sm font-semibold"><CloudSun className="h-4 w-4" /> 7-day forecast {weather.data.currentTempC != null && <span className="font-normal text-slate-600">· now {Math.round(weather.data.currentTempC)}°C, {weather.data.currentSummary}</span>}</p>
      <div className="grid grid-cols-7 gap-2 text-center text-xs">
        {weather.data.daily.map((d) => <div key={d.date}><p className="font-medium">{shortDate(d.date).split(' ')[0]}</p><p className="text-slate-500" title={d.summary}>{Math.round(d.maxC)}° / {Math.round(d.minC)}°</p></div>)}
      </div>
    </div>
  );
}

function Similar({ propertyId }: { propertyId: string }) {
  const similar = useQuery({ queryKey: ['similar', propertyId], queryFn: () => api<Card[]>(`/properties/${propertyId}/similar`) });
  if (!similar.data?.length) return null;
  return (
    <section className="mt-12" aria-labelledby="similar">
      <h2 id="similar" className="mb-5 text-xl font-semibold">Similar stays nearby</h2>
      <div className="grid gap-6 sm:grid-cols-2 lg:grid-cols-3">{similar.data.slice(0, 3).map((p) => <PropertyCard key={p.id} property={p} />)}</div>
    </section>
  );
}

function MessageHostModal({ open, onClose, propertyId, hostName }: { open: boolean; onClose: () => void; propertyId: string; hostName: string }) {
  const user = useAuthStore((s) => s.user);
  const navigate = useNavigate();
  const [text, setText] = useState('');
  useEffect(() => { if (open && !user) navigate(`/login?returnTo=${encodeURIComponent(location.pathname)}`); }, [open, user, navigate]);
  const send = useMutation({
    mutationFn: () => api<{ id: string }>('/conversations', { method: 'POST', body: { propertyId, message: text } }),
    onSuccess: (c) => { toast.success('Message sent'); navigate(`/messages/${c.id}`); },
    onError: (e) => toast.error((e as Error).message),
  });
  return (
    <Modal open={open && !!user} onClose={onClose} title={`Message ${hostName}`}>
      <textarea className="input min-h-32" placeholder="Hi! I have a question about…" value={text} onChange={(e) => setText(e.target.value)} maxLength={4000} aria-label="Message" />
      <button className="btn-primary mt-4 w-full" disabled={!text.trim() || send.isPending} onClick={() => send.mutate()}>Send message</button>
    </Modal>
  );
}

