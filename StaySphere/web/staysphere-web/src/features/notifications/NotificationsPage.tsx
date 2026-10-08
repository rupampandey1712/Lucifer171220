import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { clsx } from 'clsx';
import { Bell } from 'lucide-react';
import { Link } from 'react-router';
import { api } from '@/api/client';
import type { NotificationDto, Paged } from '@/api/types';
import { EmptyState, ErrorState, PageHeader, Skeleton } from '@/components/ui';
import { relativeTime } from '@/lib/format';

export function NotificationsPage() {
  const qc = useQueryClient();
  const list = useQuery({ queryKey: ['notifications', 'list'], queryFn: () => api<Paged<NotificationDto>>('/notifications?pageSize=50') });
  const readAll = useMutation({ mutationFn: () => api('/notifications/read-all', { method: 'POST' }), onSuccess: () => qc.invalidateQueries({ queryKey: ['notifications'] }) });
  const readOne = (id: string) => api(`/notifications/${id}/read`, { method: 'POST' }).then(() => qc.invalidateQueries({ queryKey: ['notifications'] }));
  return (
    <div className="container-page max-w-3xl py-10">
      <PageHeader title="Notifications" actions={<button className="btn-secondary" onClick={() => readAll.mutate()}>Mark all as read</button>} />
      {list.isLoading ? <div className="space-y-3">{Array.from({ length: 5 }, (_, i) => <Skeleton key={i} className="h-16" />)}</div>
        : list.isError ? <ErrorState error={list.error} />
        : list.data!.items.length === 0 ? <EmptyState icon={<Bell className="h-7 w-7" />} title="You're all caught up" description="Booking updates, messages and reminders will show up here." />
        : (
          <ul className="divide-y divide-slate-100 overflow-hidden rounded-2xl border border-slate-200">
            {list.data!.items.map((n) => (
              <li key={n.id}>
                <Link to={n.link ?? '#'} onClick={() => !n.isRead && readOne(n.id)} className={clsx('flex gap-4 px-5 py-4 hover:bg-slate-50', !n.isRead && 'bg-brand-50/40')}>
                  <span className={clsx('mt-1.5 h-2.5 w-2.5 shrink-0 rounded-full', n.isRead ? 'bg-transparent' : 'bg-coral-500')} aria-label={n.isRead ? undefined : 'Unread'} />
                  <div className="flex-1"><p className="font-semibold">{n.title}</p><p className="text-sm text-slate-600">{n.body}</p></div>
                  <span className="shrink-0 text-xs text-slate-500">{relativeTime(n.createdAt)}</span>
                </Link>
              </li>
            ))}
          </ul>
        )}
    </div>
  );
}
