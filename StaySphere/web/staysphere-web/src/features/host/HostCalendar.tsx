import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { clsx } from 'clsx';
import { addMonths, eachDayOfInterval, endOfMonth, format, getDay, isBefore, startOfMonth, startOfToday } from 'date-fns';
import { ChevronLeft, ChevronRight, Trash2 } from 'lucide-react';
import { useState } from 'react';
import { api } from '@/api/client';
import type { AvailabilityDto, HostPropertyListItem, SeasonalPriceDto } from '@/api/types';
import { EmptyState, PageHeader, Skeleton } from '@/components/ui';
import { isoDate, money, prettyDate } from '@/lib/format';
import { toast } from '@/lib/toast';

export function HostCalendarPage() {
  const listings = useQuery({ queryKey: ['host', 'properties'], queryFn: () => api<HostPropertyListItem[]>('/host/properties') });
  const [selected, setSelected] = useState<string>('');
  const id = selected || listings.data?.[0]?.id || '';
  return (
    <>
      <PageHeader title="Calendar" subtitle="Block dates and set seasonal prices. Booked nights can't be blocked." actions={listings.data && listings.data.length > 0 && (
        <select className="input !w-auto" value={id} onChange={(e) => setSelected(e.target.value)} aria-label="Listing">{listings.data.map((l) => <option key={l.id} value={l.id}>{l.title}</option>)}</select>)} />
      {listings.isLoading ? <Skeleton className="h-96" /> : !id ? <EmptyState title="No listings yet" description="Create a listing to manage its calendar." /> : <HostCalendar propertyId={id} />}
    </>
  );
}

