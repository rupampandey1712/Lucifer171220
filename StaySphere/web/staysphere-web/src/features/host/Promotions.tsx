import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { BadgePercent, Plus } from 'lucide-react';
import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { z } from 'zod';
import { api, ApiError } from '@/api/client';
import type { HostPropertyListItem, PromotionDto } from '@/api/types';
import { DataTable, EmptyState, ErrorState, FieldError, Modal, PageHeader, Skeleton, StatusBadge } from '@/components/ui';
import { prettyDate } from '@/lib/format';
import { toast } from '@/lib/toast';

const KINDS = {
  EarlyBird: 'Early bird — booked well in advance',
  LastMinute: 'Last minute — stays starting soon',
  LongStay: 'Long stay — minimum number of nights',
  FlashSale: 'Flash sale — stays starting in a date window',
} as const;

const schema = z.object({
  name: z.string().trim().min(1, 'Give it a name').max(80),
  kind: z.enum(['EarlyBird', 'LastMinute', 'LongStay', 'FlashSale']),
  percentOff: z.coerce.number().min(1, 'At least 1%').max(50, 'At most 50%'),
  propertyId: z.string().optional(),
  days: z.coerce.number().int().min(0).max(365).optional(),
  minNights: z.coerce.number().int().min(2).max(90).optional(),
  stayStart: z.string().optional(),
  stayEnd: z.string().optional(),
  validTo: z.string().min(1, 'Choose an end date'),
}).superRefine((v, ctx) => {
  if ((v.kind === 'EarlyBird' || v.kind === 'LastMinute') && v.days === undefined) ctx.addIssue({ code: 'custom', path: ['days'], message: 'Required' });
  if (v.kind === 'LongStay' && v.minNights === undefined) ctx.addIssue({ code: 'custom', path: ['minNights'], message: 'Required' });
  if (v.kind === 'FlashSale' && (!v.stayStart || !v.stayEnd)) ctx.addIssue({ code: 'custom', path: ['stayEnd'], message: 'Choose the stay window' });
});
type FormValues = z.input<typeof schema>;

const emptyToUndefined = (v: unknown) => (v === '' || v === null || Number.isNaN(v) ? undefined : v);

