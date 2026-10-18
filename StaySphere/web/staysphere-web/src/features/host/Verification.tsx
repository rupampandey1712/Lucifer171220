import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { BadgeCheck, Clock } from 'lucide-react';
import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { z } from 'zod';
import { api, ApiError } from '@/api/client';
import type { HostVerificationDto, Paged } from '@/api/types';
import { Badge, DataTable, ErrorState, FieldError, Modal, PageHeader, Skeleton } from '@/components/ui';
import { prettyDate } from '@/lib/format';
import { toast } from '@/lib/toast';

const schema = z.object({
  legalName: z.string().trim().min(3, 'Enter your full legal name').max(120),
  note: z.string().max(1000).optional(),
});
type FormValues = z.infer<typeof schema>;

/** Small "Verified host" badge shown next to a host's name. */
export function VerifiedHostBadge() {
  return <Badge tone="green"><BadgeCheck className="mr-1 inline h-3.5 w-3.5" />Verified host</Badge>;
}

export function HostVerificationPage() {
  const qc = useQueryClient();
  const status = useQuery({ queryKey: ['host', 'verification'], queryFn: () => api<HostVerificationDto>('/host/verification') });
  const form = useForm<FormValues>({ resolver: zodResolver(schema) });
  const submit = useMutation({
    mutationFn: (v: FormValues) => api<HostVerificationDto>('/host/verification', { method: 'POST', body: { legalName: v.legalName, note: v.note || null } }),
    onSuccess: (d) => { qc.setQueryData(['host', 'verification'], d); toast.success('Request sent. An admin will review it shortly.'); },
    onError: (e) => toast.error(e instanceof ApiError ? e.message : 'Could not send the request'),
  });

  return (
    <section className="max-w-2xl">
      <PageHeader title="Host verification" subtitle="Verified hosts get a badge on every listing, which helps guests book with confidence." />
      {status.isLoading ? <Skeleton className="h-40" /> : status.isError ? <ErrorState error={status.error} onRetry={() => status.refetch()} /> : (() => {
        const v = status.data!;
        if (v.status === 'Verified') return (
          <div className="card flex items-center gap-3 p-6"><VerifiedHostBadge /><p className="text-sm text-slate-600">Verified {v.verifiedAt ? prettyDate(v.verifiedAt) : ''}.</p></div>);
        if (v.status === 'Pending') return (
          <div className="card flex items-center gap-3 p-6"><Clock className="h-5 w-5 text-amber-600" />
            <p className="text-sm text-slate-600">Your request from {v.submittedAt ? prettyDate(v.submittedAt) : 'recently'} is being reviewed.</p></div>);
        return (
          <form className="card space-y-4 p-6" onSubmit={form.handleSubmit((d) => submit.mutate(d))} noValidate>
            {v.status === 'Rejected' && <p className="rounded-lg bg-red-50 p-3 text-sm text-red-700" role="alert">Your last request was not approved: {v.note}</p>}
            <label className="block text-sm font-medium">Full legal name
              <input className="input mt-1" {...form.register('legalName')} autoComplete="name" />
            </label>
            <FieldError message={form.formState.errors.legalName?.message} />
            <label className="block text-sm font-medium">Anything the reviewer should know (optional)
              <textarea className="input mt-1" rows={3} {...form.register('note')} />
            </label>
            <p className="text-xs text-slate-500">You need a confirmed email address. Never send ID documents through messages.</p>
            <button className="btn-primary" disabled={submit.isPending}>{submit.isPending ? 'Sending…' : 'Request verification'}</button>
          </form>);
      })()}
    </section>
  );
}

/** Admin queue of pending host verification requests. */
export function HostVerificationQueue() {
  const qc = useQueryClient();
  const [rejecting, setRejecting] = useState<HostVerificationDto | null>(null);
  const [reason, setReason] = useState('');
  const queue = useQuery({ queryKey: ['admin', 'host-verifications'], queryFn: () => api<Paged<HostVerificationDto>>('/admin/host-verifications') });
  const review = useMutation({
    mutationFn: (v: { userId: string; approve: boolean; reason?: string }) =>
      api<HostVerificationDto>(`/admin/host-verifications/${v.userId}`, { method: 'POST', body: { approve: v.approve, reason: v.reason ?? null } }),
    onSuccess: (d) => { toast.success(d.status === 'Verified' ? 'Host verified' : 'Request rejected'); setRejecting(null); setReason(''); qc.invalidateQueries({ queryKey: ['admin', 'host-verifications'] }); },
    onError: (e) => toast.error((e as Error).message),
  });

  return (
    <section className="mt-10">
      <h2 className="mb-3 font-semibold">Host verification requests</h2>
      {queue.isLoading ? <Skeleton className="h-24" /> : queue.isError ? <ErrorState error={queue.error} onRetry={() => queue.refetch()} /> : (
        <DataTable rows={queue.data!.items} rowKey={(r) => r.userId} empty="No pending requests."
          columns={[
            { header: 'Host', cell: (r) => <div><p className="font-medium">{r.displayName}</p><p className="text-xs text-slate-500">{r.email}</p></div> },
            { header: 'Legal name', cell: (r) => r.legalName },
            { header: 'Note', cell: (r) => <span className="text-xs">{r.note}</span> },
            { header: 'Submitted', cell: (r) => (r.submittedAt ? prettyDate(r.submittedAt) : '') },
            { header: '', cell: (r) => (
              <div className="flex gap-2">
                <button className="btn-secondary" disabled={review.isPending} onClick={() => review.mutate({ userId: r.userId, approve: true })}>Approve</button>
                <button className="btn-ghost text-red-700" onClick={() => setRejecting(r)}>Reject</button>
              </div>) },
          ]} />)}
      <Modal open={rejecting !== null} onClose={() => setRejecting(null)} title={`Reject ${rejecting?.displayName ?? ''}`}>
        <label className="block text-sm font-medium">Reason (sent to the host)
          <textarea className="input mt-1" rows={3} value={reason} onChange={(e) => setReason(e.target.value)} />
        </label>
        <button className="btn-primary mt-4" disabled={!reason.trim() || review.isPending}
          onClick={() => rejecting && review.mutate({ userId: rejecting.userId, approve: false, reason })}>Reject request</button>
      </Modal>
    </section>
  );
}
