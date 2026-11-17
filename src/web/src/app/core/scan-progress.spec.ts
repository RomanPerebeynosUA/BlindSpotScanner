import { TestBed } from '@angular/core/testing';
import { HubConnection } from '@microsoft/signalr';
import { HUB_CONNECTION_FACTORY, ScanProgressService } from './scan-progress';

/** Just the HubConnection members the service uses. */
class FakeHub {
  readonly handlers = new Map<string, (...args: unknown[]) => void>();
  reconnected: (() => void) | null = null;
  readonly invoke = vi.fn().mockResolvedValue(undefined);
  readonly start = vi.fn().mockResolvedValue(undefined);
  readonly stop = vi.fn().mockResolvedValue(undefined);

  on(name: string, handler: (...args: unknown[]) => void): void {
    this.handlers.set(name, handler);
  }

  onreconnected(callback: () => void): void {
    this.reconnected = callback;
  }

  emit(name: string, ...args: unknown[]): void {
    this.handlers.get(name)?.(...args);
  }
}

describe('ScanProgressService', () => {
  let hub: FakeHub;
  let service: ScanProgressService;

  beforeEach(() => {
    hub = new FakeHub();
    TestBed.configureTestingModule({ providers: [{ provide: HUB_CONNECTION_FACTORY, useValue: () => hub as unknown as HubConnection }] });
    service = TestBed.inject(ScanProgressService);
  });

  const flush = () => new Promise((resolve) => setTimeout(resolve));

  it('subscribes to the run after start', async () => {
    // Act
    service.watch('run1');
    await flush();

    // Assert
    expect(hub.start).toHaveBeenCalled();
    expect(hub.invoke).toHaveBeenCalledWith('Subscribe', 'run1');
  });

  it('appends progress events in order', async () => {
    // Arrange
    const watch = service.watch('run1');
    await flush();

    // Act
    hub.emit('ScanProgress', 'run1', 'P1', 0, 'P1 started');
    hub.emit('ScanProgress', 'run1', 'P1', 12, 'P1 done');

    // Assert
    expect(watch.events().map((e) => [e.phase, e.pct, e.message])).toEqual([['P1', 0, 'P1 started'], ['P1', 12, 'P1 done']]);
  });

  it('ignores events for other runs', async () => {
    // Arrange
    const watch = service.watch('run1');
    await flush();

    // Act
    hub.emit('ScanProgress', 'other', 'P1', 0, 'P1 started');
    hub.emit('ScanCompleted', 'other', 'Succeeded');

    // Assert
    expect([watch.events().length, watch.completed()]).toEqual([0, null]);
  });

  it('sets completed on ScanCompleted', async () => {
    // Arrange
    const watch = service.watch('run1');
    await flush();

    // Act
    hub.emit('ScanCompleted', 'run1', 'Succeeded');

    // Assert
    expect(watch.completed()).toBe('Succeeded');
  });

  it('resubscribes after reconnect', async () => {
    // Arrange
    service.watch('run1');
    await flush();
    hub.invoke.mockClear();

    // Act
    hub.reconnected?.();
    await flush();

    // Assert
    expect(hub.invoke).toHaveBeenCalledWith('Subscribe', 'run1');
  });

  it('stop unsubscribes and stops', async () => {
    // Arrange
    const watch = service.watch('run1');
    await flush();

    // Act
    await watch.stop();

    // Assert
    expect(hub.invoke).toHaveBeenCalledWith('Unsubscribe', 'run1');
    expect(hub.stop).toHaveBeenCalled();
  });
});
