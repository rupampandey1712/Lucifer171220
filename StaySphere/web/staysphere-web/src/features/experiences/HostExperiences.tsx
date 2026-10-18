import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Plus, Sparkles } from 'lucide-react';
import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { Link } from 'react-router';
import { z } from 'zod';
import { api, ApiError } from '@/api/client';
import type { ExperienceDetail } from '@/api/types';
import { EmptyState, ErrorState, FieldError, Modal, PageHeader, Skeleton, StatusBadge } from '@/components/ui';
import { dateTime, money } from '@/lib/format';
import { toast } from '@/lib/toast';
import { experienceCategories } from './ExperiencesPages';

const schema = z.object({
  title: z.string().trim().min(5, 'At least 5 characters').max(120),
  description: z.string().trim().min(30, 'Describe it in at least 30 characters').max(4000),
  category: z.enum(experienceCategories),
  city: z.string().trim().min(1, 'Required').max(100),
  countryCode: z.string().trim().length(2, '2-letter code, e.g. PT'),
  meetingPoint: z.string().trim().min(1, 'Required').max(300),
  durationMinutes: z.coerce.number().int().min(30, 'At least 30 minutes').max(720, 'At most 12 hours'),
  maxGuests: z.coerce.number().int().min(1).max(30),
  pricePerGuest: z.coerce.number().positive('Set a price'),
  currency: z.string().trim().length(3),
  imageUrl: z.union([z.literal(''), z.string().url().startsWith('https://', 'Use an https image URL')]).optional(),
});
type FormValues = z.input<typeof schema>;

function SlotForm({ experience }: { experience: ExperienceDetail }) {
  const qc = useQueryClient();
  const [startsAt, setStartsAt] = useState('');
  const [capacity, setCapacity] = useState('');
  const add = useMutation({
    mutationFn: () => api(`/host/experiences/${experience.id}/slots`, {
      method: 'POST', body: { startsAt: new Date(startsAt).toISOString(), capacity: capacity ? Number(capacity) : null },
    }),
    onSuccess: () => { toast.success('Session added'); setStartsAt(''); void qc.invalidateQueries({ queryKey: ['host', 'experiences'] }); },
    onError: (e) => toast.error((e as Error).message),
  });
  return (
    <form className="mt-3 flex flex-wrap items-end gap-2" onSubmit={(e) => { e.preventDefault(); if (startsAt) add.mutate(); }}>
      <label className="text-xs font-medium">Start<input type="datetime-local" className="input mt-1" value={startsAt} onChange={(e) => setStartsAt(e.target.value)} required /></label>
      <label className="text-xs font-medium">Seats<input type="number" min={1} max={experience.maxGuests} placeholder={String(experience.maxGuests)} className="input mt-1 w-24" value={capacity} onChange={(e) => setCapacity(e.target.value)} /></label>
      <button className="btn-secondary" disabled={add.isPending}>Add session</button>
    </form>
  );
}

