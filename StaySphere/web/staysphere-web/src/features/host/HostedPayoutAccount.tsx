import { useMutation, useQueryClient } from '@tanstack/react-query';
import { ExternalLink, Landmark } from 'lucide-react';
import { useEffect, useRef } from 'react';
import { useSearchParams } from 'react-router';
import { api } from '@/api/client';
import type { PayoutAccount, PayoutOnboarding } from '@/api/types';
import { toast } from '@/lib/toast';

/**
 * Payout account when bank details are collected by Stripe Connect: the host is sent to Stripe's hosted onboarding and
 * comes back to /host/earnings?payouts=return, where we re-read the account. StaySphere never sees the bank details.
 */
export function HostedPayoutAccount({ account }: { account: PayoutAccount | null | undefined }) {
  const qc = useQueryClient();
  const [params, setParams] = useSearchParams();
  const refreshed = useRef(false);

  const onboarding = useMutation({
    mutationFn: () => api<PayoutOnboarding>('/host/payouts/onboarding', { method: 'POST', body: {} }),
    onSuccess: ({ url }) => {
      // Only ever leave for an https URL the API gave us (Stripe's onboarding page).
      if (!url.startsWith('https://')) { toast.error('Unexpected onboarding link.'); return; }
      window.location.assign(url);
    },
    onError: (e) => toast.error((e as Error).message),
  });
  const refresh = useMutation({
    mutationFn: () => api<PayoutAccount>('/host/payouts/account/refresh', { method: 'POST', body: {} }),
    onSuccess: (a) => {
      if (a.payoutsEnabled) toast.success('Payouts are set up');
      else toast.info(a.requirement ?? 'Stripe is still reviewing your details.');
      void qc.invalidateQueries({ queryKey: ['host', 'payouts'] });
    },
    onError: (e) => toast.error((e as Error).message),
  });

  // Back from Stripe: re-read the account once, then tidy the URL.
  useEffect(() => {
    const back = params.get('payouts');
    if (!back || refreshed.current) return;
    refreshed.current = true;
    if (back === 'refresh') onboarding.mutate(); // Stripe's link expired: get a fresh one
    else refresh.mutate();
    params.delete('payouts');
    setParams(params, { replace: true });
  }, [params, setParams, onboarding, refresh]);

  const busy = onboarding.isPending || refresh.isPending;
  return (
    <div className="card p-6">
      <h2 className="mb-3 flex items-center gap-2 text-lg font-semibold"><Landmark className="h-5 w-5" /> Payout account</h2>
      {!account ? (
        <>
          <p className="mb-4 text-sm text-slate-600">Payouts are handled by Stripe. You'll confirm your identity and add a bank account on Stripe's secure site.</p>
          <button className="btn-primary" disabled={busy} onClick={() => onboarding.mutate()}>
            {onboarding.isPending ? 'Opening Stripe…' : <>Set up payouts <ExternalLink className="h-4 w-4" /></>}
          </button>
        </>
      ) : account.payoutsEnabled ? (
        <>
          <p className="font-medium">{account.accountHolder}</p>
          <p className="font-mono text-sm text-slate-600">{account.maskedAccount} · {account.country}</p>
          <button className="btn-ghost mt-3 !px-0 underline" disabled={busy} onClick={() => onboarding.mutate()}>Update on Stripe</button>
        </>
      ) : (
        <>
          <p className="mb-4 rounded-xl bg-amber-50 p-3 text-sm text-amber-900" role="status">{account.requirement ?? 'Finish setting up payouts on Stripe to receive your earnings.'}</p>
          <div className="flex flex-wrap gap-2">
            <button className="btn-primary" disabled={busy} onClick={() => onboarding.mutate()}>Continue setup <ExternalLink className="h-4 w-4" /></button>
            <button className="btn-ghost" disabled={busy} onClick={() => refresh.mutate()}>{refresh.isPending ? 'Checking…' : 'Check status'}</button>
          </div>
        </>
      )}
    </div>
  );
}
