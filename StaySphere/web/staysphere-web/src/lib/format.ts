import { differenceInCalendarDays, format, parseISO } from 'date-fns';

const ZERO_DECIMAL = new Set(['JPY', 'IDR', 'KRW', 'VND']);

export function money(amount: number | null | undefined, currency = 'USD', compact = false): string {
  if (amount === null || amount === undefined) return '—';
  const digits = ZERO_DECIMAL.has(currency) || (compact && Number.isInteger(amount)) ? 0 : compact ? 0 : 2;
  try {
    return new Intl.NumberFormat(undefined, { style: 'currency', currency, maximumFractionDigits: digits, minimumFractionDigits: digits }).format(amount);
  } catch {
    return `${currency} ${amount.toFixed(digits)}`;
  }
}

export const isoDate = (d: Date) => format(d, 'yyyy-MM-dd');
export const prettyDate = (iso: string) => format(parseISO(iso), 'd MMM yyyy');
export const shortDate = (iso: string) => format(parseISO(iso), 'd MMM');
export const dateTime = (iso: string) => format(parseISO(iso), 'd MMM yyyy, HH:mm');
export const nights = (checkIn: string, checkOut: string) => differenceInCalendarDays(parseISO(checkOut), parseISO(checkIn));
export const stayLabel = (checkIn: string, checkOut: string) => `${shortDate(checkIn)} – ${prettyDate(checkOut)}`;
export const pluralize = (n: number, word: string) => `${n} ${word}${n === 1 ? '' : 's'}`;

export function humanize(value: string): string {
  return value.replace(/([a-z])([A-Z])/g, '$1 $2').replace(/^./, (c) => c.toUpperCase());
}

export function relativeTime(iso: string): string {
  const diff = (Date.now() - parseISO(iso).getTime()) / 1000;
  if (diff < 60) return 'just now';
  if (diff < 3600) return `${Math.floor(diff / 60)}m ago`;
  if (diff < 86400) return `${Math.floor(diff / 3600)}h ago`;
  if (diff < 7 * 86400) return `${Math.floor(diff / 86400)}d ago`;
  return prettyDate(iso);
}
