import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { HostedPayoutAccount } from './HostedPayoutAccount';
import { renderWithProviders } from '@/test/utils';

const json = (body: unknown) => new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } });

describe('HostedPayoutAccount', () => {
  const assign = vi.fn();
  beforeEach(() => {
    assign.mockReset();
    vi.stubGlobal('location', { ...window.location, assign });
  });
  afterEach(() => { vi.restoreAllMocks(); vi.unstubAllGlobals(); });

  it('sends hosts without an account to Stripe onboarding', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(json({ url: 'https://connect.stripe.com/setup/e/acct_1/abc' }));
    renderWithProviders(<HostedPayoutAccount account={null} />);

    await userEvent.click(screen.getByRole('button', { name: /set up payouts/i }));

    await waitFor(() => expect(assign).toHaveBeenCalledWith('https://connect.stripe.com/setup/e/acct_1/abc'));
  });

  it('never follows a non-https onboarding link', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(json({ url: 'javascript:alert(1)' }));
    renderWithProviders(<HostedPayoutAccount account={null} />);

    await userEvent.click(screen.getByRole('button', { name: /set up payouts/i }));

    await waitFor(() => expect(globalThis.fetch).toHaveBeenCalled());
    expect(assign).not.toHaveBeenCalled();
  });

  it('shows what Stripe still needs while payouts are not enabled', () => {
    renderWithProviders(<HostedPayoutAccount account={{ accountHolder: 'Ana', maskedAccount: 'Pending setup', country: 'PT', payoutsEnabled: false, requirement: 'Upload an ID document' }} />);

    expect(screen.getByRole('status')).toHaveTextContent('Upload an ID document');
    expect(screen.getByRole('button', { name: /continue setup/i })).toBeEnabled();
  });
});
