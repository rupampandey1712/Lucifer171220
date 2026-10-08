import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { clsx } from 'clsx';
import { Check, ImagePlus, Loader2, MapPin, Search, Trash2 } from 'lucide-react';
import { lazy, Suspense, useEffect, useRef, useState } from 'react';
import { useNavigate, useParams } from 'react-router';
import { api, ApiError, toQuery } from '@/api/client';
import type { AmenityDto, GeocodeResult, ImageDto, PropertyDetail, UpdatePropertyRequest } from '@/api/types';
import { GuestSelector } from '@/components/SearchBar';
import { ErrorState, PageLoader, Skeleton, SafeImg } from '@/components/ui';
import { humanize, money } from '@/lib/format';
import { toast } from '@/lib/toast';
import { HostCalendar } from './HostCalendar';

const MapView = lazy(() => import('@/components/MapView').then((m) => ({ default: m.MapView })));

const STEPS = ['Property type', 'Location', 'Basics', 'Rooms', 'Amenities', 'Photos', 'Description', 'House rules', 'Pricing', 'Availability', 'Preview', 'Publish'] as const;
const TYPES = ['Apartment', 'House', 'Villa', 'Cabin', 'Cottage', 'GuestHouse', 'Hotel', 'Hostel', 'BedAndBreakfast', 'Resort', 'FarmStay', 'UniqueStay'];
const ROOMS = ['EntirePlace', 'PrivateRoom', 'SharedRoom', 'HotelRoom'];

/** New listing: pick a type, create a draft, then continue in the editor (every later step autosaves). */
export function NewListingPage() {
  const navigate = useNavigate();
  const [type, setType] = useState('Apartment');
  const [room, setRoom] = useState('EntirePlace');
  const create = useMutation({
    mutationFn: () => api<PropertyDetail>('/properties', { method: 'POST', body: { propertyType: type, roomType: room } }),
    onSuccess: (p) => navigate(`/host/properties/${p.id}/edit?step=1`, { replace: true }),
    onError: (e) => toast.error((e as Error).message),
  });
  return (
    <WizardFrame step={0} onNext={() => create.mutate()} nextLabel="Create draft" busy={create.isPending}>
      <TypeStep type={type} room={room} onChange={(t, r) => { setType(t); setRoom(r); }} />
    </WizardFrame>
  );
}

export function EditListingPage() {
  const { id = '' } = useParams();
  const qc = useQueryClient();
  const navigate = useNavigate();
  const initialStep = Number(new URLSearchParams(location.search).get('step') ?? 0);
  const [step, setStep] = useState(Math.min(Math.max(initialStep, 0), STEPS.length - 1));
  const property = useQuery({ queryKey: ['property', id], queryFn: () => api<PropertyDetail>(`/properties/${id}`) });
  const [draft, setDraft] = useState<UpdatePropertyRequest>({});
  const [saveState, setSaveState] = useState<'idle' | 'saving' | 'saved' | 'error'>('idle');
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null);

  const save = useMutation({
    mutationFn: (body: UpdatePropertyRequest) => api<PropertyDetail>(`/properties/${id}`, { method: 'PATCH', body }),
    onMutate: () => setSaveState('saving'),
    onSuccess: (p) => { qc.setQueryData(['property', id], p); setSaveState('saved'); },
    onError: (e) => { setSaveState('error'); toast.error(e instanceof ApiError ? e.message : 'Autosave failed'); },
  });

  /** Debounced autosave of the draft (wizard steps never lose work). */
  const patch = (changes: UpdatePropertyRequest) => {
    const next = { ...draft, ...changes };
    setDraft(next);
    if (timer.current) clearTimeout(timer.current);
    timer.current = setTimeout(() => { save.mutate(next); setDraft({}); }, 700);
  };
  const flush = async () => {
    if (timer.current) clearTimeout(timer.current);
    if (Object.keys(draft).length) { await save.mutateAsync(draft); setDraft({}); }
  };
  useEffect(() => () => { if (timer.current) clearTimeout(timer.current); }, []);

  const publish = useMutation({
    mutationFn: async () => { await flush(); return api(`/properties/${id}/publish`, { method: 'POST' }); },
    onSuccess: () => { toast.success('Your listing is live! 🎉'); qc.invalidateQueries({ queryKey: ['host'] }); navigate(`/property/${id}`); },
    onError: (e) => toast.error((e as Error).message),
  });

  if (property.isLoading) return <PageLoader />;
  if (property.isError) return <ErrorState error={property.error} />;
  const p = property.data!;
  const go = async (s: number) => { await flush(); setStep(s); window.scrollTo(0, 0); };

  return (
    <WizardFrame step={step} onBack={step > 0 ? () => go(step - 1) : undefined} onNext={step < STEPS.length - 1 ? () => go(step + 1) : () => publish.mutate()}
      nextLabel={step === STEPS.length - 1 ? (p.status === 'Published' ? 'Done' : 'Publish listing') : 'Next'} busy={publish.isPending} saveState={saveState} onJump={go}>
      {step === 0 && <TypeStep type={p.propertyType} room={p.roomType} onChange={(t, r) => patch({ propertyType: t, roomType: r })} />}
      {step === 1 && <LocationStep p={p} onChange={patch} />}
      {step === 2 && <BasicsStep p={p} onChange={patch} />}
      {step === 3 && <RoomsStep p={p} onChange={patch} />}
      {step === 4 && <AmenitiesStep p={p} onChange={patch} />}
      {step === 5 && <PhotosStep p={p} />}
      {step === 6 && <DescriptionStep p={p} onChange={patch} />}
      {step === 7 && <RulesStep p={p} onChange={patch} />}
      {step === 8 && <PricingStep p={p} onChange={patch} />}
      {step === 9 && <HostCalendar propertyId={p.id} embedded />}
      {step === 10 && <PreviewStep p={p} />}
      {step === 11 && <PublishStep p={p} />}
    </WizardFrame>
  );
}

