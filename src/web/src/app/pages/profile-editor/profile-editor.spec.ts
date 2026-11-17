import { provideTestAuth } from '../../../testing/auth';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { MatSnackBar } from '@angular/material/snack-bar';
import { of, throwError } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';
import { RepositoriesApi } from '../../core/api/repositories-api';
import { Profile } from '../../core/api/api-types';
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

describe('ProfileEditor', () => {
  const saveProfile = vi.fn();
  const draftProfile = vi.fn();
  const open = vi.fn();

  beforeEach(() => {
    latest = () => profile();
    saveProfile.mockReset();
    draftProfile.mockReset();
    open.mockReset();
  });

  async function render() {
    TestBed.configureTestingModule({
      imports: [ProfileEditor],
      providers: [
        provideTestAuth(),
        provideRouter([]),
        { provide: MatSnackBar, useValue: { open } },
        {
          provide: RepositoriesApi,
          useValue: {
            profile: (_id: string, version?: number) => of(version === 1 ? profile({ version: 1, markdown: '# Old' }) : latest()),
            profileVersions: () => of([2, 1]),
            saveProfile,
            draftProfile,
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
    draftProfile.mockReturnValue(of({ markdown: '# Drafted', openQuestions: ['Is Redis a real seam?'] }));
    const { el, settle } = await render();

    // Act
    (el.querySelector('button.draft') as HTMLButtonElement).click();
    await settle();

    // Assert
    expect(field(el, 'markdown').value).toBe('# Drafted');
    expect(el.querySelector('.questions li')?.textContent?.trim()).toBe('Is Redis a real seam?');
    expect(saveProfile).not.toHaveBeenCalled();
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
});