/** Host promotions (scope "host") or platform-wide promotions (scope "admin"). The server enforces who may do what. */
export function PromotionsManager({ scope }: { scope: 'host' | 'admin' }) {
  const qc = useQueryClient();
  const [open, setOpen] = useState(false);
  const base = scope === 'host' ? '/host/promotions' : '/admin/promotions';
  const list = useQuery({ queryKey: [scope, 'promotions'], queryFn: () => api<PromotionDto[]>(base) });
  const listings = useQuery({ queryKey: ['host', 'properties'], queryFn: () => api<HostPropertyListItem[]>('/host/properties'), enabled: scope === 'host' });
  const form = useForm<FormValues>({ resolver: zodResolver(schema), defaultValues: { kind: 'EarlyBird', percentOff: 10, days: 30 } });
  const kind = form.watch('kind');

  const create = useMutation({
    mutationFn: (v: FormValues) => {
      const parsed = schema.parse(v);
      return api<PromotionDto>(base, {
        method: 'POST',
        body: {
          name: parsed.name, kind: parsed.kind, percentOff: parsed.percentOff, propertyId: parsed.propertyId || null,
          daysBeforeCheckIn: parsed.kind === 'EarlyBird' || parsed.kind === 'LastMinute' ? parsed.days : null,
          minNights: parsed.kind === 'LongStay' ? parsed.minNights : null,
          stayStart: parsed.kind === 'FlashSale' ? parsed.stayStart : null, stayEnd: parsed.kind === 'FlashSale' ? parsed.stayEnd : null,
          validFrom: null, validTo: new Date(`${parsed.validTo}T23:59:59Z`).toISOString(),
        },
      });
    },
    onSuccess: () => { toast.success('Promotion is live'); setOpen(false); form.reset(); qc.invalidateQueries({ queryKey: [scope, 'promotions'] }); },
    onError: (e) => toast.error(e instanceof ApiError ? e.message : 'Could not create the promotion'),
  });
  const deactivate = useMutation({
    mutationFn: (id: string) => api(`${base}/${id}/deactivate`, { method: 'POST', body: {} }),
    onSuccess: () => { toast.success('Promotion switched off'); qc.invalidateQueries({ queryKey: [scope, 'promotions'] }); },
    onError: (e) => toast.error((e as Error).message),
  });

  const err = (name: keyof FormValues) => form.formState.errors[name]?.message as string | undefined;

  return (
    <section>
      <PageHeader
        title={scope === 'host' ? 'Promotions' : 'Platform promotions'}
        subtitle={scope === 'host'
          ? 'Automatic discounts guests see on your listings. Only the best promotion applies to a stay.'
          : 'Discounts on every listing on the platform. Only the best promotion applies to a stay.'}
        actions={<button className="btn-primary" onClick={() => setOpen(true)}><Plus className="h-4 w-4" /> New promotion</button>} />

      {list.isLoading ? <Skeleton className="h-40" /> : list.isError ? <ErrorState error={list.error} onRetry={() => list.refetch()} /> :
        list.data!.length === 0 ? (
          <EmptyState icon={<BadgePercent className="h-7 w-7" />} title="No promotions yet"
            description="Offer an early-bird, last-minute, long-stay or flash-sale discount to fill your calendar."
            action={<button className="btn-primary" onClick={() => setOpen(true)}>Create a promotion</button>} />
        ) : (
          <DataTable rows={list.data!} rowKey={(p) => p.id} columns={[
            { header: 'Promotion', cell: (p) => <div><p className="font-medium">{p.name}</p><p className="text-xs text-slate-500">{p.summary}</p></div> },
            { header: 'Applies to', cell: (p) => p.propertyTitle ?? (scope === 'host' ? 'All your listings' : 'Every listing') },
            { header: 'Ends', cell: (p) => prettyDate(p.validTo) },
            { header: 'Status', cell: (p) => <StatusBadge status={p.isActive ? 'Active' : 'Inactive'} /> },
            { header: '', cell: (p) => p.isActive && <button className="btn-secondary !py-1 text-xs" disabled={deactivate.isPending} onClick={() => deactivate.mutate(p.id)}>Switch off</button> },
          ]} />
        )}

      <Modal open={open} onClose={() => setOpen(false)} title="New promotion">
        <form className="space-y-4" onSubmit={form.handleSubmit((v) => create.mutate(v))} noValidate>
          <div><label className="label" htmlFor="promo-name">Name</label><input id="promo-name" className="input" {...form.register('name')} placeholder="Early bird summer deal" /><FieldError message={err('name')} /></div>
          <div><label className="label" htmlFor="promo-kind">Type</label>
            <select id="promo-kind" className="input" {...form.register('kind')}>{Object.entries(KINDS).map(([k, v]) => <option key={k} value={k}>{v}</option>)}</select></div>
          <div className="grid grid-cols-2 gap-3">
            <div><label className="label" htmlFor="promo-percent">Discount (%)</label><input id="promo-percent" type="number" className="input" {...form.register('percentOff')} /><FieldError message={err('percentOff')} /></div>
            <div><label className="label" htmlFor="promo-end">Bookable until</label><input id="promo-end" type="date" className="input" {...form.register('validTo')} /><FieldError message={err('validTo')} /></div>
          </div>
          {(kind === 'EarlyBird' || kind === 'LastMinute') && (
            <div><label className="label" htmlFor="promo-days">{kind === 'EarlyBird' ? 'Booked at least this many days ahead' : 'Stay starts within this many days'}</label>
              <input id="promo-days" type="number" className="input" {...form.register('days', { setValueAs: emptyToUndefined })} /><FieldError message={err('days')} /></div>
          )}
          {kind === 'LongStay' && (
            <div><label className="label" htmlFor="promo-nights">Minimum nights</label>
              <input id="promo-nights" type="number" className="input" {...form.register('minNights', { setValueAs: emptyToUndefined })} /><FieldError message={err('minNights')} /></div>
          )}
          {kind === 'FlashSale' && (
            <div className="grid grid-cols-2 gap-3">
              <div><label className="label" htmlFor="promo-start">Stays starting from</label><input id="promo-start" type="date" className="input" {...form.register('stayStart')} /></div>
              <div><label className="label" htmlFor="promo-stay-end">until</label><input id="promo-stay-end" type="date" className="input" {...form.register('stayEnd')} /><FieldError message={err('stayEnd')} /></div>
            </div>
          )}
          {scope === 'host' && (
            <div><label className="label" htmlFor="promo-listing">Listing</label>
              <select id="promo-listing" className="input" {...form.register('propertyId')}>
                <option value="">All my listings</option>
                {listings.data?.map((l) => <option key={l.id} value={l.id}>{l.title}</option>)}
              </select></div>
          )}
          <div className="flex justify-end gap-2 pt-2">
            <button type="button" className="btn-secondary" onClick={() => setOpen(false)}>Cancel</button>
            <button className="btn-primary" disabled={create.isPending}>{create.isPending ? 'Saving…' : 'Create promotion'}</button>
          </div>
        </form>
      </Modal>
    </section>
  );
}

export function HostPromotionsPage() {
  return <PromotionsManager scope="host" />;
}
