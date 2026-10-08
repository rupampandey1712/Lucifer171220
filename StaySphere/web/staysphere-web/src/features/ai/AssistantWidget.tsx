import { useMutation } from '@tanstack/react-query';
import { clsx } from 'clsx';
import { Bot, Send, Sparkles, X } from 'lucide-react';
import { useEffect, useRef, useState } from 'react';
import { Link, useNavigate } from 'react-router';
import { api } from '@/api/client';
import type { AssistantReply, ConfirmActionResult } from '@/api/types';
import { Spinner, SafeImg } from '@/components/ui';
import { useAuthStore } from '@/lib/auth-store';
import { money } from '@/lib/format';
import { useFeatureFlags } from '@/lib/hooks';
import { toast } from '@/lib/toast';

interface Turn { role: 'user' | 'assistant'; text: string; reply?: AssistantReply }

const SUGGESTIONS = ['Find a family-friendly place in Goa for 4 people under ₹8,000 per night', 'Find me a 2-bedroom apartment in London under £400 with wifi', 'Which places in Lisbon have parking?', 'Plan a 5-day trip to Kyoto'];

export function AssistantWidget() {
  const flags = useFeatureFlags();
  const user = useAuthStore((s) => s.user);
  const navigate = useNavigate();
  const [open, setOpen] = useState(false);
  const [input, setInput] = useState('');
  const [turns, setTurns] = useState<Turn[]>([{ role: 'assistant', text: "Hi! I'm your StaySphere travel assistant. Tell me where you're going, your dates, group size and budget — I'll search real listings for you." }]);
  const [conversationId, setConversationId] = useState<string | null>(null);
  const bottom = useRef<HTMLDivElement>(null);
  useEffect(() => bottom.current?.scrollIntoView({ behavior: 'smooth' }), [turns]);

  const send = useMutation({
    mutationFn: (message: string) => api<AssistantReply>('/ai/messages', { method: 'POST', body: { conversationId, message }, timeoutMs: 90000 }),
    onSuccess: (reply) => { setConversationId(reply.conversationId); setTurns((t) => [...t, { role: 'assistant', text: reply.reply, reply }]); },
    onError: (e) => setTurns((t) => [...t, { role: 'assistant', text: `Sorry — ${(e as Error).message}` }]),
  });

  // Human-in-the-loop: nothing is booked until the user presses Confirm (server re-checks price & availability).
  const confirm = useMutation({
    mutationFn: ({ id, approve }: { id: string; approve: boolean }) => api<ConfirmActionResult>(`/ai/actions/${id}/confirm`, { method: 'POST', body: { approve } }),
    onSuccess: (r) => {
      setTurns((t) => [...t.map((x) => (x.reply?.pendingAction ? { ...x, reply: { ...x.reply, pendingAction: undefined } } : x)), { role: 'assistant', text: r.message }]);
      if (r.reservationId) { setOpen(false); navigate(`/book/${r.reservationId}`); }
    },
    onError: (e) => toast.error((e as Error).message),
  });

  if (flags.data && !flags.data.AiAssistant) return null;
  const submit = (text: string) => {
    if (!text.trim() || send.isPending) return;
    setTurns((t) => [...t, { role: 'user', text }]);
    setInput('');
    send.mutate(text);
  };

  return (
    <>
      <button onClick={() => setOpen((o) => !o)} aria-expanded={open} aria-label="Open AI travel assistant"
        className="fixed right-5 bottom-5 z-[900] flex items-center gap-2 rounded-full bg-gradient-to-r from-brand-700 to-teal-500 px-5 py-3.5 font-semibold text-white shadow-2xl transition hover:scale-105">
        {open ? <X className="h-5 w-5" /> : <Sparkles className="h-5 w-5" />} <span className="hidden sm:inline">{open ? 'Close' : 'Ask AI'}</span>
      </button>
      {open && (
        <section role="dialog" aria-label="AI travel assistant" className="fixed right-5 bottom-24 z-[900] flex h-[min(640px,75vh)] w-[min(420px,calc(100vw-2.5rem))] flex-col overflow-hidden rounded-3xl border border-slate-200 bg-white shadow-2xl animate-fade-in">
          <header className="flex items-center gap-3 bg-gradient-to-r from-brand-800 to-brand-600 px-5 py-4 text-white">
            <Bot className="h-6 w-6" /><div><p className="font-semibold">Travel assistant</p><p className="text-xs text-teal-100">Answers from live listings · never books without your OK</p></div>
          </header>
          <div className="flex-1 space-y-4 overflow-y-auto p-4" aria-live="polite">
            {turns.map((t, i) => (
              <div key={i} className={clsx('flex', t.role === 'user' ? 'justify-end' : 'justify-start')}>
                <div className={clsx('max-w-[90%] rounded-2xl px-4 py-2.5 text-sm', t.role === 'user' ? 'rounded-br-md bg-slate-900 text-white' : 'rounded-bl-md bg-slate-100')}>
                  <p className="whitespace-pre-wrap">{t.text.replace(/\*\*/g, '')}</p>
                  {t.reply && t.reply.properties.length > 0 && (
                    <div className="mt-3 space-y-2">
                      {t.reply.properties.slice(0, 3).map((p) => (
                        <Link key={p.id} to={`/property/${p.id}`} className="flex gap-3 rounded-xl bg-white p-2 shadow-sm hover:ring-1 hover:ring-slate-300">
                          {p.imageUrl && <SafeImg src={p.imageUrl} alt="" className="h-14 w-16 rounded-lg object-cover" />}
                          <div className="min-w-0 text-xs"><p className="truncate font-semibold">{p.title}</p><p className="text-slate-500">{p.city} · {p.rating ? `${p.rating.toFixed(1)}★` : 'New'}</p><p className="font-semibold">{money(p.nightlyPrice, p.currency, true)} / night</p></div>
                        </Link>
                      ))}
                    </div>
                  )}
                  {t.reply?.pendingAction && (
                    <div className="mt-3 rounded-xl border border-amber-300 bg-amber-50 p-3">
                      <p className="text-xs font-semibold text-amber-900">Confirmation required</p>
                      <p className="mt-1 text-xs text-amber-900">{t.reply.pendingAction.summary}</p>
                      {user ? (
                        <div className="mt-2 flex gap-2">
                          <button className="btn-primary !py-1.5 text-xs" disabled={confirm.isPending} onClick={() => confirm.mutate({ id: t.reply!.pendingAction!.id, approve: true })}>Confirm hold</button>
                          <button className="btn-secondary !py-1.5 text-xs" onClick={() => confirm.mutate({ id: t.reply!.pendingAction!.id, approve: false })}>No thanks</button>
                        </div>
                      ) : <Link to="/login" className="btn-primary mt-2 !py-1.5 text-xs">Log in to book</Link>}
                    </div>
                  )}
                  {t.reply && t.reply.toolsUsed.length > 0 && <p className="mt-2 text-[10px] text-slate-400">tools: {t.reply.toolsUsed.join(', ')} · {t.reply.provider}</p>}
                </div>
              </div>
            ))}
            {send.isPending && <div className="flex items-center gap-2 text-sm text-slate-500"><Spinner className="h-4 w-4" /> Searching listings…</div>}
            {turns.length === 1 && (
              <div className="flex flex-wrap gap-2">{SUGGESTIONS.map((s) => <button key={s} className="rounded-full border border-slate-200 px-3 py-1.5 text-left text-xs hover:bg-slate-50" onClick={() => submit(s)}>{s}</button>)}</div>
            )}
            <div ref={bottom} />
          </div>
          <form className="flex gap-2 border-t border-slate-200 p-3" onSubmit={(e) => { e.preventDefault(); submit(input); }}>
            <input className="input" placeholder="e.g. Cabin in Banff for 2, 2027-01-10 to 2027-01-14" value={input} onChange={(e) => setInput(e.target.value)} maxLength={1000} aria-label="Message the assistant" />
            <button className="btn-primary" disabled={!input.trim() || send.isPending} aria-label="Send"><Send className="h-4 w-4" /></button>
          </form>
        </section>
      )}
    </>
  );
}
