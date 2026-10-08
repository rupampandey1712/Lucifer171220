import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useNavigate } from 'react-router';
import { api } from '@/api/client';
import { useAuthStore } from './auth-store';
import { toast } from './toast';

/** Favorites with optimistic toggle (safe: idempotent PUT/DELETE on the server). */
export function useFavorites() {
  const user = useAuthStore((s) => s.user);
  const qc = useQueryClient();
  const navigate = useNavigate();
  const ids = useQuery({ queryKey: ['favorites', 'ids'], queryFn: () => api<string[]>('/favorites/ids'), enabled: !!user, staleTime: 60_000 });

  const toggle = useMutation({
    mutationFn: async ({ id, saved }: { id: string; saved: boolean }) =>
      saved ? api(`/favorites/${id}`, { method: 'DELETE' }) : api(`/favorites/${id}`, { method: 'PUT' }),
    onMutate: async ({ id, saved }) => {
      await qc.cancelQueries({ queryKey: ['favorites', 'ids'] });
      const previous = qc.getQueryData<string[]>(['favorites', 'ids']) ?? [];
      qc.setQueryData(['favorites', 'ids'], saved ? previous.filter((x) => x !== id) : [...previous, id]);
      return { previous };
    },
    onError: (_e, _v, ctx) => {
      qc.setQueryData(['favorites', 'ids'], ctx?.previous);
      toast.error("Couldn't update your saved stays.");
    },
    onSuccess: (_d, { saved }) => toast.success(saved ? 'Removed from saved stays' : 'Saved to your favorites'),
    onSettled: () => qc.invalidateQueries({ queryKey: ['favorites'] }),
  });

  const set = new Set(ids.data ?? []);
  return {
    isSaved: (id: string) => set.has(id),
    toggle: (id: string) => {
      if (!user) {
        navigate(`/login?returnTo=${encodeURIComponent(location.pathname + location.search)}`);
        return;
      }
      toggle.mutate({ id, saved: set.has(id) });
    },
  };
}

export function useFeatureFlags() {
  return useQuery({ queryKey: ['features'], queryFn: () => api<Record<string, boolean>>('/features'), staleTime: 5 * 60_000 });
}
