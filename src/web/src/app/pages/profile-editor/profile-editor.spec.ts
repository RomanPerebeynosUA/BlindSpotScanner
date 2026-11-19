import { MatDialog } from '@angular/material/dialog';
import { provideTestAuth } from '../../../testing/auth';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { MatSnackBar } from '@angular/material/snack-bar';
import { of, throwError } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';
import { RepositoriesApi } from '../../core/api/repositories-api';
import { PatternMatch, Profile, ProfileDraft } from '../../core/api/api-types';
import { Role } from '../../core/auth/auth';
import { ProfileEditor } from './profile-editor';

const profile = (overrides: Partial<Profile> = {}): Profile => ({
  version: 2,
  markdown: '# Repo profile',
  integrationMappedTo: 'real-seam',
  testGlobs: ['tests/**/*.cs'],
  controllerGlobs: ['src/**/Controllers/**/*Controller.cs'],
  minimalApiGlobs: [],
  integrationGlobs: ['tests/*.IntegrationTests/**'],
  integrationAttributes: [],
  integrationBaseTypes: ['WebApplicationFactory'],
  notIntegrationGlobs: [],
  ...overrides,
});

const draftOf = (overrides: Partial<ProfileDraft> = {}): ProfileDraft => ({
  markdown: '# Drafted',
  openQuestions: [],
  rules: {
    integrationMappedTo: 'e2e-mocked-data',
    testGlobs: ['Todo.Api.Tests/**/*.cs'],
    controllerGlobs: [],
    minimalApiGlobs: ['**/*Api.cs'],
    integrationGlobs: ['Todo.Api.Tests/**'],
    integrationAttributes: [],
    integrationBaseTypes: ['WebApplicationFactory'],
    notIntegrationGlobs: ['**/Nope.cs'],
  },
  matches: [
    { field: 'testGlobs', pattern: 'Todo.Api.Tests/**/*.cs', files: 6 },
    { field: 'minimalApiGlobs', pattern: '**/*Api.cs', files: 4 },
    { field: 'notIntegrationGlobs', pattern: '**/Nope.cs', files: 0 },
  ],
  ...overrides,
});

