import { useEffect } from 'react';
import { HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr';
import { useQueryClient } from '@tanstack/react-query';
import { useAuthStore } from './auth-store';
import { toast } from './toast';
import type { MessageDto, NotificationDto } from '@/api/types';

let connection: HubConnection | null = null;
type Handler = (...args: unknown[]) => void;
const listeners = new Map<string, Set<Handler>>();

function ensureConnection(): HubConnection {
  if (connection) return connection;
  connection = new HubConnectionBuilder()
    .withUrl('/hubs/realtime', { accessTokenFactory: () => useAuthStore.getState().accessToken ?? '' })
    .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
    .configureLogging(LogLevel.Warning)
    .build();
  for (const event of ['ReceiveMessage', 'Notification', 'Typing', 'ReadReceipt']) {
    connection.on(event, (...args: unknown[]) => listeners.get(event)?.forEach((h) => h(...args)));
  }
  return connection;
}

export function onRealtime(event: string, handler: Handler): () => void {
  if (!listeners.has(event)) listeners.set(event, new Set());
  listeners.get(event)!.add(handler);
  return () => listeners.get(event)?.delete(handler);
}

export async function invokeRealtime(method: string, ...args: unknown[]) {
  const c = ensureConnection();
  if (c.state === HubConnectionState.Connected) await c.invoke(method, ...args);
}

/** Connects while signed in; keeps notification + message caches fresh and shows toasts. */
export function useRealtime() {
  const token = useAuthStore((s) => s.accessToken);
  const qc = useQueryClient();

  useEffect(() => {
    if (!token) return;
    const c = ensureConnection();
    if (c.state === HubConnectionState.Disconnected) c.start().catch(() => undefined);
    const offN = onRealtime('Notification', (n) => {
      const notification = n as NotificationDto;
      qc.invalidateQueries({ queryKey: ['notifications'] });
      if (notification.type !== 'message') toast.info(notification.title);
    });
    const offM = onRealtime('ReceiveMessage', (m) => {
      const message = m as MessageDto;
      qc.invalidateQueries({ queryKey: ['conversations'] });
      qc.invalidateQueries({ queryKey: ['messages', message.conversationId] });
    });
    return () => {
      offN();
      offM();
    };
  }, [token, qc]);

  useEffect(() => {
    if (token || !connection) return;
    connection.stop().catch(() => undefined);
    connection = null;
  }, [token]);
}
