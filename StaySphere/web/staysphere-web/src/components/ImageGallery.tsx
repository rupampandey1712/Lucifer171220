import { Grid3x3 } from 'lucide-react';
import { useState } from 'react';
import type { ImageDto } from '@/api/types';
import { Modal, SafeImg } from './ui';

export function ImageGallery({ images, title }: { images: ImageDto[]; title: string }) {
  const [open, setOpen] = useState(false);
  const shown = images.slice(0, 5);
  if (images.length === 0) return <div className="aspect-[2/1] rounded-2xl bg-slate-100" />;
  return (
    <>
      <div className="relative grid h-[260px] grid-cols-4 grid-rows-2 gap-2 overflow-hidden rounded-2xl sm:h-[420px]">
        {shown.map((img, i) => (
          <button key={img.id} onClick={() => setOpen(true)} className={i === 0 ? 'col-span-4 row-span-2 sm:col-span-2' : 'hidden sm:block'} aria-label={`Open photo ${i + 1}`}>
            <SafeImg src={img.url} alt={`${title} — photo ${i + 1}`} className="h-full w-full object-cover transition hover:brightness-90" loading={i === 0 ? 'eager' : 'lazy'} />
          </button>
        ))}
        <button onClick={() => setOpen(true)} className="btn-secondary absolute right-4 bottom-4 !py-1.5 text-xs shadow">
          <Grid3x3 className="h-4 w-4" /> Show all photos
        </button>
      </div>
      <Modal open={open} onClose={() => setOpen(false)} title={`${title} · ${images.length} photos`} wide>
        <div className="grid gap-3 sm:grid-cols-2">
          {images.map((img, i) => (
            <SafeImg key={img.id} src={img.url} alt={`${title} — photo ${i + 1}`} loading="lazy" className="w-full rounded-xl object-cover" />
          ))}
        </div>
      </Modal>
    </>
  );
}