function WizardFrame({ step, children, onNext, onBack, nextLabel, busy, saveState, onJump }: {
  step: number; children: React.ReactNode; onNext: () => void; onBack?: () => void; nextLabel: string; busy?: boolean; saveState?: string; onJump?: (s: number) => void;
}) {
  return (
    <div className="mx-auto max-w-3xl">
      <div className="mb-2 flex items-center justify-between text-sm text-slate-500">
        <span>Step {step + 1} of {STEPS.length} · {STEPS[step]}</span>
        <span aria-live="polite">{saveState === 'saving' ? <span className="flex items-center gap-1"><Loader2 className="h-3 w-3 animate-spin" /> Saving…</span> : saveState === 'saved' ? <span className="flex items-center gap-1 text-emerald-700"><Check className="h-3 w-3" /> Draft saved</span> : saveState === 'error' ? <span className="text-red-600">Not saved</span> : null}</span>
      </div>
      <div className="mb-8 flex gap-1" role="progressbar" aria-valuemin={1} aria-valuemax={STEPS.length} aria-valuenow={step + 1}>
        {STEPS.map((s, i) => <button key={s} type="button" title={s} disabled={!onJump} onClick={() => onJump?.(i)} className={clsx('h-1.5 flex-1 rounded-full transition', i <= step ? 'bg-slate-900' : 'bg-slate-200')} aria-label={`Go to ${s}`} />)}
      </div>
      <div className="min-h-[50vh] animate-fade-in">{children}</div>
      <div className="sticky bottom-0 mt-10 flex justify-between border-t border-slate-200 bg-white py-4">
        {onBack ? <button className="btn-ghost underline" onClick={onBack}>Back</button> : <span />}
        <button className="btn-primary !px-6" onClick={onNext} disabled={busy}>{busy && <Loader2 className="h-4 w-4 animate-spin" />} {nextLabel}</button>
      </div>
    </div>
  );
}

function H({ title, sub }: { title: string; sub?: string }) {
  return <div className="mb-6"><h1 className="text-2xl font-bold sm:text-3xl">{title}</h1>{sub && <p className="mt-1 text-slate-600">{sub}</p>}</div>;
}

