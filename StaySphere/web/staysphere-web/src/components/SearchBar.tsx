import { clsx } from 'clsx';
import { MapPin, Minus, Plus, Search } from 'lucide-react';
import { useEffect, useId, useRef, useState } from 'react';
import { useNavigate } from 'react-router';
import { useQuery } from '@tanstack/react-query';
import { api, toQuery } from '@/api/client';
import { isoDate } from '@/lib/format';

export interface SearchValues { location: string; checkIn: string; checkOut: string; guests: number }

export function GuestSelector({ value, onChange, max = 16 }: { value: number; onChange: (v: number) => void; max?: number }) {
  return (
    <div className="flex items-center gap-3">
      <button type="button" className="rounded-full border border-slate-300 p-1.5 disabled:opacity-30" disabled={value <= 1} onClick={() => onChange(value - 1)} aria-label="Fewer guests">
        <Minus className="h-4 w-4" />
      </button>
      <span className="w-6 text-center font-medium" aria-live="polite">{value}</span>
      <button type="button" className="rounded-full border border-slate-300 p-1.5 disabled:opacity-30" disabled={value >= max} onClick={() => onChange(value + 1)} aria-label="More guests">
        <Plus className="h-4 w-4" />
      </button>
    </div>
  );
}

export function SearchBar({ initial, compact }: { initial?: Partial<SearchValues>; compact?: boolean }) {
  const navigate = useNavigate();
  const listId = useId();
  const today = isoDate(new Date());
  const [values, setValues] = useState<SearchValues>({ location: '', checkIn: '', checkOut: '', guests: 1, ...initial });
  const [showGuests, setShowGuests] = useState(false);
  const [focused, setFocused] = useState(false);
  const guestRef = useRef<HTMLDivElement>(null);

  const suggestions = useQuery({
    queryKey: ['suggest', values.location],
    queryFn: () => api<string[]>(`/search/suggest${toQuery({ q: values.location })}`),
    enabled: focused && values.location.trim().length >= 2,
    staleTime: 60_000,
  });

  useEffect(() => {
    const close = (e: MouseEvent) => guestRef.current && !guestRef.current.contains(e.target as Node) && setShowGuests(false);
    document.addEventListener('mousedown', close);
    return () => document.removeEventListener('mousedown', close);
  }, []);

  const submit = (e?: React.FormEvent) => {
    e?.preventDefault();
    navigate(`/search${toQuery({ location: values.location.trim(), checkIn: values.checkIn, checkOut: values.checkOut, guests: values.guests > 1 ? values.guests : undefined })}`);
  };

  return (
    <form onSubmit={submit} role="search" aria-label="Search stays"
      className={clsx('relative grid w-full grid-cols-2 items-center gap-1 rounded-3xl border border-slate-200 bg-white p-2 shadow-card md:flex md:rounded-full', compact ? 'max-w-3xl' : 'max-w-4xl')}>
      <label className="relative col-span-2 flex flex-1 flex-col rounded-full px-5 py-2 hover:bg-slate-50 md:col-span-1">
        <span className="text-xs font-semibold">Where</span>
        <input
          className="bg-transparent text-sm outline-none placeholder:text-slate-400"
          placeholder="Search destinations"
          value={values.location}
          onChange={(e) => setValues({ ...values, location: e.target.value })}
          onFocus={() => setFocused(true)}
          onBlur={() => setTimeout(() => setFocused(false), 150)}
          aria-autocomplete="list"
          aria-controls={listId}
        />
        {focused && (suggestions.data?.length ?? 0) > 0 && (
          <ul id={listId} role="listbox" className="absolute top-full left-0 z-50 mt-2 w-80 overflow-hidden rounded-2xl border border-slate-200 bg-white py-2 shadow-xl">
            {suggestions.data!.map((s) => (
              <li key={s}>
                <button type="button" role="option" aria-selected={false} className="flex w-full items-center gap-3 px-4 py-2.5 text-left text-sm hover:bg-slate-50"
                  onMouseDown={() => setValues({ ...values, location: s.split(',')[0] })}>
                  <span className="rounded-lg bg-slate-100 p-2"><MapPin className="h-4 w-4" /></span>
                  {s}
                </button>
              </li>
            ))}
          </ul>
        )}
      </label>
      <span className="hidden h-8 w-px bg-slate-200 md:block" />
      <label className="flex flex-col rounded-full px-5 py-2 hover:bg-slate-50">
        <span className="text-xs font-semibold">Check in</span>
        <input type="date" min={today} className="bg-transparent text-sm outline-none" value={values.checkIn}
          onChange={(e) => setValues({ ...values, checkIn: e.target.value, checkOut: values.checkOut && values.checkOut <= e.target.value ? '' : values.checkOut })} />
      </label>
      <span className="hidden h-8 w-px bg-slate-200 md:block" />
      <label className="flex flex-col rounded-full px-5 py-2 hover:bg-slate-50">
        <span className="text-xs font-semibold">Check out</span>
        <input type="date" min={values.checkIn || today} className="bg-transparent text-sm outline-none" value={values.checkOut}
          onChange={(e) => setValues({ ...values, checkOut: e.target.value })} />
      </label>
      <span className="hidden h-8 w-px bg-slate-200 md:block" />
      <div ref={guestRef} className="relative flex items-center justify-between gap-2 rounded-full py-1 pr-1 pl-5 hover:bg-slate-50 col-span-2 md:col-span-1">
        <button type="button" className="flex flex-col text-left" onClick={() => setShowGuests((s) => !s)} aria-expanded={showGuests}>
          <span className="text-xs font-semibold">Who</span>
          <span className="text-sm text-slate-600">{values.guests} guest{values.guests === 1 ? '' : 's'}</span>
        </button>
        <button type="submit" className="btn-accent rounded-full !px-4 !py-3" aria-label="Search">
          <Search className="h-5 w-5" /> <span className="md:hidden lg:inline">Search</span>
        </button>
        {showGuests && (
          <div className="absolute top-full right-0 z-50 mt-3 w-72 rounded-2xl border border-slate-200 bg-white p-5 shadow-xl">
            <div className="flex items-center justify-between">
              <div>
                <p className="font-medium">Guests</p>
                <p className="text-sm text-slate-500">Adults and children</p>
              </div>
              <GuestSelector value={values.guests} onChange={(g) => setValues({ ...values, guests: g })} />
            </div>
          </div>
        )}
      </div>
    </form>
  );
}
