import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '@/api/client';
import type { ConsentDto } from '@/api/types';
import { prettyDate } from '@/lib/format';
import { toast } from '@/lib/toast';

const labels: Record<string, string> = {
  Terms: 'Terms of service',
  Privacy: 'Privacy policy',
  MarketingEmails: 'Marketing emails',
  ProductAnalytics: 'Product analytics',
};

/** Consent toggles: required consents are shown but locked; every change is recorded (append-only). */
export function ConsentSettings() {
  const qc = useQueryClient();
  const consents = useQuery({ queryKey: ['me', 'consents'], queryFn: () => api<ConsentDto[]>('/me/consents') });
  const update = useMutation({
    mutationFn: (v: { purpose: string; granted: boolean }) => api<ConsentDto>(`/me/consents/${v.purpose}`, { method: 'PUT', body: { granted: v.granted } }),
    onSuccess: () => { void qc.invalidateQueries({ queryKey: ['me', 'consents'] }); toast.success('Preference saved'); },
    onError: (e) => toast.error((e as Error).message),
  });

  if (consents.isPending) return <p className="mb-4 text-sm text-slate-500">Loading consent preferences…</p>;
  if (consents.isError) return <p className="mb-4 text-sm text-red-700" role="alert">Couldn't load consent preferences.</p>;
  if (consents.data.length === 0) return <p className="mb-4 text-sm text-slate-500">No consent preferences.</p>;

  return (
    <ul className="mb-4 divide-y divide-slate-100">
      {consents.data.map((c) => (
        <li key={c.purpose} className="flex items-center justify-between gap-4 py-2 text-sm">
          <div>
            <p className="font-medium">{labels[c.purpose] ?? c.purpose}</p>
            <p className="text-xs text-slate-500">
              {c.required ? 'Required to use StaySphere' : 'Optional'} · policy {c.policyVersion}
              {c.recordedAt ? ` · updated ${prettyDate(c.recordedAt)}` : ''}
            </p>
          </div>
          <input
            type="checkbox"
            className="h-4 w-4"
            aria-label={labels[c.purpose] ?? c.purpose}
            checked={c.granted}
            disabled={c.required || update.isPending}
            onChange={(e) => update.mutate({ purpose: c.purpose, granted: e.target.checked })}
          />
        </li>
      ))}
    </ul>
  );
}