function TypeStep({ type, room, onChange }: { type: string; room: string; onChange: (t: string, r: string) => void }) {
  return (
    <>
      <H title="Which of these best describes your place?" />
      <div className="grid grid-cols-2 gap-3 sm:grid-cols-3">{TYPES.map((t) => <button key={t} type="button" onClick={() => onChange(t, room)} aria-pressed={t === type} className={clsx('rounded-2xl border-2 p-4 text-left font-semibold transition', t === type ? 'border-slate-900 bg-slate-50' : 'border-slate-200 hover:border-slate-400')}>{humanize(t)}</button>)}</div>
      <h2 className="mt-8 mb-3 font-semibold">What type of place will guests have?</h2>
      <div className="space-y-2">{ROOMS.map((r) => <button key={r} type="button" onClick={() => onChange(type, r)} aria-pressed={r === room} className={clsx('w-full rounded-2xl border-2 p-4 text-left', r === room ? 'border-slate-900 bg-slate-50' : 'border-slate-200 hover:border-slate-400')}><span className="font-semibold">{humanize(r)}</span></button>)}</div>
    </>
  );
}

function LocationStep({ p, onChange }: { p: PropertyDetail; onChange: (c: UpdatePropertyRequest) => void }) {
  const [q, setQ] = useState('');
  const [addr, setAddr] = useState(p.address ?? { line1: '', line2: null, city: '', region: null, postalCode: null, countryCode: '', country: '' });
  const [coords, setCoords] = useState<{ lat: number; lng: number } | null>(p.latitude != null ? { lat: p.latitude, lng: p.longitude! } : null);
  const results = useQuery({ queryKey: ['geocode', q], queryFn: () => api<GeocodeResult[]>(`/geo/geocode${toQuery({ q })}`), enabled: q.length >= 3 });
  const commit = (a = addr, c = coords) => { if (a.line1 && a.city && a.countryCode.length === 2 && c) onChange({ address: a, latitude: c.lat, longitude: c.lng }); };
  const pick = (r: GeocodeResult) => {
    const a = { ...addr, city: r.city ?? addr.city, country: r.country ?? addr.country, countryCode: r.countryCode ?? addr.countryCode };
    const c = { lat: r.latitude, lng: r.longitude };
    setAddr(a); setCoords(c); setQ(''); commit(a, c);
  };
  const field = (key: 'line1' | 'city' | 'region' | 'postalCode' | 'country' | 'countryCode', label: string) => (
    <div><label className="label" htmlFor={key}>{label}</label><input id={key} className="input" value={addr[key] ?? ''} maxLength={key === 'countryCode' ? 2 : 200}
      onChange={(e) => setAddr({ ...addr, [key]: key === 'countryCode' ? e.target.value.toUpperCase() : e.target.value })} onBlur={() => commit()} /></div>
  );
  return (
    <>
      <H title="Where's your place located?" sub="Guests only see the exact address after booking." />
      <div className="relative">
        <Search className="absolute top-3 left-3 h-4 w-4 text-slate-400" />
        <input className="input pl-9" placeholder="Search for a city or address (OpenStreetMap)" value={q} onChange={(e) => setQ(e.target.value)} aria-label="Search location" />
        {(results.data?.length ?? 0) > 0 && <ul className="absolute z-20 mt-1 w-full overflow-hidden rounded-xl border border-slate-200 bg-white shadow-xl">{results.data!.map((r) => <li key={r.displayName}><button type="button" className="flex w-full items-center gap-2 px-4 py-2.5 text-left text-sm hover:bg-slate-50" onClick={() => pick(r)}><MapPin className="h-4 w-4 shrink-0" />{r.displayName}</button></li>)}</ul>}
      </div>
      <div className="mt-6 grid gap-4 sm:grid-cols-2">{field('line1', 'Street address')}{field('city', 'City')}{field('region', 'Region / state')}{field('postalCode', 'Postal code')}{field('country', 'Country')}{field('countryCode', 'Country code (ISO, e.g. PT)')}</div>
      <div className="mt-4 grid gap-4 sm:grid-cols-2">
        <div><label className="label" htmlFor="lat">Latitude</label><input id="lat" type="number" step="0.000001" className="input" value={coords?.lat ?? ''} onChange={(e) => setCoords({ lat: Number(e.target.value), lng: coords?.lng ?? 0 })} onBlur={() => commit()} /></div>
        <div><label className="label" htmlFor="lng">Longitude</label><input id="lng" type="number" step="0.000001" className="input" value={coords?.lng ?? ''} onChange={(e) => setCoords({ lat: coords?.lat ?? 0, lng: Number(e.target.value) })} onBlur={() => commit()} /></div>
      </div>
      {coords && <div className="mt-6 h-64"><Suspense fallback={<Skeleton className="h-full" />}><MapView single points={[{ id: 'pin', lat: coords.lat, lng: coords.lng, title: 'Your place' }]} /></Suspense></div>}
    </>
  );
}

