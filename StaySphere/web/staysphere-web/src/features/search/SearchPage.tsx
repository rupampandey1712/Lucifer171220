import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { clsx } from 'clsx';
import { List, Map as MapIcon, SlidersHorizontal } from 'lucide-react';
import { lazy, Suspense, useMemo, useState } from 'react';
import { useSearchParams } from 'react-router';
import { api, toQuery } from '@/api/client';
import type { AmenityDto, Paged, PropertyCard as Card } from '@/api/types';
import { PropertyCard, PropertyCardSkeleton } from '@/components/PropertyCard';
import { SearchBar } from '@/components/SearchBar';
import { EmptyState, ErrorState, Modal, Pagination, Skeleton } from '@/components/ui';
import { humanize } from '@/lib/format';

const MapView = lazy(() => import('@/components/MapView').then((m) => ({ default: m.MapView })));

const TYPES = ['Apartment', 'House', 'Villa', 'Cabin', 'Cottage', 'GuestHouse', 'Hotel', 'Hostel', 'BedAndBreakfast', 'UniqueStay', 'FarmStay', 'Resort'];
const SORTS: [string, string][] = [['Recommended', 'Recommended'], ['PriceLowHigh', 'Price: low to high'], ['PriceHighLow', 'Price: high to low'], ['Rating', 'Top rated'], ['MostReviewed', 'Most reviewed'], ['Newest', 'Newest']];

