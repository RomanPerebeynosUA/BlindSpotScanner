import { Component, inject, signal } from '@angular/core';
import { KeyValuePipe } from '@angular/common';
import { SettingsApi } from '../../core/api/settings-api';
import { ConnectionStatus, ConnectionTestResult } from '../../core/api/api-types';

const TITLES: Record<string, string> = { github: 'GitHub', jira: 'Jira', llm: 'Claude' };

/** `/settings` (Admin, D26): which connections are configured, secrets masked, with a live Test. Values come from env vars. */
@Component({
  selector: 'app-settings',
  imports: [KeyValuePipe],
  templateUrl: './settings.html',
  styleUrl: './settings.scss',
})
export class Settings {
  private readonly api = inject(SettingsApi);

  protected readonly titles = TITLES;
  protected readonly connections = signal<ConnectionStatus[]>([]);
  protected readonly results = signal<Record<string, ConnectionTestResult | 'testing'>>({});

  constructor() {
    this.api.connections().subscribe((c) => this.connections.set(c));
  }

  protected test(kind: string): void {
    this.results.update((r) => ({ ...r, [kind]: 'testing' }));
    this.api.test(kind).subscribe({
      next: (result) => this.results.update((r) => ({ ...r, [kind]: result })),
      error: () => this.results.update((r) => ({ ...r, [kind]: { ok: false, message: 'The test request failed.' } })),
    });
  }

  protected result(kind: string): ConnectionTestResult | 'testing' | undefined {
    return this.results()[kind];
  }

  protected outcome(kind: string): ConnectionTestResult | null {
    const r = this.results()[kind];
    return r && r !== 'testing' ? r : null;
  }
}