describe('ProfileEditor', () => {
  const saveProfile = vi.fn();
  const draftProfile = vi.fn();
  const profileMatches = vi.fn();
  const acknowledgeLlmSharing = vi.fn(() => of(undefined));
  const dialogOpen = vi.fn(() => ({ afterClosed: () => of(true) }));
  const open = vi.fn();

  beforeEach(() => {
    latest = () => profile();
    saveProfile.mockReset();
    draftProfile.mockReset();
    profileMatches.mockReset();
    open.mockReset();
  });

  async function render(role: Role = 'Admin') {
    TestBed.configureTestingModule({
      imports: [ProfileEditor],
      providers: [
        provideTestAuth(role),
        provideRouter([]),
        { provide: MatSnackBar, useValue: { open } },
        { provide: MatDialog, useValue: { open: dialogOpen } },
        {
          provide: RepositoriesApi,
          useValue: {
            profile: (_id: string, version?: number) => of(version === 1 ? profile({ version: 1, markdown: '# Old' }) : latest()),
            profileVersions: () => of([2, 1]),
            saveProfile,
            draftProfile,
            profileMatches,
            acknowledgeLlmSharing,
          },
        },
      ],
    });
    const fixture = TestBed.createComponent(ProfileEditor);
    fixture.componentRef.setInput('id', 'r1');
    await fixture.whenStable();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const settle = async () => {
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();
    };
    return { el, settle };
  }

  let latest = () => profile();

  const field = (el: HTMLElement, name: string) => el.querySelector(`[name=${name}]`) as HTMLTextAreaElement;

  function type(el: HTMLElement, name: string, value: string): void {
    const input = field(el, name);
    input.value = value;
    input.dispatchEvent(new Event('input'));
  }

  it('loads latest profile into the form', async () => {
    // Act
    const { el } = await render();

    // Assert
    expect(field(el, 'markdown').value).toBe('# Repo profile');
    expect(field(el, 'testGlobs').value).toBe('tests/**/*.cs');
    expect(field(el, 'integrationBaseTypes').value).toBe('WebApplicationFactory');
    expect((el.querySelector('[name=integrationMappedTo]') as HTMLSelectElement).value).toBe('real-seam');
  });

  it('save sends trimmed lines and the loaded base version', async () => {
    // Arrange
    saveProfile.mockReturnValue(of(profile({ version: 3 })));
    const { el, settle } = await render();
    type(el, 'testGlobs', ' tests/a/**  \n\n tests/b/** ');
    await settle();

    // Act
    (el.querySelector('button.save') as HTMLButtonElement).click();
    await settle();

    // Assert
    expect(saveProfile).toHaveBeenCalledWith('r1', 2, expect.objectContaining({ testGlobs: ['tests/a/**', 'tests/b/**'], markdown: '# Repo profile' }));
  });

  it('save success shows the new version', async () => {
    // Arrange
    saveProfile.mockReturnValue(of(profile({ version: 3 })));
    const { el, settle } = await render();

    // Act
    (el.querySelector('button.save') as HTMLButtonElement).click();
    await settle();

    // Assert
    expect(open).toHaveBeenCalledWith('Saved as version 3', 'Dismiss', expect.anything());
  });

  it('preview renders markdown headings', async () => {
    // Arrange
    const { el, settle } = await render();

    // Act
    type(el, 'markdown', '## Basics');
    await settle();

    // Assert
    expect(el.querySelector('.preview h2')?.textContent).toBe('Basics');
  });

  it('preview strips script tags', async () => {
    // Arrange
    const { el, settle } = await render();

    // Act
    type(el, 'markdown', 'Hi <script>alert(1)</script>');
    await settle();

    // Assert
    expect(el.querySelector('.preview script')).toBeNull();
  });

  it('auto-draft replaces markdown and shows open questions without saving', async () => {
    // Arrange
    draftProfile.mockReturnValue(of(draftOf({ openQuestions: ['Is Redis a real seam?'] })));
    const { el, settle } = await render();

    // Act
    (el.querySelector('button.draft') as HTMLButtonElement).click();
    await settle();

    // Assert
    expect(field(el, 'markdown').value).toBe('# Drafted');
    expect(el.querySelector('.questions li')?.textContent?.trim()).toBe('Is Redis a real seam?');
    expect(saveProfile).not.toHaveBeenCalled();
  });


  const matchLines = (el: HTMLElement, name: string) =>
    [...(field(el, name).closest('label')?.querySelectorAll('.matches li') ?? [])].map((li) => li.textContent?.trim());

  async function drafted() {
    draftProfile.mockReturnValue(of(draftOf()));
    const rendered = await render();
    (rendered.el.querySelector('button.draft') as HTMLButtonElement).click();
    await rendered.settle();
    return rendered;
  }

  it('auto-draft fills every rule field and the mapping', async () => {
    // Act
    const { el } = await drafted();

    // Assert
    expect([field(el, 'minimalApiGlobs').value, field(el, 'controllerGlobs').value, (el.querySelector('[name=integrationMappedTo]') as HTMLSelectElement).value])
      .toEqual(['**/*Api.cs', '', 'e2e-mocked-data']);
  });

  it('auto-draft shows how many files each pattern matches', async () => {
    // Act
    const { el } = await drafted();

    // Assert
    expect(matchLines(el, 'minimalApiGlobs')).toEqual(['**/*Api.cs — 4 files']);
  });

  it('a pattern matching no file is marked as a warning', async () => {
    // Act
    const { el } = await drafted();

    // Assert
    expect(field(el, 'notIntegrationGlobs').closest('label')?.querySelector('.matches li.zero')?.textContent?.trim()).toBe('**/Nope.cs — 0 files');
  });

  it('editing a field hides its stale counts', async () => {
    // Arrange
    const { el, settle } = await drafted();

    // Act
    type(el, 'minimalApiGlobs', 'src/**/*Api.cs');
    await settle();

    // Assert
    expect([matchLines(el, 'minimalApiGlobs'), matchLines(el, 'testGlobs')]).toEqual([[], ['Todo.Api.Tests/**/*.cs — 6 files']]);
  });

  it('check patterns sends the current lists and shows the new counts', async () => {
    // Arrange
    const counted: PatternMatch[] = [{ field: 'minimalApiGlobs', pattern: 'src/**/*Api.cs', files: 1 }];
    profileMatches.mockReturnValue(of({ matches: counted, errors: [] }));
    const { el, settle } = await render();
    type(el, 'minimalApiGlobs', 'src/**/*Api.cs');

    // Act
    (el.querySelector('button.check') as HTMLButtonElement).click();
    await settle();

    // Assert
    expect([profileMatches.mock.calls[0][1].minimalApiGlobs, matchLines(el, 'minimalApiGlobs')]).toEqual([['src/**/*Api.cs'], ['src/**/*Api.cs — 1 file']]);
  });

  it('check patterns lists syntax errors', async () => {
    // Arrange
    profileMatches.mockReturnValue(of({ matches: [], errors: ["testGlobs: 'a b' is not one pattern"] }));
    const { el, settle } = await render();

    // Act
    (el.querySelector('button.check') as HTMLButtonElement).click();
    await settle();

    // Assert
    expect(el.querySelector('.pattern-errors li')?.textContent?.trim()).toBe("testGlobs: 'a b' is not one pattern");
  });

  it('check patterns is available to a Viewer', async () => {
    // Act
    const { el } = await render('Viewer');

    // Assert
    expect((el.querySelector('button.check') as HTMLButtonElement | null)?.disabled).toBe(false);
  });

  it('switching versions with unsaved edits asks first and keeps them on cancel', async () => {
    // Arrange
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(false);
    const { el, settle } = await render();
    type(el, 'markdown', '# My unsaved edit');
    await settle();

    // Act
    (el.querySelector('.versions button[data-version="1"]') as HTMLButtonElement).click();
    await settle();

    // Assert
    expect(confirm).toHaveBeenCalled();
    expect(field(el, 'markdown').value).toBe('# My unsaved edit');
    confirm.mockRestore();
  });

  it('switching versions without edits does not ask', async () => {
    // Arrange
    const confirm = vi.spyOn(window, 'confirm');
    const { el, settle } = await render();

    // Act
    (el.querySelector('.versions button[data-version="1"]') as HTMLButtonElement).click();
    await settle();

    // Assert
    expect(confirm).not.toHaveBeenCalled();
    confirm.mockRestore();
  });

  it('after a 409 Reload latest loads the newer version as the base', async () => {
    // Arrange
    saveProfile.mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 409 })));
    const { el, settle } = await render();
    (el.querySelector('button.save') as HTMLButtonElement).click();
    await settle();
    latest = () => profile({ version: 3, markdown: '# Theirs' });

    // Act
    (el.querySelector('button.reload') as HTMLButtonElement).click();
    await settle();

    // Assert
    expect(field(el, 'markdown').value).toBe('# Theirs');
    saveProfile.mockReturnValue(of(profile({ version: 4 })));
    (el.querySelector('button.save') as HTMLButtonElement).click();
    expect(saveProfile).toHaveBeenLastCalledWith('r1', 3, expect.anything());
  });

  it('older version is read-only until restored', async () => {
    // Arrange
    const { el, settle } = await render();

    // Act
    (el.querySelector('.versions button[data-version="1"]') as HTMLButtonElement).click();
    await settle();

    // Assert
    expect(field(el, 'markdown').value).toBe('# Old');
    expect(field(el, 'markdown').readOnly).toBe(true);
    expect((el.querySelector('button.save') as HTMLButtonElement).disabled).toBe(true);
    (el.querySelector('button.restore') as HTMLButtonElement).click();
    await settle();
    expect(field(el, 'markdown').readOnly).toBe(false);
    expect(field(el, 'markdown').value).toBe('# Old');
  });

  it('a draft refused for LLM sharing asks for consent, records it, then drafts', async () => {
    // Arrange (doc 10: drafting sends code to the LLM too)
    const refused = new HttpErrorResponse({ status: 409, error: { title: 'LLM data sharing not acknowledged' } });
    draftProfile.mockReturnValueOnce(throwError(() => refused)).mockReturnValueOnce(of(draftOf()));
    const { el, settle } = await render();

    // Act
    (el.querySelector('button.draft') as HTMLButtonElement).click();
    await settle();

    // Assert
    expect([dialogOpen.mock.calls.length, acknowledgeLlmSharing.mock.calls.length, draftProfile.mock.calls.length]).toEqual([1, 1, 2]);
  });
});