export function SearchPage() {
  const [params, setParams] = useSearchParams();
  const [filtersOpen, setFiltersOpen] = useState(false);
  const [showMap, setShowMap] = useState(false);
  const [hovered, setHovered] = useState<string | null>(null);

  const query = useMemo(() => {
    const q: Record<string, unknown> = {};
    params.forEach((v, k) => {
      if (k === 'amenities' || k === 'propertyTypes') q[k] = params.getAll(k);
      else q[k] = v;
    });
    return q;
  }, [params]);

  const result = useQuery({
    queryKey: ['search', query],
    queryFn: () => api<Paged<Card>>(`/search/properties${toQuery({ ...query, pageSize: 18 })}`),
    placeholderData: keepPreviousData,
  });
  const amenities = useQuery({ queryKey: ['amenities'], queryFn: () => api<AmenityDto[]>('/amenities'), staleTime: Infinity });

  const update = (changes: Record<string, string | string[] | undefined>) => {
    const next = new URLSearchParams(params);
    for (const [k, v] of Object.entries(changes)) {
      next.delete(k);
      if (Array.isArray(v)) v.forEach((x) => next.append(k, x));
      else if (v) next.set(k, v);
    }
    if (!('page' in changes)) next.delete('page');
    setParams(next);
  };

  const carry = toQuery({ checkIn: params.get('checkIn'), checkOut: params.get('checkOut'), guests: params.get('guests') });
  const points = (result.data?.items ?? []).filter((p) => p.latitude != null).map((p) => ({ id: p.id, lat: p.latitude!, lng: p.longitude!, price: p.nightlyPrice, currency: p.currency, title: p.title, image: p.imageUrls[0] }));
  const activeFilters = ['minPrice', 'maxPrice', 'bedrooms', 'beds', 'bathrooms', 'minRating', 'instantBook', 'petsAllowed', 'roomType'].filter((k) => params.get(k)).length + params.getAll('amenities').length + params.getAll('propertyTypes').length;

  return (
    <div>
      <div className="border-b border-slate-200 bg-white py-4">
        <div className="container-page flex flex-col items-center gap-4">
          <SearchBar compact initial={{ location: params.get('location') ?? '', checkIn: params.get('checkIn') ?? '', checkOut: params.get('checkOut') ?? '', guests: Number(params.get('guests') ?? 1) }} />
          <div className="flex w-full flex-wrap items-center gap-2">
            <button className={clsx('chip', activeFilters > 0 && 'chip-active')} onClick={() => setFiltersOpen(true)}><SlidersHorizontal className="h-4 w-4" /> Filters{activeFilters > 0 && ` (${activeFilters})`}</button>
            {['Apartment', 'House', 'Villa', 'Cabin'].map((t) => {
              const on = params.getAll('propertyTypes').includes(t);
              return <button key={t} className={clsx('chip', on && 'chip-active')} aria-pressed={on} onClick={() => update({ propertyTypes: on ? params.getAll('propertyTypes').filter((x) => x !== t) : [...params.getAll('propertyTypes'), t] })}>{t}</button>;
            })}
            {['wifi', 'pool', 'parking', 'kitchen'].map((a) => {
              const on = params.getAll('amenities').includes(a);
              return <button key={a} className={clsx('chip', on && 'chip-active')} aria-pressed={on} onClick={() => update({ amenities: on ? params.getAll('amenities').filter((x) => x !== a) : [...params.getAll('amenities'), a] })}>{humanize(a)}</button>;
            })}
            <button className={clsx('chip', params.get('instantBook') && 'chip-active')} onClick={() => update({ instantBook: params.get('instantBook') ? undefined : 'true' })}>Instant book</button>
            <label className="ml-auto flex items-center gap-2 text-sm">
              <span className="text-slate-600">Sort</span>
              <select className="input !w-auto !py-1.5" value={params.get('sort') ?? 'Recommended'} onChange={(e) => update({ sort: e.target.value })}>
                {SORTS.map(([v, l]) => <option key={v} value={v}>{l}</option>)}
              </select>
            </label>
          </div>
        </div>
      </div>

      <div className="container-page grid gap-8 py-6 lg:grid-cols-[1fr_minmax(380px,40%)]">
        <section className={clsx(showMap && 'hidden lg:block')} aria-live="polite">
          <p className="mb-4 text-sm font-medium text-slate-700">
            {result.isLoading ? <Skeleton className="h-4 w-40" /> : `${result.data?.totalCount ?? 0} stays${params.get('location') ? ` in ${params.get('location')}` : ''}`}
          </p>
          {result.isError ? <ErrorState error={result.error} onRetry={() => result.refetch()} />
            : result.isLoading ? <div className="grid gap-6 sm:grid-cols-2 xl:grid-cols-3">{Array.from({ length: 6 }, (_, i) => <PropertyCardSkeleton key={i} />)}</div>
            : result.data!.items.length === 0 ? (
              <EmptyState title="No exact matches" description="Try changing or removing some of your filters, adjusting your dates or zooming out on the map."
                action={<button className="btn-secondary" onClick={() => setParams(params.get('location') ? { location: params.get('location')! } : {})}>Clear filters</button>} />
            ) : (
              <>
                <div className={clsx('grid gap-x-6 gap-y-10 sm:grid-cols-2 xl:grid-cols-3', result.isFetching && 'opacity-70 transition')}>
                  {result.data!.items.map((p) => <PropertyCard key={p.id} property={p} query={carry} highlighted={hovered === p.id} onHover={setHovered} />)}
                </div>
                <Pagination page={result.data!.page} totalPages={Math.ceil(result.data!.totalCount / result.data!.pageSize)} onChange={(p) => update({ page: String(p) })} />
              </>
            )}
        </section>
        <aside className={clsx('sticky top-24 h-[calc(100vh-8rem)]', !showMap && 'hidden lg:block')} aria-label="Map of results">
          <Suspense fallback={<Skeleton className="h-full w-full" />}>
            <MapView points={points} activeId={hovered} />
          </Suspense>
        </aside>
      </div>

      <button className="btn fixed bottom-6 left-1/2 z-[400] -translate-x-1/2 rounded-full bg-slate-900 text-white shadow-xl lg:hidden" onClick={() => setShowMap((m) => !m)}>
        {showMap ? <><List className="h-4 w-4" /> Show list</> : <><MapIcon className="h-4 w-4" /> Show map</>}
      </button>

      <Modal open={filtersOpen} onClose={() => setFiltersOpen(false)} title="Filters" wide>
        <FilterForm params={params} amenities={amenities.data ?? []} onApply={(changes) => { update(changes); setFiltersOpen(false); }} />
      </Modal>
    </div>
  );
}

