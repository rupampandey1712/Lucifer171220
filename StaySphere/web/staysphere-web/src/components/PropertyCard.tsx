import { clsx } from 'clsx';
import { ChevronLeft, ChevronRight, Heart, Zap } from 'lucide-react';
import { useState } from 'react';
import { Link } from 'react-router';
import type { PropertyCard as Card } from '@/api/types';
import { humanize, money } from '@/lib/format';
import { useFavorites } from '@/lib/hooks';
import { Rating, Skeleton, SafeImg } from './ui';

export function PropertyCard({ property, query, highlighted, onHover }: { property: Card; query?: string; highlighted?: boolean; onHover?: (id: string | null) => void }) {
  const [index, setIndex] = useState(0);
  const { isSaved, toggle } = useFavorites();
  const images = property.imageUrls.length ? property.imageUrls : ['https://picsum.photos/seed/placeholder/800/600'];
  const saved = isSaved(property.id);
  const href = `/property/${property.id}${query ?? ''}`;

  return (
    <article
      className={clsx('group relative animate-fade-in', highlighted && 'ring-2 ring-slate-900 ring-offset-4 rounded-2xl')}
      onMouseEnter={() => onHover?.(property.id)}
      onMouseLeave={() => onHover?.(null)}
    >
      <div className="relative aspect-[4/3] overflow-hidden rounded-2xl bg-slate-100">
        <Link to={href} aria-label={property.title}>
          <SafeImg src={images[index]} alt={`${property.title} — photo ${index + 1}`} loading="lazy" className="h-full w-full object-cover transition duration-500 group-hover:scale-[1.03]" />
        </Link>
        <button
          type="button"
          onClick={() => toggle(property.id)}
          aria-pressed={saved}
          aria-label={saved ? 'Remove from favorites' : 'Save to favorites'}
          className="absolute top-3 right-3 rounded-full p-1.5 transition hover:scale-110"
        >
          <Heart className={clsx('h-6 w-6 drop-shadow', saved ? 'fill-coral-500 text-coral-500' : 'fill-black/30 text-white')} />
        </button>
        {property.instantBook && (
          <span className="absolute top-3 left-3 inline-flex items-center gap-1 rounded-full bg-white/95 px-2.5 py-1 text-xs font-semibold shadow">
            <Zap className="h-3 w-3 fill-amber-400 text-amber-500" /> Instant book
          </span>
        )}
        {images.length > 1 && (
          <>
            <button aria-label="Previous photo" onClick={() => setIndex((i) => (i - 1 + images.length) % images.length)}
              className="absolute top-1/2 left-2 -translate-y-1/2 rounded-full bg-white/90 p-1 opacity-0 shadow transition group-hover:opacity-100 focus:opacity-100">
              <ChevronLeft className="h-4 w-4" />
            </button>
            <button aria-label="Next photo" onClick={() => setIndex((i) => (i + 1) % images.length)}
              className="absolute top-1/2 right-2 -translate-y-1/2 rounded-full bg-white/90 p-1 opacity-0 shadow transition group-hover:opacity-100 focus:opacity-100">
              <ChevronRight className="h-4 w-4" />
            </button>
            <div className="absolute bottom-2 left-1/2 flex -translate-x-1/2 gap-1">
              {images.map((img, i) => (
                <span key={img} className={clsx('h-1.5 w-1.5 rounded-full', i === index ? 'bg-white' : 'bg-white/50')} />
              ))}
            </div>
          </>
        )}
      </div>
      <Link to={href} className="mt-3 block">
        <div className="flex items-start justify-between gap-2">
          <h3 className="line-clamp-1 font-semibold">{property.city}, {property.country}</h3>
          <Rating value={property.ratingAverage} count={property.reviewCount} />
        </div>
        <p className="line-clamp-1 text-sm text-slate-600">{property.title}</p>
        <p className="text-sm text-slate-500">
          {humanize(property.propertyType)} · {property.bedrooms} bedroom{property.bedrooms === 1 ? '' : 's'} · up to {property.maxGuests} guests
          {property.distanceKm != null && ` · ${property.distanceKm} km`}
        </p>
        <p className="mt-1 text-sm">
          <span className="font-semibold">{money(property.nightlyPrice, property.currency, true)}</span> <span className="text-slate-600">night</span>
          {property.totalPrice != null && <span className="text-slate-500"> · {money(property.totalPrice, property.currency, true)} total est.</span>}
        </p>
      </Link>
    </article>
  );
}

export function PropertyCardSkeleton() {
  return (
    <div>
      <Skeleton className="aspect-[4/3] w-full" />
      <Skeleton className="mt-3 h-4 w-2/3" />
      <Skeleton className="mt-2 h-3 w-1/2" />
      <Skeleton className="mt-2 h-3 w-1/3" />
    </div>
  );
}
