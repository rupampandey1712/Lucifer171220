import { create } from 'zustand';
import type { AuthResponse, UserDto } from '@/api/types';

interface AuthState {
  user: UserDto | null;
  /** Kept in memory only (never localStorage) to limit XSS token theft; refresh token lives in an HttpOnly cookie. */
  accessToken: string | null;
  initialized: boolean;
  setSession: (auth: AuthResponse) => void;
  setUser: (user: UserDto) => void;
  clear: () => void;
  markInitialized: () => void;
}

export const useAuthStore = create<AuthState>((set) => ({
  user: null,
  accessToken: null,
  initialized: false,
  setSession: (auth) => set({ user: auth.user, accessToken: auth.accessToken }),
  setUser: (user) => set({ user }),
  clear: () => set({ user: null, accessToken: null }),
  markInitialized: () => set({ initialized: true }),
}));

export const hasRole = (user: UserDto | null, role: 'Guest' | 'Host' | 'Admin' | 'Support') => !!user?.roles.includes(role);