function FilterForm({ params, amenities, onApply }: { params: URLSearchParams; amenities: AmenityDto[]; onApply: (c: Record<string, string | string[] | undefined>) => void }) {
  const [f, setF] = useState({
    minPrice: params.get('minPrice') ?? '', maxPrice: params.get('maxPrice') ?? '', bedrooms: params.get('bedrooms') ?? '', beds: params.get('beds') ?? '',
    bathrooms: params.get('bathrooms') ?? '', minRating: params.get('minRating') ?? '', roomType: params.get('roomType') ?? '',
    instantBook: !!params.get('instantBook'), petsAllowed: !!params.get('petsAllowed'),
    amenities: params.getAll('amenities'), propertyTypes: params.getAll('propertyTypes'),
  });
  const toggle = (key: 'amenities' | 'propertyTypes', v: string) => setF({ ...f, [key]: f[key].includes(v) ? f[key].filter((x) => x !== v) : [...f[key], v] });
  const counter = (key: 'bedrooms' | 'beds' | 'bathrooms', label: string) => (
    <div className="flex items-center justify-between py-2">
      <span>{label}</span>
      <div className="flex gap-2">{['', '1', '2', '3', '4', '5'].map((n) => <button key={n} type="button" className={clsx('chip !px-3', f[key] === n && 'chip-active')} onClick={() => setF({ ...f, [key]: n })}>{n === '' ? 'Any' : `${n}+`}</button>)}</div>
    </div>
  );
  return (
    <div className="space-y-8">
      <section><h3 className="mb-3 font-semibold">Price per night</h3>
        <div className="flex gap-4"><input className="input" type="number" min={0} placeholder="Min" value={f.minPrice} onChange={(e) => setF({ ...f, minPrice: e.target.value })} aria-label="Minimum price" /><input className="input" type="number" min={0} placeholder="Max" value={f.maxPrice} onChange={(e) => setF({ ...f, maxPrice: e.target.value })} aria-label="Maximum price" /></div></section>
      <section><h3 className="mb-3 font-semibold">Type of place</h3>
        <div className="flex flex-wrap gap-2">{[['', 'Any type'], ['EntirePlace', 'Entire place'], ['PrivateRoom', 'Private room'], ['SharedRoom', 'Shared room'], ['HotelRoom', 'Hotel room']].map(([v, l]) => <button type="button" key={v} className={clsx('chip', f.roomType === v && 'chip-active')} onClick={() => setF({ ...f, roomType: v })}>{l}</button>)}</div></section>
      <section><h3 className="mb-1 font-semibold">Rooms and beds</h3>{counter('bedrooms', 'Bedrooms')}{counter('beds', 'Beds')}{counter('bathrooms', 'Bathrooms')}</section>
      <section><h3 className="mb-3 font-semibold">Property type</h3>
        <div className="flex flex-wrap gap-2">{TYPES.map((t) => <button type="button" key={t} className={clsx('chip', f.propertyTypes.includes(t) && 'chip-active')} onClick={() => toggle('propertyTypes', t)}>{humanize(t)}</button>)}</div></section>
      <section><h3 className="mb-3 font-semibold">Amenities</h3>
        <div className="grid grid-cols-2 gap-2 sm:grid-cols-3">{amenities.map((a) => <label key={a.code} className="flex items-center gap-2 text-sm"><input type="checkbox" className="h-4 w-4 accent-brand-700" checked={f.amenities.includes(a.code)} onChange={() => toggle('amenities', a.code)} />{a.name}</label>)}</div></section>
      <section className="grid gap-3 sm:grid-cols-3">
        <label className="flex items-center gap-2 text-sm"><input type="checkbox" className="h-4 w-4 accent-brand-700" checked={f.instantBook} onChange={(e) => setF({ ...f, instantBook: e.target.checked })} /> Instant book</label>
        <label className="flex items-center gap-2 text-sm"><input type="checkbox" className="h-4 w-4 accent-brand-700" checked={f.petsAllowed} onChange={(e) => setF({ ...f, petsAllowed: e.target.checked })} /> Pet friendly</label>
        <label className="flex items-center gap-2 text-sm">Min rating <select className="input !w-auto !py-1" value={f.minRating} onChange={(e) => setF({ ...f, minRating: e.target.value })}><option value="">Any</option><option value="4">4+</option><option value="4.5">4.5+</option><option value="4.8">4.8+</option></select></label>
      </section>
      <div className="flex justify-between border-t border-slate-100 pt-4">
        <button type="button" className="btn-ghost underline" onClick={() => onApply({ minPrice: undefined, maxPrice: undefined, bedrooms: undefined, beds: undefined, bathrooms: undefined, minRating: undefined, roomType: undefined, instantBook: undefined, petsAllowed: undefined, amenities: [], propertyTypes: [] })}>Clear all</button>
        <button type="button" className="btn-primary" onClick={() => onApply({ ...f, instantBook: f.instantBook ? 'true' : undefined, petsAllowed: f.petsAllowed ? 'true' : undefined })}>Show stays</button>
      </div>
    </div>
  );
}