export function HostExperiencesPage() {
  const qc = useQueryClient();
  const [open, setOpen] = useState(false);
  const list = useQuery({ queryKey: ['host', 'experiences'], queryFn: () => api<ExperienceDetail[]>('/host/experiences') });
  const form = useForm<FormValues>({ resolver: zodResolver(schema), defaultValues: { category: 'Food', durationMinutes: 120, maxGuests: 8, currency: 'EUR', countryCode: '' } });
  const create = useMutation({
    mutationFn: (v: FormValues) => { const p = schema.parse(v); return api<ExperienceDetail>('/host/experiences', { method: 'POST', body: { ...p, imageUrl: p.imageUrl || null } }); },
    onSuccess: () => { toast.success('Draft created. Add a session, then publish.'); setOpen(false); form.reset(); void qc.invalidateQueries({ queryKey: ['host', 'experiences'] }); },
    onError: (e) => toast.error(e instanceof ApiError ? e.message : 'Could not create the experience'),
  });
  const publish = useMutation({
    mutationFn: (id: string) => api(`/host/experiences/${id}/publish`, { method: 'POST', body: {} }),
    onSuccess: () => { toast.success('Published'); void qc.invalidateQueries({ queryKey: ['host', 'experiences'] }); },
    onError: (e) => toast.error((e as Error).message),
  });
  const err = (name: keyof FormValues) => form.formState.errors[name]?.message as string | undefined;
  const field = (name: keyof FormValues, label: string, props: React.InputHTMLAttributes<HTMLInputElement> = {}) => (
    <label className="block text-sm font-medium">{label}<input className="input mt-1" {...props} {...form.register(name)} /><FieldError message={err(name)} /></label>
  );

  return (
    <section>
      <PageHeader title="Experiences" subtitle="Host tours, classes and activities. Guests book seats in your sessions and pay you on the day."
        actions={<button className="btn-primary" onClick={() => setOpen(true)}><Plus className="h-4 w-4" /> New experience</button>} />
      {list.isLoading ? <Skeleton className="h-40" /> : list.isError ? <ErrorState error={list.error} onRetry={() => list.refetch()} />
        : list.data!.length === 0 ? <EmptyState icon={<Sparkles className="h-7 w-7" />} title="No experiences yet" description="Create one, add sessions, then publish it." />
        : (
          <div className="space-y-4">
            {list.data!.map((x) => {
              const booked = x.slots.reduce((sum, s) => sum + (s.capacity - s.remaining), 0);
              return (
                <article key={x.id} className="card p-5">
                  <div className="flex flex-wrap items-start justify-between gap-3">
                    <div>
                      <Link to={`/experiences/${x.id}`} className="font-semibold hover:underline">{x.title}</Link>
                      <p className="text-sm text-slate-600">{x.category} · {x.city} · {money(x.pricePerGuest, x.currency)} / guest · {booked} seat{booked === 1 ? '' : 's'} booked</p>
                    </div>
                    <div className="flex items-center gap-2">
                      <StatusBadge status={x.status} />
                      {x.status !== 'Published' && <button className="btn-secondary" disabled={publish.isPending} onClick={() => publish.mutate(x.id)}>Publish</button>}
                    </div>
                  </div>
                  {x.slots.length > 0 && (
                    <ul className="mt-3 flex flex-wrap gap-2 text-xs">
                      {x.slots.slice(0, 12).map((s) => <li key={s.id} className="rounded-full bg-slate-100 px-3 py-1">{dateTime(s.startsAt)} · {s.capacity - s.remaining}/{s.capacity}</li>)}
                      {x.slots.length > 12 && <li className="px-2 py-1 text-slate-500">+{x.slots.length - 12} more</li>}
                    </ul>
                  )}
                  <SlotForm experience={x} />
                </article>
              );
            })}
          </div>
        )}

      <Modal open={open} onClose={() => setOpen(false)} title="New experience" wide>
        <form className="grid gap-3 sm:grid-cols-2" onSubmit={form.handleSubmit((v) => create.mutate(v))} noValidate>
          <div className="sm:col-span-2">{field('title', 'Title')}</div>
          <label className="block text-sm font-medium sm:col-span-2">Description<textarea className="input mt-1" rows={4} {...form.register('description')} /><FieldError message={err('description')} /></label>
          <label className="block text-sm font-medium">Category
            <select className="input mt-1" {...form.register('category')}>{experienceCategories.map((c) => <option key={c}>{c}</option>)}</select>
          </label>
          {field('meetingPoint', 'Meeting point')}
          {field('city', 'City')}
          {field('countryCode', 'Country code', { maxLength: 2 })}
          {field('durationMinutes', 'Duration (minutes)', { type: 'number', min: 30, max: 720 })}
          {field('maxGuests', 'Group size', { type: 'number', min: 1, max: 30 })}
          {field('pricePerGuest', 'Price per guest', { type: 'number', min: 1, step: '0.01' })}
          {field('currency', 'Currency', { maxLength: 3 })}
          <div className="sm:col-span-2">{field('imageUrl', 'Photo URL (https, optional)')}</div>
          <div className="sm:col-span-2"><button className="btn-primary" disabled={create.isPending}>{create.isPending ? 'Creating…' : 'Create draft'}</button></div>
        </form>
      </Modal>
    </section>
  );
}
