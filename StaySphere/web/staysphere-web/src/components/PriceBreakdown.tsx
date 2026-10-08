import type { QuoteDto } from '@/api/types';
import { money } from '@/lib/format';

/** Displays the SERVER quote. The browser never computes authoritative prices. */
export function PriceBreakdown({ quote }: { quote: QuoteDto }) {
  const c = quote.currency;
  const rows: [string, number][] = [[`${money(quote.averageNightly, c)} × ${quote.nights} night${quote.nights === 1 ? '' : 's'}`, quote.accommodation]];
  if (quote.discount > 0) rows.push(['Discounts', -quote.discount]);
  if (quote.cleaningFee > 0) rows.push(['Cleaning fee', quote.cleaningFee]);
  rows.push(['StaySphere service fee', quote.serviceFee]);
  if (quote.taxes > 0) rows.push(['Taxes', quote.taxes]);

  return (
    <dl className="space-y-2.5 text-sm" aria-label="Price breakdown">
      {rows.map(([label, amount]) => (
        <div key={label} className="flex justify-between">
          <dt className="text-slate-600 underline decoration-dotted underline-offset-4">{label}</dt>
          <dd className={amount < 0 ? 'text-emerald-700' : undefined}>{amount < 0 ? `−${money(-amount, c)}` : money(amount, c)}</dd>
        </div>
      ))}
      <div className="flex justify-between border-t border-slate-200 pt-3 text-base font-semibold">
        <dt>Total</dt>
        <dd>{money(quote.total, c)}</dd>
      </div>
    </dl>
  );
}
