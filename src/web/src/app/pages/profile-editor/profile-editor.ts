import { AuthService } from '../../core/auth/auth';
import { Component, computed, DOCUMENT, inject, input, OnInit, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import { MatSnackBar } from '@angular/material/snack-bar';
import { marked } from 'marked';
import { RepositoriesApi } from '../../core/api/repositories-api';
import { Profile, ProfileInput } from '../../core/api/api-types';
import { IntegrationMappedTo } from '../../core/api/report';

type ListField = Exclude<keyof ProfileInput, 'markdown' | 'integrationMappedTo'>;

const LIST_FIELDS: readonly { key: ListField; label: string }[] = [
  { key: 'testGlobs', label: 'Test globs' },
  { key: 'controllerGlobs', label: 'Controller globs' },
  { key: 'minimalApiGlobs', label: 'Minimal API files' },
  { key: 'integrationGlobs', label: 'Integration folders / naming' },
  { key: 'integrationAttributes', label: 'Integration attributes' },
  { key: 'integrationBaseTypes', label: 'Integration base types' },
  { key: 'notIntegrationGlobs', label: 'Never integration' },
];

const MAPPINGS: readonly IntegrationMappedTo[] = ['real-seam', 'e2e-mocked-data', 'e2e-real-backend', 'contract', 'other'];

const lines = (text: string): string[] => text.split('\n').map((l) => l.trim()).filter((l) => l.length > 0);

/**
 * `/repos/:id/profile`: the typed classification rules (D18) and the Markdown, with preview, version
 * history and an AI draft. Every save is a new version; a draft is never saved by itself (D22).
 */
@Component({
  selector: 'app-profile-editor',
  imports: [RouterLink],
  templateUrl: './profile-editor.html',
  styleUrl: './profile-editor.scss',
})
export class ProfileEditor implements OnInit {
  private readonly api = inject(RepositoriesApi);
  private readonly auth = inject(AuthService);
  private readonly snackBar = inject(MatSnackBar);
  private readonly document = inject(DOCUMENT);

  readonly id = input.required<string>();

  protected readonly listFields = LIST_FIELDS;
  protected readonly mappings = MAPPINGS;

  /** The latest version: the base of the next save. */
  protected readonly latest = signal<Profile | null>(null);
  protected readonly versions = signal<number[]>([]);

  /** An older version shown read-only, or null while editing. */
  protected readonly viewing = signal<Profile | null>(null);

  protected readonly markdown = signal('');
  protected readonly mapping = signal<IntegrationMappedTo>('real-seam');
  protected readonly lists = signal<Record<ListField, string>>(this.listsOf(null));

  protected readonly saving = signal(false);
  protected readonly drafting = signal(false);
  protected readonly openQuestions = signal<string[]>([]);

  /** Edits since the last load or save. */
  protected readonly dirty = signal(false);

  /** A save was rejected because someone saved a newer version. */
  protected readonly conflict = signal(false);

  /** An old version, or a Viewer (D26): nothing can be edited. */
  protected readonly readOnly = computed(() => this.viewing() !== null || !this.auth.can('Member'));
  protected readonly preview = computed(() => marked.parse(this.markdown(), { async: false }));

  ngOnInit(): void {
    this.loadLatest();
  }

  /** After a 409: take the newer version as the base (unsaved edits are dropped). */
  protected reloadLatest(): void {
    this.conflict.set(false);
    this.viewing.set(null);
    this.loadLatest();
  }

  private loadLatest(): void {
    this.api.profile(this.id()).subscribe((profile) => {
      this.latest.set(profile);
      this.load(profile);
    });
    this.api.profileVersions(this.id()).subscribe((versions) => this.versions.set(versions));
  }

  protected show(version: number): void {
    if (this.dirty() && !(this.document.defaultView?.confirm('Discard your unsaved changes?') ?? true)) {
      return;
    }

    const latest = this.latest();
    if (latest && version === latest.version) {
      this.viewing.set(null);
      this.load(latest);
      return;
    }

    this.api.profile(this.id(), version).subscribe((profile) => {
      this.viewing.set(profile);
      this.load(profile);
    });
  }

  /** Copies the viewed version into the editor; saving it creates a new version. */
  protected restore(): void {
    this.viewing.set(null);
  }

  protected save(): void {
    const latest = this.latest();
    if (!latest || this.readOnly()) {
      return;
    }

    this.saving.set(true);
    const lists = this.lists();
    const input: ProfileInput = {
      markdown: this.markdown(),
      integrationMappedTo: this.mapping(),
      testGlobs: lines(lists.testGlobs),
      controllerGlobs: lines(lists.controllerGlobs),
      minimalApiGlobs: lines(lists.minimalApiGlobs),
      integrationGlobs: lines(lists.integrationGlobs),
      integrationAttributes: lines(lists.integrationAttributes),
      integrationBaseTypes: lines(lists.integrationBaseTypes),
      notIntegrationGlobs: lines(lists.notIntegrationGlobs),
    };
    this.api.saveProfile(this.id(), latest.version, input).subscribe({
      next: (saved) => {
        this.saving.set(false);
        this.latest.set(saved);
        this.versions.update((v) => [saved.version, ...v.filter((x) => x !== saved.version)]);
        this.load(saved);
        this.snackBar.open(`Saved as version ${saved.version}`, 'Dismiss', { duration: 4000 });
      },
      error: (error: unknown) => {
        this.saving.set(false);
        this.conflict.set(error instanceof HttpErrorResponse && error.status === 409);
      },
    });
  }

  protected draft(): void {
    this.drafting.set(true);
    this.api.draftProfile(this.id()).subscribe({
      next: (draft) => {
        this.drafting.set(false);
        this.markdown.set(draft.markdown);
        this.dirty.set(true);
        this.openQuestions.set(draft.openQuestions);
      },
      error: () => this.drafting.set(false),
    });
  }

  protected setList(key: ListField, event: Event): void {
    const value = (event.target as HTMLTextAreaElement).value;
    this.lists.update((l) => ({ ...l, [key]: value }));
    this.dirty.set(true);
  }

  protected value(event: Event): string {
    return (event.target as HTMLTextAreaElement | HTMLSelectElement).value;
  }

  protected setMapping(event: Event): void {
    this.mapping.set(this.value(event) as IntegrationMappedTo);
    this.dirty.set(true);
  }

  protected setMarkdown(event: Event): void {
    this.markdown.set(this.value(event));
    this.dirty.set(true);
  }

  private load(profile: Profile): void {
    this.dirty.set(false);
    this.markdown.set(profile.markdown);
    this.mapping.set(profile.integrationMappedTo);
    this.lists.set(this.listsOf(profile));
  }

  private listsOf(profile: Profile | null): Record<ListField, string> {
    return Object.fromEntries(LIST_FIELDS.map((f) => [f.key, (profile?.[f.key] ?? []).join('\n')])) as Record<ListField, string>;
  }
}
