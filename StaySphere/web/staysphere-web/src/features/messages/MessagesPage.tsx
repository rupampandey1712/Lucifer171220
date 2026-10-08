import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { clsx } from 'clsx';
import { ArrowLeft, Check, CheckCheck, Flag, MessageSquare, Send } from 'lucide-react';
import { useEffect, useRef, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router';
import { api } from '@/api/client';
import type { ConversationDto, MessageDto, Paged } from '@/api/types';
import { EmptyState, ErrorState, Skeleton } from '@/components/ui';
import { useAuthStore } from '@/lib/auth-store';
import { relativeTime } from '@/lib/format';
import { invokeRealtime, onRealtime } from '@/lib/realtime';
import { toast } from '@/lib/toast';

export function MessagesPage() {
  const { conversationId } = useParams();
  const conversations = useQuery({ queryKey: ['conversations'], queryFn: () => api<ConversationDto[]>('/conversations') });
  const active = conversations.data?.find((c) => c.id === conversationId);

  return (
    <div className="container-page py-6">
      <div className="grid h-[calc(100vh-9rem)] overflow-hidden rounded-2xl border border-slate-200 md:grid-cols-[340px_1fr]">
        <aside className={clsx('overflow-y-auto border-r border-slate-200', conversationId && 'hidden md:block')}>
          <h1 className="sticky top-0 border-b border-slate-200 bg-white px-5 py-4 text-xl font-bold">Messages</h1>
          {conversations.isLoading ? <div className="space-y-3 p-4">{Array.from({ length: 4 }, (_, i) => <Skeleton key={i} className="h-16" />)}</div>
            : conversations.isError ? <div className="p-4"><ErrorState error={conversations.error} /></div>
            : conversations.data!.length === 0 ? <p className="p-6 text-sm text-slate-500">No conversations yet. Message a host from any listing.</p>
            : (
              <ul>
                {conversations.data!.map((c) => (
                  <li key={c.id}>
                    <Link to={`/messages/${c.id}`} className={clsx('flex gap-3 border-b border-slate-100 px-5 py-4 hover:bg-slate-50', c.id === conversationId && 'bg-slate-100')}>
                      <span className="flex h-11 w-11 shrink-0 items-center justify-center rounded-full bg-slate-800 font-semibold text-white">{c.otherUserName[0]}</span>
                      <div className="min-w-0 flex-1">
                        <div className="flex justify-between gap-2"><p className="truncate font-semibold">{c.otherUserName}</p><span className="shrink-0 text-xs text-slate-500">{relativeTime(c.lastMessageAt)}</span></div>
                        <p className="truncate text-xs text-slate-500">{c.propertyTitle}</p>
                        <p className={clsx('truncate text-sm', c.unreadCount ? 'font-semibold text-slate-900' : 'text-slate-600')}>{c.lastMessage}</p>
                      </div>
                      {c.unreadCount > 0 && <span className="self-center rounded-full bg-coral-500 px-2 text-xs font-bold text-white">{c.unreadCount}</span>}
                    </Link>
                  </li>
                ))}
              </ul>
            )}
        </aside>
        <section className={clsx('flex min-h-0 flex-col', !conversationId && 'hidden md:flex')}>
          {conversationId ? <Chat conversationId={conversationId} conversation={active} />
            : <div className="flex flex-1 items-center justify-center p-8"><EmptyState icon={<MessageSquare className="h-7 w-7" />} title="Select a conversation" description="Your messages with hosts and guests appear here in real time." /></div>}
        </section>
      </div>
    </div>
  );
}

function Chat({ conversationId, conversation }: { conversationId: string; conversation?: ConversationDto }) {
  const me = useAuthStore((s) => s.user?.id);
  const qc = useQueryClient();
  const navigate = useNavigate();
  const [text, setText] = useState('');
  const [typing, setTyping] = useState(false);
  const bottom = useRef<HTMLDivElement>(null);
  const lastTyping = useRef(0);

  const messages = useQuery({ queryKey: ['messages', conversationId], queryFn: () => api<Paged<MessageDto>>(`/conversations/${conversationId}/messages?pageSize=100`) });

  useEffect(() => {
    api(`/conversations/${conversationId}/read`, { method: 'POST' }).then(() => qc.invalidateQueries({ queryKey: ['conversations'] })).catch(() => undefined);
    const offTyping = onRealtime('Typing', (e) => {
      if ((e as { conversationId: string }).conversationId !== conversationId) return;
      setTyping(true);
      setTimeout(() => setTyping(false), 3000);
    });
    const offRead = onRealtime('ReadReceipt', () => qc.invalidateQueries({ queryKey: ['messages', conversationId] }));
    const offMsg = onRealtime('ReceiveMessage', (m) => {
      if ((m as MessageDto).conversationId === conversationId && (m as MessageDto).senderId !== me) invokeRealtime('MarkRead', conversationId).catch(() => undefined);
    });
    return () => { offTyping(); offRead(); offMsg(); };
  }, [conversationId, qc, me]);

  useEffect(() => bottom.current?.scrollIntoView({ behavior: 'smooth' }), [messages.data?.items.length, typing]);

  const send = useMutation({
    mutationFn: (body: string) => api<MessageDto>(`/conversations/${conversationId}/messages`, { method: 'POST', body: { body } }),
    onMutate: async (body) => {
      // Optimistic append — the server copy replaces it on refetch.
      const key = ['messages', conversationId];
      const previous = qc.getQueryData<Paged<MessageDto>>(key);
      if (previous) qc.setQueryData(key, { ...previous, items: [...previous.items, { id: `tmp-${Date.now()}`, conversationId, senderId: me!, body, sentAt: new Date().toISOString(), isRead: false }] });
      return { previous };
    },
    onError: (e, _v, ctx) => { qc.setQueryData(['messages', conversationId], ctx?.previous); toast.error((e as Error).message); },
    onSettled: () => { qc.invalidateQueries({ queryKey: ['messages', conversationId] }); qc.invalidateQueries({ queryKey: ['conversations'] }); },
  });

  const report = useMutation({
    mutationFn: () => api(`/conversations/${conversationId}/report`, { method: 'POST', body: { targetType: 'Conversation', targetId: conversationId, reason: 'Reported from chat' } }),
    onSuccess: () => toast.success('Thanks — our trust & safety team will review this conversation.'),
  });

  const onType = (v: string) => {
    setText(v);
    if (Date.now() - lastTyping.current > 2000) {
      lastTyping.current = Date.now();
      invokeRealtime('Typing', conversationId).catch(() => undefined);
    }
  };

  return (
    <>
      <header className="flex items-center justify-between border-b border-slate-200 px-4 py-3">
        <div className="flex items-center gap-3">
          <button className="md:hidden" onClick={() => navigate('/messages')} aria-label="Back"><ArrowLeft className="h-5 w-5" /></button>
          <div><p className="font-semibold">{conversation?.otherUserName ?? 'Conversation'}</p><p className="text-xs text-slate-500">{conversation?.propertyTitle}</p></div>
        </div>
        <button className="btn-ghost !py-1.5 text-xs" onClick={() => report.mutate()}><Flag className="h-3.5 w-3.5" /> Report</button>
      </header>
      <div className="flex-1 space-y-3 overflow-y-auto bg-slate-50 p-4" aria-live="polite">
        {messages.isLoading && <Skeleton className="h-16 w-2/3" />}
        {messages.data?.items.map((m) => {
          const mine = m.senderId === me;
          return (
            <div key={m.id} className={clsx('flex', mine ? 'justify-end' : 'justify-start')}>
              <div className={clsx('max-w-[75%] rounded-2xl px-4 py-2.5 text-sm shadow-sm', mine ? 'rounded-br-md bg-brand-700 text-white' : 'rounded-bl-md bg-white')}>
                <p className="whitespace-pre-wrap break-words">{m.body}</p>
                <p className={clsx('mt-1 flex items-center justify-end gap-1 text-[10px]', mine ? 'text-teal-100' : 'text-slate-400')}>
                  {relativeTime(m.sentAt)} {mine && (m.isRead ? <CheckCheck className="h-3 w-3" aria-label="Read" /> : <Check className="h-3 w-3" aria-label="Sent" />)}
                </p>
              </div>
            </div>
          );
        })}
        {typing && <p className="text-xs text-slate-500 italic">{conversation?.otherUserName ?? 'They'} is typing…</p>}
        <div ref={bottom} />
      </div>
      <form className="flex gap-2 border-t border-slate-200 p-3" onSubmit={(e) => { e.preventDefault(); if (text.trim()) { send.mutate(text.trim()); setText(''); } }}>
        <input className="input" placeholder="Write a message…" value={text} onChange={(e) => onType(e.target.value)} maxLength={4000} aria-label="Message" />
        <button className="btn-primary" disabled={!text.trim()} aria-label="Send"><Send className="h-4 w-4" /></button>
      </form>
    </>
  );
}
