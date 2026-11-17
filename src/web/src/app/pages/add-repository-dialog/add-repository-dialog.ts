import { Component, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { Router } from '@angular/router';
import { MatDialogRef } from '@angular/material/dialog';
import { RepositoriesApi } from '../../core/api/repositories-api';

/** Registers a repository by URL; then the user confirms its profile (prototype "Add repository"). */
@Component({
  selector: 'app-add-repository-dialog',
  templateUrl: './add-repository-dialog.html',
  styleUrl: './add-repository-dialog.scss',
})
export class AddRepositoryDialog {
  private readonly api = inject(RepositoriesApi);
  private readonly router = inject(Router);
  private readonly dialog = inject(MatDialogRef<AddRepositoryDialog>);

  protected readonly url = signal('');
  protected readonly branch = signal('');
  protected readonly busy = signal(false);

  protected submit(event: Event): void {
    event.preventDefault();
    if (!this.url().trim() || this.busy()) {
      return;
    }

    this.busy.set(true);
    this.api.add(this.url().trim(), this.branch().trim() || undefined).subscribe({
      next: (repository) => {
        this.dialog.close();
        void this.router.navigate(['/repos', repository.id, 'profile']);
      },
      error: (error: unknown) => {
        this.busy.set(false);
        const existing = error instanceof HttpErrorResponse && error.status === 409 ? (error.error as { id?: string } | null)?.id : undefined;
        if (existing) {
          this.dialog.close();
          void this.router.navigate(['/repos', existing]);
        }
      },
    });
  }

  protected cancel(): void {
    this.dialog.close();
  }

  protected read(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }
}
