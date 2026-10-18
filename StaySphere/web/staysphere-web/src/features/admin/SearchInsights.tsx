import { useQuery } from '@tanstack/react-query';
import { api } from '@/api/client';
import type { SearchInsightsDto } from '@/api/types';
import { ErrorState, Skeleton } from '@/components/ui';

function TermList({ title, terms, empty }: { title: string; terms: SearchInsightsDto['topDestinations']; empty: string }) {
  return (
    <div className="card p-5">
      <h3 className="mb-3 text-sm font-semibold">{title}</h3>
      {terms.length === 0 ? <p className="text-sm text-slate-500">{empty}</p> : (
        <ol className="space-y-1 text-sm">
          {terms.map((t) => <li key={t.location} className="flex justify-between capitalize"><span>{t.location}</span><span className="text-slate-500">{t.searches}</span></li>)}
        </ol>)}
    </div>
  );
}

/** Anonymised search demand (last 30 days). Terms searched fewer than 3 times are hidden by the API. */
export function SearchInsights() {
  const insights = useQuery({ queryKey: ['admin', 'search-insights'], queryFn: () => api<SearchInsightsDto>('/admin/search-insights?days=30') });
  return (
    <section className="mt-10">
      <h2 className="mb-3 font-semibold">Search demand {insights.data ? <span className="text-sm font-normal text-slate-500">· {insights.data.totalSearches} searches in {insights.data.days} days</span> : null}</h2>
      {insights.isLoading ? <Skeleton className="h-32" /> : insights.isError ? <ErrorState error={insights.error} onRetry={() => insights.refetch()} /> : (
        <div className="grid gap-4 md:grid-cols-2">
          <TermList title="Top destinations" terms={insights.data!.topDestinations} empty="Not enough searches yet." />
          <TermList title="Searches with no results" terms={insights.data!.zeroResultSearches} empty="Every popular search found stays." />
        </div>)}
    </section>
  );
}
