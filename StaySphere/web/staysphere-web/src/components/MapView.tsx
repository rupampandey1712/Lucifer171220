import { useEffect } from 'react';
import L from 'leaflet';
import { MapContainer, Marker, Popup, TileLayer, useMap, useMapEvents } from 'react-leaflet';
import { Link } from 'react-router';
import { money } from '@/lib/format';

export interface MapPoint { id: string; lat: number; lng: number; price?: number; currency?: string; title: string; image?: string }

function priceIcon(label: string, active: boolean) {
  return L.divIcon({ className: '', html: `<div class="price-marker ${active ? 'active' : ''}">${label}</div>`, iconSize: [60, 24], iconAnchor: [30, 12] });
}

const pinIcon = L.divIcon({
  className: '',
  html: '<div style="width:22px;height:22px;border-radius:9999px;background:#0f766e;border:3px solid white;box-shadow:0 2px 8px rgb(0 0 0/.35)"></div>',
  iconSize: [22, 22],
  iconAnchor: [11, 11],
});

function FitBounds({ points }: { points: MapPoint[] }) {
  const map = useMap();
  const key = points.map((p) => p.id).join(',');
  useEffect(() => {
    if (points.length === 0) return;
    if (points.length === 1) map.setView([points[0].lat, points[0].lng], 13);
    else map.fitBounds(L.latLngBounds(points.map((p) => [p.lat, p.lng] as [number, number])), { padding: [40, 40], maxZoom: 14 });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key, map]);
  return null;
}

function BoundsWatcher({ onMove }: { onMove: (b: { south: number; west: number; north: number; east: number }) => void }) {
  useMapEvents({
    moveend: (e) => {
      const b = e.target.getBounds();
      onMove({ south: b.getSouth(), west: b.getWest(), north: b.getNorth(), east: b.getEast() });
    },
  });
  return null;
}

/** OpenStreetMap tiles via Leaflet. Swap the TileLayer for Azure Maps/Mapbox without touching callers. */
export function MapView({ points, activeId, single, onBoundsChange, className }: {
  points: MapPoint[];
  activeId?: string | null;
  single?: boolean;
  onBoundsChange?: (b: { south: number; west: number; north: number; east: number }) => void;
  className?: string;
}) {
  const center: [number, number] = points.length ? [points[0].lat, points[0].lng] : [30, 0];
  return (
    <MapContainer center={center} zoom={points.length ? 12 : 2} scrollWheelZoom className={className ?? 'h-full w-full rounded-2xl'} aria-label="Map">
      <TileLayer attribution='&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors' url="https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png" />
      {!onBoundsChange && <FitBounds points={points} />}
      {onBoundsChange && <BoundsWatcher onMove={onBoundsChange} />}
      {points.map((p) => (
        <Marker key={p.id} position={[p.lat, p.lng]} icon={single || p.price === undefined ? pinIcon : priceIcon(money(p.price, p.currency, true), p.id === activeId)} zIndexOffset={p.id === activeId ? 1000 : 0}>
          {!single && (
            <Popup>
              <Link to={`/property/${p.id}`} className="block w-48">
                {p.image && <img src={p.image} alt="" className="mb-2 h-28 w-full rounded-lg object-cover" />}
                <span className="block text-sm font-semibold text-slate-900">{p.title}</span>
                {p.price !== undefined && <span className="text-sm text-slate-600">{money(p.price, p.currency, true)} night</span>}
              </Link>
            </Popup>
          )}
        </Marker>
      ))}
    </MapContainer>
  );
}
