import { clsx } from 'clsx';
import { AlertTriangle, CheckCircle2, Info, Loader2, SearchX, Star, X } from 'lucide-react';
import { useEffect, useRef, type ReactNode } from 'react';
import { ApiError } from '@/api/client';
import { useToasts } from '@/lib/toast';

export function Spinner({ className }: { className?: string }) {
  return <Loader2 className={clsx('animate-spin', className ?? 'h-5 w-5')} aria-hidden />;
}

export function Skeleton({ className }: { className?: string }) {
  return <div className={clsx('animate-pulse rounded-xl bg-slate-200/80', className)} aria-hidden />;
}

export function PageLoader() {
  return (
    <div className="flex min-h-[40vh] items-center justify-center" role="status" aria-live="polite">
      <Spinner className="h-8 w-8 text-brand-700" />
      <span className="sr-only">Loading…</span>
    </div>
  );
}

export function EmptyState({ icon, title, description, action }: { icon?: ReactNode; title: string; description?: string; action?: ReactNode }) {
  return (
    <div className="flex flex-col items-center justify-center rounded-2xl border border-dashed border-slate-300 px-6 py-16 text-center animate-fade-in">
      <div className="mb-4 flex h-14 w-14 items-center justify-center rounded-full bg-brand-50 text-brand-700">{icon ?? <SearchX className="h-7 w-7" />}</div>
      <h3 className="text-lg font-semibold">{title}</h3>
      {description && <p className="mt-1 max-w-md text-sm text-slate-600">{description}</p>}
      {action && <div className="mt-6">{action}</div>}
    </div>
  );
}

export function ErrorState({ error, onRetry }: { error: unknown; onRetry?: () => void }) {
  const message = error instanceof ApiError ? error.message : 'Something went wrong. Please try again.';
  const status = error instanceof ApiError ? error.status : undefined;
  return (
    <div role="alert" className="flex flex-col items-center rounded-2xl border border-red-200 bg-red-50 px-6 py-10 text-center">
      <AlertTriangle className="mb-3 h-8 w-8 text-red-600" />
      <p className="font-semibold text-red-900">{status === 404 ? 'Not found' : status === 403 ? 'Access denied' : "We couldn't load this"}</p>
      <p className="mt-1 text-sm text-red-800">{message}</p>
      {onRetry && (
        <button className="btn-secondary mt-4" onClick={onRetry}>
          Try again
        </button>
      )}
    </div>
  );
}

export function FieldError({ message }: { message?: string }) {
  return message ? <p className="mt-1 text-xs text-red-600" role="alert">{message}</p> : null;
}

export function Rating({ value, count, size = 'sm' }: { value: number; count?: number; size?: 'sm' | 'lg' }) {
  if (!count && !value) return <span className="text-sm text-slate-500">New</span>;
  return (
    <span className={clsx('inline-flex items-center gap-1 font-medium', size === 'lg' ? 'text-base' : 'text-sm')} aria-label={`Rated ${value.toFixed(2)} out of 5`}>
      <Star className={clsx('fill-current text-slate-900', size === 'lg' ? 'h-4.5 w-4.5' : 'h-3.5 w-3.5')} aria-hidden />
      {value.toFixed(2)}
      {count !== undefined && <span className="font-normal text-slate-500">({count})</span>}
    </span>
  );
}

export function Badge({ children, tone = 'slate' }: { children: ReactNode; tone?: 'slate' | 'green' | 'amber' | 'red' | 'brand' | 'blue' }) {
  const tones = {
    slate: 'bg-slate-100 text-slate-700',
    green: 'bg-emerald-50 text-emerald-700 ring-emerald-200',
    amber: 'bg-amber-50 text-amber-800 ring-amber-200',
    red: 'bg-red-50 text-red-700 ring-red-200',
    brand: 'bg-brand-50 text-brand-800 ring-brand-200',
    blue: 'bg-sky-50 text-sky-800 ring-sky-200',
  };
  return <span className={clsx('inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-semibold ring-1 ring-transparent ring-inset', tones[tone])}>{children}</span>;
}

export function StatusBadge({ status }: { status: string }) {
  const tone =
    ['Confirmed', 'Completed', 'Published', 'Succeeded', 'Active', 'Resolved', 'Refunded', 'Allow'].includes(status) ? 'green'
    : ['Held', 'PaymentPending', 'Pending', 'RefundPending', 'Draft', 'InProgress', 'WaitingForUser', 'Review', 'Open', 'PartiallyRefunded'].includes(status) ? 'amber'
    : ['Cancelled', 'Failed', 'Expired', 'Suspended', 'Hidden', 'Block', 'Deleted', 'Closed'].includes(status) ? 'red'
    : 'slate';
  return <Badge tone={tone}>{status.replace(/([a-z])([A-Z])/g, '$1 $2')}</Badge>;
}

