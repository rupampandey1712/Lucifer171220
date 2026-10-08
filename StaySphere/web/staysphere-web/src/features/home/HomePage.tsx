import { useQuery } from '@tanstack/react-query';
import { ArrowRight, Building2, Mountain, Sparkles, Trees, Umbrella, Wallet } from 'lucide-react';
import { Link } from 'react-router';
import { api, toQuery } from '@/api/client';
import type { DestinationDto, Paged, PropertyCard as Card } from '@/api/types';
import { PropertyCard, PropertyCardSkeleton } from '@/components/PropertyCard';
import { SearchBar } from '@/components/SearchBar';
import { ErrorState, SafeImg } from '@/components/ui';

function Rail({ title, subtitle, query, icon }: { title: string; subtitle: string; query: Record<string, unknown>; icon: React.ReactNode }) {
  const result = useQuery({ queryKey: ['search', query], queryFn: () => api<Paged<Card>>(`/search/properties${toQuery({ ...query, pageSize: 4 })}`) });
  return (
    <section className="container-page mt-14" aria-labelledby={title}>
      <div className="mb-5 flex items-end justify-between gap-4">
        <div className="flex items-center gap-3">
          <span className="rounded-xl bg-brand-50 p-2 text-brand-700">{icon}</span>
          <div><h2 id={title} className="text-xl font-bold">{title}</h2><p className="text-sm text-slate-600">{subtitle}</p></div>
        </div>
        <Link to={`/search${toQuery(query)}`} className="flex items-center gap-1 text-sm font-semibold text-brand-700 hover:underline">Show all <ArrowRight className="h-4 w-4" /></Link>
      </div>
      {result.isError ? <ErrorState error={result.error} onRetry={() => result.refetch()} /> : (
        <div className="grid gap-6 sm:grid-cols-2 lg:grid-cols-4">
          {result.isLoading ? Array.from({ length: 4 }, (_, i) => <PropertyCardSkeleton key={i} />) : result.data!.items.map((p) => <PropertyCard key={p.id} property={p} />)}
        </div>
      )}
    </section>
  );
}

export function HomePage() {
  const destinations = useQuery({ queryKey: ['destinations'], queryFn: () => api<DestinationDto[]>('/destinations/featured'), staleTime: 10 * 60_000 });
  return (
    <>
      <section className="relative overflow-hidden bg-gradient-to-br from-brand-900 via-brand-700 to-teal-500 pb-24 pt-16 text-white sm:pt-24">
        <div className="pointer-events-none absolute -top-20 -right-20 h-96 w-96 rounded-full bg-coral-400/30 blur-3xl" />
        <div className="pointer-events-none absolute -bottom-32 -left-10 h-96 w-96 rounded-full bg-teal-300/30 blur-3xl" />
        <div className="container-page relative">
          <h1 className="max-w-3xl text-4xl font-extrabold tracking-tight sm:text-6xl">Find your next stay</h1>
          <p className="mt-4 max-w-2xl text-lg text-teal-50">Apartments, cabins, villas and one-of-a-kind homes in {destinations.data?.length ?? 'dozens of'} destinations — hosted by real people.</p>
          <div className="mt-10 text-slate-900"><SearchBar /></div>
          <p className="mt-4 flex items-center gap-2 text-sm text-teal-50"><Sparkles className="h-4 w-4" /> Try the AI travel assistant (bottom right) — "family-friendly place in Goa for 4 under ₹8,000".</p>
        </div>
      </section>

      <section className="container-page -mt-12 relative" aria-labelledby="popular">
        <div className="card p-6">
          <h2 id="popular" className="mb-4 text-xl font-bold">Popular destinations</h2>
          <div className="flex gap-4 overflow-x-auto pb-2">
            {(destinations.data ?? []).map((d) => (
              <Link key={`${d.city}-${d.country}`} to={`/search?location=${encodeURIComponent(d.city)}`} className="group w-40 shrink-0">
                <div className="aspect-square overflow-hidden rounded-2xl bg-slate-100">{d.imageUrl && <SafeImg src={d.imageUrl} alt="" loading="lazy" className="h-full w-full object-cover transition group-hover:scale-105" />}</div>
                <p className="mt-2 font-semibold">{d.city}</p>
                <p className="text-xs text-slate-500">{d.country} · {d.listingCount} stays</p>
              </Link>
            ))}
            {destinations.isLoading && Array.from({ length: 6 }, (_, i) => <div key={i} className="aspect-square w-40 shrink-0 animate-pulse rounded-2xl bg-slate-200" />)}
          </div>
        </div>
      </section>

      <Rail title="Trending stays" subtitle="Guests love these right now" query={{ sort: 'MostReviewed' }} icon={<Sparkles className="h-5 w-5" />} />
      <Rail title="Highly rated" subtitle="Consistently 5-star experiences" query={{ sort: 'Rating', minRating: 4.8 }} icon={<Trees className="h-5 w-5" />} />
      <Rail title="Budget friendly" subtitle="Great value under $120 a night" query={{ maxPrice: 120, sort: 'PriceLowHigh' }} icon={<Wallet className="h-5 w-5" />} />
      <Rail title="Unique stays" subtitle="Treehouses, farm stays and more" query={{ propertyTypes: ['UniqueStay', 'FarmStay', 'Cabin'] }} icon={<Mountain className="h-5 w-5" />} />
      <Rail title="Beach destinations" subtitle="Sun, sand and sea views" query={{ location: 'Goa' }} icon={<Umbrella className="h-5 w-5" />} />
      <Rail title="City breaks" subtitle="Stylish bases in great neighbourhoods" query={{ location: 'Paris' }} icon={<Building2 className="h-5 w-5" />} />
    </>
  );
}