function BasicsStep({ p, onChange }: { p: PropertyDetail; onChange: (c: UpdatePropertyRequest) => void }) {
  const [title, setTitle] = useState(p.title);
  return (
    <>
      <H title="Give your place a title" sub="Short titles work best. Have fun with it — you can always change it later." />
      <textarea className="input text-2xl font-semibold" rows={2} maxLength={120} value={title} onChange={(e) => { setTitle(e.target.value); onChange({ title: e.target.value }); }} aria-label="Title" />
      <p className="mt-1 text-right text-xs text-slate-500">{title.length}/120</p>
    </>
  );
}

function RoomsStep({ p, onChange }: { p: PropertyDetail; onChange: (c: UpdatePropertyRequest) => void }) {
  const [v, setV] = useState({ maxGuests: p.maxGuests, bedrooms: p.bedrooms, beds: p.beds, bathrooms: p.bathrooms });
  const set = (k: keyof typeof v, n: number) => { const next = { ...v, [k]: n }; setV(next); onChange(next); };
  const row = (k: keyof typeof v, label: string, min: number) => (
    <div className="flex items-center justify-between border-b border-slate-100 py-5"><span className="text-lg">{label}</span>
      <div className="flex items-center gap-3"><GuestSelector value={v[k]} onChange={(n) => set(k, Math.max(min, n))} max={50} /></div></div>
  );
  return (<><H title="Share some basics about your place" />{row('maxGuests', 'Guests', 1)}{row('bedrooms', 'Bedrooms', 0)}{row('beds', 'Beds', 1)}{row('bathrooms', 'Bathrooms', 0)}</>);
}

function AmenitiesStep({ p, onChange }: { p: PropertyDetail; onChange: (c: UpdatePropertyRequest) => void }) {
  const all = useQuery({ queryKey: ['amenities'], queryFn: () => api<AmenityDto[]>('/amenities'), staleTime: Infinity });
  const [selected, setSelected] = useState(new Set(p.amenities.map((a) => a.code)));
  const toggle = (code: string) => { const next = new Set(selected); if (next.has(code)) next.delete(code); else next.add(code); setSelected(next); onChange({ amenities: [...next] }); };
  const groups = (all.data ?? []).reduce<Record<string, AmenityDto[]>>((acc, a) => ({ ...acc, [a.category]: [...(acc[a.category] ?? []), a] }), {});
  return (
    <>
      <H title="Tell guests what your place has to offer" />
      {Object.entries(groups).map(([cat, list]) => (
        <section key={cat} className="mb-6"><h2 className="mb-3 font-semibold">{cat}</h2>
          <div className="grid grid-cols-2 gap-3 sm:grid-cols-3">{list.map((a) => <button key={a.code} type="button" aria-pressed={selected.has(a.code)} onClick={() => toggle(a.code)} className={clsx('rounded-2xl border-2 p-4 text-left text-sm font-medium', selected.has(a.code) ? 'border-slate-900 bg-slate-50' : 'border-slate-200 hover:border-slate-400')}>{a.name}</button>)}</div></section>
      ))}
    </>
  );
}

