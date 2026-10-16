import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { AssistantWidget } from './AssistantWidget';
import { renderWithProviders } from '@/test/utils';

const json = (body: unknown) => new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } });

const agents = [
  { key: 'travel', name: 'TravelAssistantAgent', displayName: 'Travel assistant', description: 'Finds stays', tools: [] },
  { key: 'support', name: 'SupportAgent', displayName: 'Support', description: 'Helps with trips', tools: [] },
];

describe('AssistantWidget', () => {
  beforeAll(() => { Element.prototype.scrollIntoView = vi.fn(); }); // not implemented by jsdom
  afterEach(() => vi.restoreAllMocks());

  it('lists the agents the server allows and sends messages to the selected one', async () => {
    const fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = String(input);
      if (url.endsWith('/features')) return json({ AiAssistant: true });
      if (url.endsWith('/ai/agents')) return json(agents);
      return json({ conversationId: 'c1', reply: 'Your upcoming trips: none', properties: [], pendingAction: null, toolsUsed: [], provider: 'Rules', agent: 'support' });
    });

    renderWithProviders(<AssistantWidget />);
    await userEvent.click(screen.getByRole('button', { name: 'Open AI travel assistant' }));
    await userEvent.click(await screen.findByRole('tab', { name: 'Support' }));
    expect(screen.getByRole('tab', { name: 'Support' })).toHaveAttribute('aria-selected', 'true');

    await userEvent.type(screen.getByLabelText('Message the assistant'), 'show my trips');
    await userEvent.click(screen.getByRole('button', { name: 'Send' }));

    expect(await screen.findByText('Your upcoming trips: none')).toBeInTheDocument();
    await waitFor(() => {
      const call = fetchSpy.mock.calls.find(([u]) => String(u).endsWith('/ai/messages'));
      expect(JSON.parse(String(call![1]!.body))).toMatchObject({ message: 'show my trips', agent: 'support' });
    });
  });
});
