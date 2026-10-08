import { clsx } from 'clsx';
import { addDays, addMonths, eachDayOfInterval, endOfMonth, format, getDay, isBefore, parseISO, startOfMonth, startOfToday } from 'date-fns';
import { ChevronLeft, ChevronRight } from 'lucide-react';
import { useMemo, useState } from 'react';
import { isoDate } from '@/lib/format';

/**
 * Two-month range picker that greys out unavailable nights. A check-out may land on an unavailable night (that night
 * belongs to the next guest), but a range can never span one.
 */
export function AvailabilityCalendar({ unavailable, checkIn, checkOut, onChange, months = 2 }: {
  unavailable: string[];
  checkIn?: string;
  checkOut?: string;
  onChange: (checkIn: string, checkOut: string) => void;
  months?: number;
}) {
  const today = startOfToday();
  const [offset, setOffset] = useState(0);
  const blocked = useMemo(() => new Set(unavailable), [unavailable]);

  const firstBlockedAfter = (start: string) => {
    let d = parseISO(start);
    for (let i = 0; i < 120; i++) {
      d = addDays(d, 1);
      if (blocked.has(isoDate(d))) return isoDate(d);
    }
    return null;
  };
  const limit = checkIn && !checkOut ? firstBlockedAfter(checkIn) : null;

  const click = (day: string) => {
    if (!checkIn || checkOut || day <= checkIn) {
      if (blocked.has(day)) return;
      onChange(day, '');
      return;
    }
    if (limit && day > limit) {
      onChange(day, '');
      return;
    }
    onChange(checkIn, day);
  };

  return (
    <div>
      <div className="mb-2 flex items-center justify-between">
        <button type="button" className="rounded-full p-1.5 hover:bg-slate-100 disabled:opacity-30" disabled={offset === 0} onClick={() => setOffset((o) => o - 1)} aria-label="Previous month">
          <ChevronLeft className="h-5 w-5" />
        </button>
        <button type="button" className="rounded-full p-1.5 hover:bg-slate-100" onClick={() => setOffset((o) => o + 1)} aria-label="Next month">
          <ChevronRight className="h-5 w-5" />
        </button>
      </div>
      <div className={clsx('grid gap-8', months > 1 && 'md:grid-cols-2')}>
        {Array.from({ length: months }, (_, m) => {
          const month = addMonths(startOfMonth(today), offset + m);
          const days = eachDayOfInterval({ start: month, end: endOfMonth(month) });
          const lead = (getDay(month) + 6) % 7;
          return (
            <div key={m} className={clsx(m > 0 && 'hidden md:block')}>
              <p className="mb-3 text-center font-semibold">{format(month, 'MMMM yyyy')}</p>
              <div className="grid grid-cols-7 text-center text-xs text-slate-500">
                {['Mo', 'Tu', 'We', 'Th', 'Fr', 'Sa', 'Su'].map((d) => <span key={d} className="py-1">{d}</span>)}
              </div>
              <div className="grid grid-cols-7 gap-y-1">
                {Array.from({ length: lead }, (_, i) => <span key={`l${i}`} />)}
                {days.map((d) => {
                  const iso = isoDate(d);
                  const past = isBefore(d, today);
                  const isBlocked = blocked.has(iso);
                  const outOfRange = !!limit && iso > limit;
                  const canCheckOut = !!checkIn && !checkOut && iso > checkIn && !outOfRange;
                  const disabled = past || (isBlocked && !canCheckOut) || (outOfRange && false);
                  const selected = iso === checkIn || iso === checkOut;
                  const inRange = checkIn && checkOut && iso > checkIn && iso < checkOut;
                  return (
                    <button
                      type="button"
                      key={iso}
                      disabled={disabled}
                      onClick={() => click(iso)}
                      aria-pressed={selected}
                      aria-label={`${format(d, 'EEEE d MMMM yyyy')}${isBlocked ? ', unavailable' : ''}`}
                      className={clsx(
                        'mx-auto flex h-10 w-10 items-center justify-center rounded-full text-sm transition',
                        selected && 'bg-slate-900 font-semibold text-white',
                        inRange && 'rounded-none bg-slate-100',
                        !selected && !disabled && 'hover:ring-1 hover:ring-slate-900',
                        disabled && 'cursor-not-allowed text-slate-300 line-through',
                      )}
                    >
                      {format(d, 'd')}
                    </button>
                  );
                })}
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
}
