import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { addDays, format } from 'date-fns';
import { AvailabilityCalendar } from './AvailabilityCalendar';
import { PriceBreakdown } from './PriceBreakdown';
import { PropertyCard } from './PropertyCard';
import { EmptyState, ErrorState, StatusBadge } from './ui';
import { ApiError } from '@/api/client';
import type { PropertyCard as Card, QuoteDto } from '@/api/types';
import { renderWithProviders } from '@/test/utils';

const card: Card = {
  id: 'p1', title: 'Sunny loft in Alfama', propertyType: 'Apartment', roomType: 'EntirePlace', city: 'Lisbon', country: 'Portugal',
  latitude: 38.7, longitude: -9.1, nightlyPrice: 120, currency: 'EUR', totalPrice: 400, ratingAverage: 4.87, reviewCount: 23,
  maxGuests: 4, bedrooms: 2, beds: 2, instantBook: true, imageUrls: ['a.jpg', 'b.jpg'], distanceKm: null,
};

describe('PropertyCard', () => {
  it('shows location, price, rating and an accessible favorite button', () => {
    renderWithProviders(<PropertyCard property={card} />);
    expect(screen.getByText('Lisbon, Portugal')).toBeInTheDocument();
    expect(screen.getByLabelText('Rated 4.87 out of 5')).toBeInTheDocument();
    expect(screen.getByText(/€120|120/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Save to favorites' })).toHaveAttribute('aria-pressed', 'false');
    expect(screen.getByText('Instant book')).toBeInTheDocument();
  });

  it('cycles through photos', async () => {
    renderWithProviders(<PropertyCard property={card} />);
    expect(screen.getByAltText(/photo 1/)).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Next photo' }));
    expect(screen.getByAltText(/photo 2/)).toBeInTheDocument();
  });
});

describe('PriceBreakdown', () => {
  it('renders the server quote lines and total', () => {
    const quote = { nights: 3, currency: 'USD', averageNightly: 100, accommodation: 300, discount: 30, cleaningFee: 50, serviceFee: 38.4, taxes: 32, total: 390.4 } as QuoteDto;
    renderWithProviders(<PriceBreakdown quote={quote} />);
    expect(screen.getByText(/× 3 nights/)).toBeInTheDocument();
    expect(screen.getByText('Discounts')).toBeInTheDocument();
    expect(screen.getByText('Total').nextSibling).toHaveTextContent('390.40');
  });
});

describe('AvailabilityCalendar', () => {
  it('disables unavailable nights and selects a range', async () => {
    const blocked = format(addDays(new Date(), 5), 'yyyy-MM-dd');
    const onChange = vi.fn();
    renderWithProviders(<AvailabilityCalendar unavailable={[blocked]} onChange={onChange} months={1} />);
    const blockedButton = screen.getAllByRole('button').find((b) => b.getAttribute('aria-label')?.includes('unavailable'));
    if (blockedButton) expect(blockedButton).toBeDisabled();
    const tomorrow = screen.getByRole('button', { name: new RegExp(format(addDays(new Date(), 1), 'EEEE d MMMM yyyy')) });
    await userEvent.click(tomorrow);
    expect(onChange).toHaveBeenCalledWith(format(addDays(new Date(), 1), 'yyyy-MM-dd'), '');
  });
});

describe('states', () => {
  it('renders empty and error states with actions', async () => {
    const retry = vi.fn();
    renderWithProviders(<><EmptyState title="You haven't saved any stays yet." /><ErrorState error={new ApiError(404, 'Listing not found.')} onRetry={retry} /></>);
    expect(screen.getByText("You haven't saved any stays yet.")).toBeInTheDocument();
    expect(screen.getByRole('alert')).toHaveTextContent('Listing not found.');
    await userEvent.click(screen.getByRole('button', { name: 'Try again' }));
    expect(retry).toHaveBeenCalled();
  });

  it('colours statuses by meaning', () => {
    renderWithProviders(<><StatusBadge status="Confirmed" /><StatusBadge status="PaymentPending" /></>);
    expect(screen.getByText('Confirmed').className).toContain('emerald');
    expect(screen.getByText('Payment Pending').className).toContain('amber');
  });
});
