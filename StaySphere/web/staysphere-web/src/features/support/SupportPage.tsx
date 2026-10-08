import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { LifeBuoy } from 'lucide-react';
import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { useSearchParams } from 'react-router';
import { api } from '@/api/client';
import type { Paged, TicketDto } from '@/api/types';
import { EmptyState, ErrorState, Modal, PageHeader, Skeleton, StatusBadge } from '@/components/ui';
import { hasRole, useAuthStore } from '@/lib/auth-store';
import { dateTime, relativeTime } from '@/lib/format';
import { toast } from '@/lib/toast';

export function SupportPage() {
  const user = useAuthStore((s) => s.user);
  const isAgent = hasRole(user, 'Support') || hasRole(user, 'Admin');
  const [params] = useSearchParams();
  const qc = useQueryClient();
  const [selected, setSelected] = useState<string | null>(null);
  const tickets = useQuery({ queryKey: ['tickets'], queryFn: () => api<Paged<TicketDto>>('/support/tickets?pageSize=50') });
  const form = useForm({ defaultValues: { category: 'Booking', priority: 'Normal', subject: '', description: '' } });
  const create = useMutation({
    mutationFn: (v: object) => api<TicketDto>('/support/tickets', { method: 'POST', body: { ...v, reservationId: params.get('reservationId') } }),
    onSuccess: () => { toast.success('Ticket created — we usually reply within a few hours.'); form.reset(); qc.invalidateQueries({ queryKey: ['tickets'] }); },
    onError: (e) => toast.error((e as Error).message),
  });

  return (
    <div className="container-page py-10">
      <PageHeader title={isAgent ? 'Support queue' : 'Help & support'} subtitle={isAgent ? 'Tickets from guests and hosts, highest priority first.' : 'Our team is here to help with bookings, payments and safety.'} />
      <div className="grid gap-8 lg:grid-cols-[1fr_380px]">
        <section>
          {tickets.isLoading ? <Skeleton className="h-40" /> : tickets.isError ? <ErrorState error={tickets.error} />
            : tickets.data!.items.length === 0 ? <EmptyState icon={<LifeBuoy className="h-7 w-7" />} title="No tickets yet" description="If something goes wrong with a stay, open a ticket and we'll help." />
            : (
              <ul className="divide-y divide-slate-100 overflow-hidden rounded-2xl border border-slate-200">
                {tickets.data!.items.map((t) => (
                  <li key={t.id}><button className="flex w-full items-center gap-4 px-5 py-4 text-left hover:bg-slate-50" onClick={() => setSelected(t.id)}>
                    <div className="flex-1"><p className="font-semibold">{t.subject}</p><p className="text-sm text-slate-500">{t.category} · {isAgent ? `${t.userName} · ` : ''}{relativeTime(t.updatedAt)}</p></div>
                    <span className="text-xs text-slate-500">{t.priority}</span><StatusBadge status={t.status} />
                  </button></li>
                ))}
              </ul>
            )}
        </section>
        {!isAgent && (
          <aside className="card h-fit p-6">
            <h2 className="mb-4 text-lg font-semibold">Open a ticket</h2>
            <form className="space-y-3" onSubmit={form.handleSubmit((v) => create.mutate(v))}>
              <div className="grid grid-cols-2 gap-3">
                <select className="input" aria-label="Category" {...form.register('category')}>{['Booking', 'Payments', 'Refunds', 'Account', 'Safety', 'Listing'].map((c) => <option key={c}>{c}</option>)}</select>
                <select className="input" aria-label="Priority" {...form.register('priority')}>{['Low', 'Normal', 'High', 'Urgent'].map((c) => <option key={c}>{c}</option>)}</select>
              </div>
              <input className="input" placeholder="Subject" aria-label="Subject" {...form.register('subject', { required: true })} />
              <textarea className="input min-h-28" placeholder="Describe the issue" aria-label="Description" {...form.register('description', { required: true })} />
              {params.get('reservationId') && <p className="text-xs text-slate-500">Linked to reservation {params.get('reservationId')!.slice(0, 8)}…</p>}
              <button className="btn-primary w-full" disabled={create.isPending}>Submit</button>
            </form>
          </aside>
        )}
      </div>
      {selected && <TicketModal id={selected} isAgent={isAgent} onClose={() => setSelected(null)} />}
    </div>
  );
}

function TicketModal({ id, isAgent, onClose }: { id: string; isAgent: boolean; onClose: () => void }) {
  const qc = useQueryClient();
  const [reply, setReply] = useState('');
  const ticket = useQuery({ queryKey: ['ticket', id], queryFn: () => api<TicketDto>(`/support/tickets/${id}`) });
  const send = useMutation({ mutationFn: () => api(`/support/tickets/${id}/messages`, { method: 'POST', body: { body: reply } }), onSuccess: () => { setReply(''); qc.invalidateQueries({ queryKey: ['ticket', id] }); qc.invalidateQueries({ queryKey: ['tickets'] }); } });
  const update = useMutation({ mutationFn: (status: string) => api(`/support/tickets/${id}`, { method: 'PATCH', body: { status, priority: ticket.data!.priority, assigneeId: ticket.data!.assigneeId } }), onSuccess: () => { qc.invalidateQueries({ queryKey: ['ticket', id] }); qc.invalidateQueries({ queryKey: ['tickets'] }); } });
  const t = ticket.data;
  return (
    <Modal open onClose={onClose} title={t?.subject ?? 'Ticket'} wide>
      {!t ? <Skeleton className="h-40" /> : (
        <>
          <div className="mb-4 flex flex-wrap items-center gap-3 text-sm"><StatusBadge status={t.status} /><span>{t.category} · {t.priority} priority · opened {dateTime(t.createdAt)}</span>
            {isAgent && <select className="input !w-auto !py-1" value={t.status} onChange={(e) => update.mutate(e.target.value)} aria-label="Status">{['Open', 'InProgress', 'WaitingForUser', 'Resolved', 'Closed'].map((s) => <option key={s}>{s}</option>)}</select>}
          </div>
          <div className="space-y-3">
            <div className="rounded-xl bg-slate-50 p-4 text-sm"><p className="mb-1 font-semibold">{t.userName}</p>{t.description}</div>
            {t.messages.map((m) => <div key={m.id} className={m.fromAgent ? 'rounded-xl bg-brand-50 p-4 text-sm' : 'rounded-xl bg-slate-50 p-4 text-sm'}><p className="mb-1 font-semibold">{m.authorName}{m.fromAgent && ' (StaySphere Support)'} <span className="font-normal text-slate-500">· {relativeTime(m.at)}</span></p>{m.body}</div>)}
          </div>
          <form className="mt-4 flex gap-2" onSubmit={(e) => { e.preventDefault(); if (reply.trim()) send.mutate(); }}>
            <input className="input" placeholder="Write a reply…" value={reply} onChange={(e) => setReply(e.target.value)} aria-label="Reply" />
            <button className="btn-primary" disabled={!reply.trim()}>Send</button>
          </form>
        </>
      )}
    </Modal>
  );
}