export function HostCalendar({ propertyId, embedded }: { propertyId: string; embedded?: boolean }) {
  const qc = useQueryClient();
  const today = startOfToday();
  const [offset, setOffset] = useState(0);
  const availability = useQuery({ queryKey: ['availability', propertyId], queryFn: () => api<AvailabilityDto>(`/properties/${propertyId}/availability`) });
  const seasons = useQuery({ queryKey: ['seasons', propertyId], queryFn: () => api<SeasonalPriceDto[]>(`/properties/${propertyId}/seasonal-prices`) });
  const [selection, setSelection] = useState<Set<string>>(new Set());
  const [season, setSeason] = useState({ start: '', end: '', nightlyPrice: '' });

  const unavailable = new Set(availability.data?.unavailableDates ?? []);
  const blockMutation = useMutation({
    mutationFn: (block: boolean) => api(`/properties/${propertyId}/blocked-dates`, { method: 'PUT', body: block ? { block: [...selection], unblock: [] } : { block: [], unblock: [...selection] } }),
    onSuccess: (_d, block) => { toast.success(block ? 'Dates blocked' : 'Dates opened'); setSelection(new Set()); qc.invalidateQueries({ queryKey: ['availability', propertyId] }); },
    onError: (e) => toast.error((e as Error).message),
  });
  const addSeason = useMutation({
    mutationFn: () => api(`/properties/${propertyId}/seasonal-prices`, { method: 'POST', body: { start: season.start, end: season.end, nightlyPrice: Number(season.nightlyPrice) } }),
    onSuccess: () => { toast.success('Seasonal price added'); setSeason({ start: '', end: '', nightlyPrice: '' }); qc.invalidateQueries({ queryKey: ['seasons', propertyId] }); },
    onError: (e) => toast.error((e as Error).message),
  });
  const removeSeason = useMutation({ mutationFn: (id: string) => api(`/properties/${propertyId}/seasonal-prices/${id}`, { method: 'DELETE' }), onSuccess: () => qc.invalidateQueries({ queryKey: ['seasons', propertyId] }) });

  const toggle = (iso: string) => { const next = new Set(selection); if (next.has(iso)) next.delete(iso); else next.add(iso); setSelection(next); };

  return (
    <div className="grid gap-8 lg:grid-cols-[1fr_320px]">
      <div>
        {embedded && <div className="mb-6"><h1 className="text-2xl font-bold sm:text-3xl">Availability</h1><p className="mt-1 text-slate-600">Click dates to select them, then block or open them. You can skip this step.</p></div>}
        <div className="mb-3 flex items-center justify-between">
          <button className="rounded-full p-1.5 hover:bg-slate-100 disabled:opacity-30" disabled={offset === 0} onClick={() => setOffset((o) => o - 1)} aria-label="Previous"><ChevronLeft className="h-5 w-5" /></button>
          <button className="rounded-full p-1.5 hover:bg-slate-100" onClick={() => setOffset((o) => o + 1)} aria-label="Next"><ChevronRight className="h-5 w-5" /></button>
        </div>
        {availability.isLoading ? <Skeleton className="h-80" /> : (
          <div className="grid gap-8 md:grid-cols-2">
            {[0, 1].map((m) => {
              const month = addMonths(startOfMonth(today), offset + m);
              const days = eachDayOfInterval({ start: month, end: endOfMonth(month) });
              return (
                <div key={m}>
                  <p className="mb-2 text-center font-semibold">{format(month, 'MMMM yyyy')}</p>
                  <div className="grid grid-cols-7 gap-1">
                    {Array.from({ length: (getDay(month) + 6) % 7 }, (_, i) => <span key={i} />)}
                    {days.map((d) => {
                      const iso = isoDate(d);
                      const past = isBefore(d, today);
                      return (
                        <button key={iso} type="button" disabled={past} onClick={() => toggle(iso)} aria-pressed={selection.has(iso)}
                          className={clsx('h-11 rounded-lg text-sm transition', past && 'text-slate-300', selection.has(iso) ? 'bg-slate-900 text-white' : unavailable.has(iso) ? 'bg-slate-200 text-slate-500 line-through' : 'bg-emerald-50 hover:bg-emerald-100')}>
                          {format(d, 'd')}
                        </button>
                      );
                    })}
                  </div>
                </div>
              );
            })}
          </div>
        )}
        <div className="mt-4 flex flex-wrap items-center gap-3 text-sm">
          <span className="text-slate-600">{selection.size} selected</span>
          <button className="btn-secondary !py-1.5" disabled={!selection.size || blockMutation.isPending} onClick={() => blockMutation.mutate(true)}>Block</button>
          <button className="btn-secondary !py-1.5" disabled={!selection.size || blockMutation.isPending} onClick={() => blockMutation.mutate(false)}>Open</button>
          <span className="ml-auto flex gap-3 text-xs text-slate-500"><span className="flex items-center gap-1"><span className="h-3 w-3 rounded bg-emerald-50 ring-1 ring-emerald-200" /> Available</span><span className="flex items-center gap-1"><span className="h-3 w-3 rounded bg-slate-200" /> Booked / blocked</span></span>
        </div>
      </div>
      <aside className="card h-fit p-5">
        <h2 className="mb-3 font-semibold">Seasonal pricing</h2>
        <ul className="mb-4 space-y-2 text-sm">
          {(seasons.data ?? []).map((s) => <li key={s.id} className="flex items-center justify-between rounded-lg bg-slate-50 px-3 py-2"><span>{prettyDate(s.start)} – {prettyDate(s.end)}<br /><strong>{money(s.nightlyPrice, undefined, true)}</strong> / night</span><button onClick={() => removeSeason.mutate(s.id)} aria-label="Remove"><Trash2 className="h-4 w-4 text-slate-500" /></button></li>)}
          {seasons.data?.length === 0 && <li className="text-slate-500">No seasonal prices.</li>}
        </ul>
        <div className="space-y-2">
          <input type="date" className="input" value={season.start} onChange={(e) => setSeason({ ...season, start: e.target.value })} aria-label="Season start" />
          <input type="date" className="input" value={season.end} min={season.start} onChange={(e) => setSeason({ ...season, end: e.target.value })} aria-label="Season end" />
          <input type="number" className="input" placeholder="Nightly price" value={season.nightlyPrice} onChange={(e) => setSeason({ ...season, nightlyPrice: e.target.value })} aria-label="Nightly price" />
          <button className="btn-primary w-full" disabled={!season.start || !season.end || !season.nightlyPrice || addSeason.isPending} onClick={() => addSeason.mutate()}>Add season</button>
        </div>
      </aside>
    </div>
  );
}