function PhotosStep({ p }: { p: PropertyDetail }) {
  const qc = useQueryClient();
  const [uploading, setUploading] = useState(0);
  const upload = async (files: FileList) => {
    for (const file of Array.from(files)) {
      setUploading((n) => n + 1);
      try {
        const form = new FormData();
        form.append('file', file);
        await api<ImageDto>(`/properties/${p.id}/images`, { method: 'POST', body: form, timeoutMs: 60000 });
      } catch (e) {
        toast.error(`${file.name}: ${(e as Error).message}`);
      } finally {
        setUploading((n) => n - 1);
      }
    }
    qc.invalidateQueries({ queryKey: ['property', p.id] });
  };
  const remove = useMutation({ mutationFn: (imageId: string) => api(`/properties/${p.id}/images/${imageId}`, { method: 'DELETE' }), onSuccess: () => qc.invalidateQueries({ queryKey: ['property', p.id] }), onError: (e) => toast.error((e as Error).message) });
  return (
    <>
      <H title="Add some photos of your place" sub="JPEG, PNG or WebP up to 10 MB. Photos are validated, stripped of location metadata and resized." />
      <label className="flex cursor-pointer flex-col items-center justify-center rounded-2xl border-2 border-dashed border-slate-300 py-12 hover:border-slate-500">
        <ImagePlus className="h-10 w-10 text-slate-500" /><span className="mt-2 font-semibold">Upload photos</span>{uploading > 0 && <span className="mt-1 text-sm text-slate-500">Uploading {uploading}…</span>}
        <input type="file" multiple accept="image/jpeg,image/png,image/webp" className="sr-only" onChange={(e) => e.target.files && upload(e.target.files)} />
      </label>
      <div className="mt-6 grid grid-cols-2 gap-3 sm:grid-cols-3">
        {p.images.map((img, i) => (
          <div key={img.id} className="group relative">
            <SafeImg src={img.thumbnailUrl} alt={`Photo ${i + 1}`} className="aspect-[4/3] w-full rounded-xl object-cover" />
            {i === 0 && <span className="absolute top-2 left-2 rounded-full bg-white px-2 py-0.5 text-xs font-semibold">Cover</span>}
            <button className="absolute top-2 right-2 rounded-full bg-white p-1.5 opacity-0 shadow group-hover:opacity-100 focus:opacity-100" onClick={() => remove.mutate(img.id)} aria-label="Delete photo"><Trash2 className="h-4 w-4" /></button>
          </div>
        ))}
      </div>
    </>
  );
}

function DescriptionStep({ p, onChange }: { p: PropertyDetail; onChange: (c: UpdatePropertyRequest) => void }) {
  const [d, setD] = useState(p.description);
  return (<><H title="Create your description" sub="Share what makes your place special (at least 20 characters)." /><textarea className="input" rows={10} maxLength={5000} value={d} onChange={(e) => { setD(e.target.value); onChange({ description: e.target.value }); }} aria-label="Description" /><p className="mt-1 text-right text-xs text-slate-500">{d.length}/5000</p></>);
}

function RulesStep({ p, onChange }: { p: PropertyDetail; onChange: (c: UpdatePropertyRequest) => void }) {
  const [r, setR] = useState({ petsAllowed: p.petsAllowed, smokingAllowed: p.smokingAllowed, eventsAllowed: p.eventsAllowed, houseRules: p.houseRules ?? '', checkInTime: p.checkInTime, checkOutTime: p.checkOutTime, cancellationPolicy: p.cancellationPolicy, instantBook: p.instantBook });
  const set = (c: Partial<typeof r>) => { const next = { ...r, ...c }; setR(next); onChange({ rules: next }); };
  const toggle = (k: 'petsAllowed' | 'smokingAllowed' | 'eventsAllowed' | 'instantBook', label: string) => (
    <label className="flex items-center justify-between border-b border-slate-100 py-4"><span>{label}</span><input type="checkbox" className="h-5 w-5 accent-brand-700" checked={r[k]} onChange={(e) => set({ [k]: e.target.checked })} /></label>
  );
  return (
    <>
      <H title="House rules & policies" />
      {toggle('instantBook', 'Instant book (guests can book without approval)')}{toggle('petsAllowed', 'Pets allowed')}{toggle('smokingAllowed', 'Smoking allowed')}{toggle('eventsAllowed', 'Events allowed')}
      <div className="mt-4 grid gap-4 sm:grid-cols-3">
        <div><label className="label" htmlFor="ci">Check-in after</label><input id="ci" type="time" className="input" value={r.checkInTime} onChange={(e) => set({ checkInTime: e.target.value })} /></div>
        <div><label className="label" htmlFor="co">Checkout before</label><input id="co" type="time" className="input" value={r.checkOutTime} onChange={(e) => set({ checkOutTime: e.target.value })} /></div>
        <div><label className="label" htmlFor="pol">Cancellation policy</label><select id="pol" className="input" value={r.cancellationPolicy} onChange={(e) => set({ cancellationPolicy: e.target.value })}>{['Flexible', 'Moderate', 'Strict'].map((x) => <option key={x}>{x}</option>)}</select></div>
      </div>
      <div className="mt-4"><label className="label" htmlFor="hr">Additional rules</label><textarea id="hr" className="input" rows={4} maxLength={2000} value={r.houseRules} onChange={(e) => set({ houseRules: e.target.value })} /></div>
    </>
  );
}

