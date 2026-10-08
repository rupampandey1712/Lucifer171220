import { QueryCache, QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { StrictMode, useEffect } from 'react';
import { createRoot } from 'react-dom/client';
import { RouterProvider } from 'react-router';
import { ApiError, refreshSession } from '@/api/client';
import { router } from '@/app/router';
import { PageLoader } from '@/components/ui';
import { useAuthStore } from '@/lib/auth-store';
import { toast } from '@/lib/toast';
import './index.css';

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30_000,
      refetchOnWindowFocus: false,
      // Retry only transient failures; never 4xx.
      retry: (count, error) => count < 2 && (!(error instanceof ApiError) || error.status === 0 || error.status >= 500),
    },
  },
  queryCache: new QueryCache({ onError: (error, query) => { if (error instanceof ApiError && error.status === 429 && query.state.data !== undefined) toast.error(error.message); } }),
});

function App() {
  const initialized = useAuthStore((s) => s.initialized);
  useEffect(() => {
    // Restore the session from the HttpOnly refresh cookie (the access token is never persisted).
    refreshSession().finally(() => useAuthStore.getState().markInitialized());
  }, []);
  if (!initialized) return <PageLoader />;
  return <RouterProvider router={router} />;
}

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <App />
    </QueryClientProvider>
  </StrictMode>,
);
