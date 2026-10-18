// Guest browsing journey under load: home data → search → listing → quote.
//   k6 run tests/perf/browse.js                       (defaults to http://localhost:5173)
//   BASE_URL=https://staging.example k6 run -e VUS=50 tests/perf/browse.js
// Thresholds are the SLOs from docs/architecture (p95 < 500 ms reads, < 800 ms quotes, < 1 % errors); k6 exits non-zero if they're missed.
import http from 'k6/http';
import { check, group, sleep } from 'k6';

const BASE = (__ENV.BASE_URL || 'http://localhost:5173').replace(/\/$/, '');
const API = `${BASE}/api/v1`;
const VUS = Number(__ENV.VUS || 20);

export const options = {
  scenarios: {
    browse: {
      executor: 'ramping-vus',
      stages: [
        { duration: '20s', target: VUS },
        { duration: __ENV.DURATION || '1m', target: VUS },
        { duration: '10s', target: 0 },
      ],
    },
  },
  thresholds: {
    http_req_failed: ['rate<0.01'],
    'http_req_duration{kind:read}': ['p(95)<500'],
    'http_req_duration{kind:quote}': ['p(95)<800'],
    checks: ['rate>0.99'],
  },
};

const cities = ['Lisbon', 'Porto', 'Barcelona', 'Paris', 'Berlin', 'London', 'Rome', 'Amsterdam'];
const read = { tags: { kind: 'read' } };

export function setup() {
  const res = http.get(`${API}/features`);
  if (res.status !== 200) throw new Error(`API not reachable at ${API} (status ${res.status})`);
}

export default function () {
  let listing;
  group('search', () => {
    http.get(`${API}/destinations/featured`, read);
    const city = cities[Math.floor(Math.random() * cities.length)];
    const res = http.get(`${API}/search/properties?location=${city}&guests=2&pageSize=18`, read);
    check(res, { 'search 200': (r) => r.status === 200 });
    const items = res.status === 200 ? res.json('items') : [];
    if (items.length === 0) {
      const any = http.get(`${API}/search/properties?pageSize=18`, read);
      listing = any.status === 200 ? any.json('items')[0] : undefined;
    } else {
      listing = items[Math.floor(Math.random() * items.length)];
    }
  });
  if (!listing) return;
  sleep(1);

  group('listing', () => {
    const res = http.get(`${API}/properties/${listing.id}`, read);
    check(res, { 'listing 200': (r) => r.status === 200 });
    http.get(`${API}/properties/${listing.id}/reviews`, read);
  });
  sleep(1);

  group('quote', () => {
    const start = new Date(Date.now() + (60 + Math.floor(Math.random() * 300)) * 86400000);
    const ci = start.toISOString().slice(0, 10);
    const co = new Date(start.getTime() + 3 * 86400000).toISOString().slice(0, 10);
    const res = http.post(`${API}/properties/${listing.id}/quote`, JSON.stringify({ checkIn: ci, checkOut: co, guests: 2 }),
      { headers: { 'Content-Type': 'application/json' }, tags: { kind: 'quote' }, responseCallback: http.expectedStatuses(200, 422) });
    // 422 = those dates aren't available for this listing: a correct answer, not an error.
    check(res, { 'quote answered': (r) => r.status === 200 || r.status === 422 });
  });
  sleep(1);
}
