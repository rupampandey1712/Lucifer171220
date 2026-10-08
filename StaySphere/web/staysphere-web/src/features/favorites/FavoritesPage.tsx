import { useQuery } from '@tanstack/react-query';
import { Heart } from 'lucide-react';
import { Link } from 'react-router';
import { api } from '@/api/client';
import type { PropertyCard as Card } from '@/api/types';
import { PropertyCard, PropertyCardSkeleton } from '@/components/PropertyCard';
import { EmptyState, ErrorState, PageHeader } from '@/components/ui';

export function FavoritesPage() {
  const favorites = useQuery({ queryKey: ['favorites', 'list'], queryFn: () => api<Card[]>('/favorites') });
  return (
    <div className="container-page py-10">
      <PageHeader title="Wishlists" subtitle="Places you've saved for later." />
      {favorites.isLoading ? <div className="grid gap-6 sm:grid-cols-2 lg:grid-cols-4">{Array.from({ length: 4 }, (_, i) => <PropertyCardSkeleton key={i} />)}</div>
        : favorites.isError ? <ErrorState error={favorites.error} onRetry={() => favorites.refetch()} />
        : favorites.data!.length === 0 ? <EmptyState icon={<Heart className="h-7 w-7" />} title="You haven't saved any stays yet." description="Tap the heart on any listing to save it here." action={<Link to="/search" className="btn-primary">Explore stays</Link>} />
        : <div className="grid gap-x-6 gap-y-10 sm:grid-cols-2 lg:grid-cols-4">{favorites.data!.map((p) => <PropertyCard key={p.id} property={p} />)}</div>}
    </div>
  );
}
