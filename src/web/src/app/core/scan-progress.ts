import { inject, Injectable, InjectionToken, Signal, signal } from '@angular/core';
import { HttpTransportType, HubConnection, HubConnectionBuilder } from '@microsoft/signalr';
import { RunStatus } from './api/api-types';

export interface ScanProgress {
  runId: string;
  phase: string;
  pct: number;
  message: string;
  at: Date;
}

export interface ScanWatch {
  events: Signal<ScanProgress[]>;
  completed: Signal<RunStatus | null>;
  stop(): Promise<void>;
}

/** Builds the `/hubs/scans` connection; specs swap it for a fake. */
export const HUB_CONNECTION_FACTORY = new InjectionToken<() => HubConnection>('HUB_CONNECTION_FACTORY', {
  providedIn: 'root',
  factory: () => () =>
    new HubConnectionBuilder()
      .withUrl('/hubs/scans', { skipNegotiation: true, transport: HttpTransportType.WebSockets })
      .withAutomaticReconnect()
      .build(),
});

/**
 * Live progress of one run over SignalR (doc 04): `ScanProgress(runId, phase, pct, message)` and
 * `ScanCompleted(runId, status)`. Events are best effort (D21), so pages also poll the run.
 */
@Injectable({ providedIn: 'root' })
export class ScanProgressService {
  private readonly create = inject(HUB_CONNECTION_FACTORY);

  watch(runId: string): ScanWatch {
    const events = signal<ScanProgress[]>([]);
    const completed = signal<RunStatus | null>(null);
    const connection = this.create();

    connection.on('ScanProgress', (id: string, phase: string, pct: number, message: string) => {
      if (id === runId) {
        events.update((list) => [...list, { runId: id, phase, pct, message, at: new Date() }]);
      }
    });
    connection.on('ScanCompleted', (id: string, status: RunStatus) => {
      if (id === runId) {
        completed.set(status);
      }
    });

    // A reconnect gets a new connection id, so the group membership must be renewed.
    connection.onreconnected(() => void subscribe());
    const subscribe = () => connection.invoke('Subscribe', runId).catch(() => undefined);
    const started = connection.start().then(subscribe).catch(() => undefined);

    return {
      events: events.asReadonly(),
      completed: completed.asReadonly(),
      stop: async () => {
        await started;
        await connection.invoke('Unsubscribe', runId).catch(() => undefined);
        await connection.stop();
      },
    };
  }
}