export function Modal({ open, onClose, title, children, wide }: { open: boolean; onClose: () => void; title: string; children: ReactNode; wide?: boolean }) {
  const ref = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (!open) return;
    const prev = document.activeElement as HTMLElement | null;
    ref.current?.focus();
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && onClose();
    document.addEventListener('keydown', onKey);
    document.body.style.overflow = 'hidden';
    return () => {
      document.removeEventListener('keydown', onKey);
      document.body.style.overflow = '';
      prev?.focus();
    };
  }, [open, onClose]);
  if (!open) return null;
  return (
    <div className="fixed inset-0 z-[1000] flex items-end justify-center bg-slate-900/50 p-0 sm:items-center sm:p-4" onMouseDown={onClose}>
      <div
        ref={ref}
        tabIndex={-1}
        role="dialog"
        aria-modal="true"
        aria-label={title}
        onMouseDown={(e) => e.stopPropagation()}
        className={clsx('max-h-[92vh] w-full overflow-y-auto rounded-t-2xl bg-white shadow-2xl outline-none animate-fade-in sm:rounded-2xl', wide ? 'sm:max-w-3xl' : 'sm:max-w-lg')}
      >
        <div className="sticky top-0 z-10 flex items-center justify-between border-b border-slate-100 bg-white px-5 py-4">
          <h2 className="text-lg font-semibold">{title}</h2>
          <button className="rounded-full p-1.5 hover:bg-slate-100" onClick={onClose} aria-label="Close">
            <X className="h-5 w-5" />
          </button>
        </div>
        <div className="p-5">{children}</div>
      </div>
    </div>
  );
}

export function Toaster() {
  const { toasts, dismiss } = useToasts();
  return (
    <div className="pointer-events-none fixed bottom-4 left-1/2 z-[2000] flex w-full max-w-sm -translate-x-1/2 flex-col gap-2 px-4" aria-live="polite">
      {toasts.map((t) => (
        <div key={t.id} className="pointer-events-auto flex items-start gap-3 rounded-xl bg-slate-900 px-4 py-3 text-sm text-white shadow-xl animate-fade-in">
          {t.kind === 'success' ? <CheckCircle2 className="h-5 w-5 shrink-0 text-emerald-400" /> : t.kind === 'error' ? <AlertTriangle className="h-5 w-5 shrink-0 text-red-400" /> : <Info className="h-5 w-5 shrink-0 text-sky-400" />}
          <span className="flex-1">{t.message}</span>
          <button onClick={() => dismiss(t.id)} aria-label="Dismiss" className="opacity-70 hover:opacity-100">
            <X className="h-4 w-4" />
          </button>
        </div>
      ))}
    </div>
  );
}

export function Pagination({ page, totalPages, onChange }: { page: number; totalPages: number; onChange: (page: number) => void }) {
  if (totalPages <= 1) return null;
  return (
    <nav className="flex items-center justify-center gap-2 py-6" aria-label="Pagination">
      <button className="btn-secondary" disabled={page <= 1} onClick={() => onChange(page - 1)}>
        Previous
      </button>
      <span className="px-3 text-sm text-slate-600">
        Page {page} of {totalPages}
      </span>
      <button className="btn-secondary" disabled={page >= totalPages} onClick={() => onChange(page + 1)}>
        Next
      </button>
    </nav>
  );
}

export function StatCard({ label, value, hint, icon }: { label: string; value: ReactNode; hint?: string; icon?: ReactNode }) {
  return (
    <div className="card p-5">
      <div className="flex items-center justify-between text-sm text-slate-500">
        {label}
        {icon && <span className="text-brand-700">{icon}</span>}
      </div>
      <div className="mt-2 text-2xl font-bold tracking-tight">{value}</div>
      {hint && <div className="mt-1 text-xs text-slate-500">{hint}</div>}
    </div>
  );
}

export interface Column<T> { header: string; cell: (row: T) => ReactNode; className?: string }

export function DataTable<T>({ rows, columns, rowKey, empty }: { rows: T[]; columns: Column<T>[]; rowKey: (row: T) => string; empty?: string }) {
  if (rows.length === 0) return <p className="py-10 text-center text-sm text-slate-500">{empty ?? 'No records yet.'}</p>;
  return (
    <div className="overflow-x-auto rounded-2xl border border-slate-200">
      <table className="min-w-full divide-y divide-slate-200 text-sm">
        <thead className="bg-slate-50">
          <tr>
            {columns.map((c) => (
              <th key={c.header} scope="col" className={clsx('px-4 py-3 text-left font-semibold text-slate-600', c.className)}>
                {c.header}
              </th>
            ))}
          </tr>
        </thead>
        <tbody className="divide-y divide-slate-100 bg-white">
          {rows.map((r) => (
            <tr key={rowKey(r)} className="hover:bg-slate-50/60">
              {columns.map((c) => (
                <td key={c.header} className={clsx('px-4 py-3 align-middle', c.className)}>
                  {c.cell(r)}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

export function PageHeader({ title, subtitle, actions }: { title: string; subtitle?: string; actions?: ReactNode }) {
  return (
    <div className="mb-6 flex flex-wrap items-end justify-between gap-4">
      <div>
        <h1 className="text-2xl font-bold tracking-tight sm:text-3xl">{title}</h1>
        {subtitle && <p className="mt-1 text-slate-600">{subtitle}</p>}
      </div>
      {actions}
    </div>
  );
}

const FALLBACK_IMAGE = `data:image/svg+xml;utf8,${encodeURIComponent(
  '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 400 300"><defs><linearGradient id="g" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#ccfbf1"/><stop offset="1" stop-color="#99f6e4"/></linearGradient></defs><rect width="400" height="300" fill="url(#g)"/><path d="M150 170 L200 125 L250 170 V205 H150 Z" fill="none" stroke="#0f766e" stroke-width="8" stroke-linejoin="round"/><rect x="188" y="175" width="24" height="30" fill="#0f766e"/></svg>',
)}`;

/** Image that degrades to a branded placeholder if the source fails (CDN outage, blocked host, deleted blob). */
export function SafeImg(props: React.ImgHTMLAttributes<HTMLImageElement>) {
  return (
    <img
      {...props}
      onError={(e) => {
        const img = e.currentTarget;
        if (img.src !== FALLBACK_IMAGE) img.src = FALLBACK_IMAGE;
      }}
    />
  );
}
