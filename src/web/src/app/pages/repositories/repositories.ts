import { Component, inject, signal } from '@angular/core';
import { DatePipe, DecimalPipe, PercentPipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { MatDialog } from '@angular/material/dialog';
import { RepositoriesApi } from '../../core/api/repositories-api';
import { RepositorySummary, TrendPoint } from '../../core/api/api-types';
import { AddRepositoryDialog } from '../add-repository-dialog/add-repository-dialog';
import { Sparkline } from '../../shared/sparkline/sparkline';

/** `/`: every repository with its latest scan (doc 04). The trend sparkline is M8. */
@Component({
  selector: 'app-repositories',
  imports: [RouterLink, DatePipe, DecimalPipe, PercentPipe, Sparkline],
  templateUrl: './repositories.html',
  styleUrl: './repositories.scss',
})
export class Repositories {
  private readonly api = inject(RepositoriesApi);
  private readonly dialog = inject(MatDialog);

  protected readonly repositories = signal<RepositorySummary[] | null>(null);

  constructor() {
    this.api.list().subscribe((list) => this.repositories.set(list));
  }

  protected scores(trend: TrendPoint[]): (number | null)[] {
    return trend.map((p) => p.score);
  }

  protected add(): void {
    this.dialog.open(AddRepositoryDialog, { maxWidth: 'calc(100vw - 32px)' });
  }
}