function PricingStep({ p, onChange }: { p: PropertyDetail; onChange: (c: UpdatePropertyRequest) => void }) {
  const [v, setV] = useState({ basePrice: p.basePrice || 100, cleaningFee: p.cleaningFee, currency: p.currency, weekendAdjustmentPercent: p.weekendAdjustmentPercent, weeklyDiscountPercent: p.weeklyDiscountPercent, monthlyDiscountPercent: p.monthlyDiscountPercent, minNights: p.minNights });
  const set = (c: Partial<typeof v>) => { const next = { ...v, ...c }; setV(next); onChange({ pricing: next }); };
  const num = (k: keyof Omit<typeof v, 'currency'>, label: string, suffix?: string) => (
    <div><label className="label" htmlFor={k}>{label}</label><div className="relative"><input id={k} type="number" min={0} className="input" value={v[k]} onChange={(e) => set({ [k]: Number(e.target.value) })} />{suffix && <span className="absolute top-2.5 right-3 text-sm text-slate-500">{suffix}</span>}</div></div>
  );
  return (
    <>
      <H title="Now, set your price" sub="Guests see a server-calculated total including cleaning, service fee and taxes." />
      <p className="mb-8 text-center text-5xl font-extrabold">{money(v.basePrice, v.currency, true)}<span className="text-lg font-normal text-slate-500"> / night</span></p>
      <div className="grid gap-4 sm:grid-cols-2">
        {num('basePrice', 'Nightly price')}
        <div><label className="label" htmlFor="cur">Currency</label><select id="cur" className="input" value={v.currency} onChange={(e) => set({ currency: e.target.value })}>{['USD', 'EUR', 'GBP', 'INR', 'JPY', 'AUD', 'CAD'].map((c) => <option key={c}>{c}</option>)}</select></div>
        {num('cleaningFee', 'Cleaning fee')}{num('minNights', 'Minimum nights')}
        {num('weekendAdjustmentPercent', 'Weekend adjustment (Fri & Sat)', '%')}{num('weeklyDiscountPercent', 'Weekly discount (7+ nights)', '%')}{num('monthlyDiscountPercent', 'Monthly discount (28+ nights)', '%')}
      </div>
    </>
  );
}

function PreviewStep({ p }: { p: PropertyDetail }) {
  return (
    <>
      <H title="Review your listing" sub="Here's what guests will see." />
      <div className="card overflow-hidden">
        {p.images[0] ? <SafeImg src={p.images[0].url} alt="" className="aspect-[16/9] w-full object-cover" /> : <div className="flex aspect-[16/9] items-center justify-center bg-slate-100 text-slate-500">No photos yet</div>}
        <div className="p-6">
          <h2 className="text-xl font-bold">{p.title || 'Untitled listing'}</h2>
          <p className="text-slate-600">{p.address ? `${p.address.city}, ${p.address.country}` : 'No location yet'} · {humanize(p.propertyType)} · {p.maxGuests} guests · {p.bedrooms} bedrooms</p>
          <p className="mt-3 line-clamp-4 text-sm text-slate-700">{p.description || 'No description yet.'}</p>
          <p className="mt-3 font-semibold">{money(p.basePrice, p.currency, true)} night</p>
        </div>
      </div>
    </>
  );
}

function PublishStep({ p }: { p: PropertyDetail }) {
  const checks: [string, boolean][] = [['Title (5+ characters)', p.title.length >= 5], ['Description (20+ characters)', p.description.length >= 20], ['Location', p.latitude != null], ['Nightly price', p.basePrice > 0], ['At least one photo', p.images.length > 0]];
  const ready = checks.every(([, ok]) => ok);
  return (
    <>
      <H title={p.status === 'Published' ? 'Your listing is live' : ready ? "You're ready to publish!" : 'Almost there'} sub={ready ? 'Publishing makes your listing searchable immediately.' : 'Complete these items to publish:'} />
      <ul className="space-y-3">{checks.map(([label, ok]) => <li key={label} className="flex items-center gap-3"><span className={clsx('flex h-6 w-6 items-center justify-center rounded-full', ok ? 'bg-emerald-100 text-emerald-700' : 'bg-slate-100 text-slate-400')}><Check className="h-4 w-4" /></span><span className={ok ? '' : 'text-slate-500'}>{label}</span></li>)}</ul>
    </>
  );
}
