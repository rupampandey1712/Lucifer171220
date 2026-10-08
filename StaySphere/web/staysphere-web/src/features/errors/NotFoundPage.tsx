import { Link } from 'react-router';

export function NotFoundPage() {
  return (
    <div className="container-page flex flex-col items-center py-24 text-center">
      <p className="text-8xl font-black text-slate-200">404</p>
      <h1 className="mt-4 text-3xl font-bold">We can't seem to find that page</h1>
      <p className="mt-2 text-slate-600">It may have moved, or the link might be broken.</p>
      <div className="mt-8 flex gap-3"><Link to="/" className="btn-primary">Go home</Link><Link to="/search" className="btn-secondary">Search stays</Link></div>
    </div>
  );
}
