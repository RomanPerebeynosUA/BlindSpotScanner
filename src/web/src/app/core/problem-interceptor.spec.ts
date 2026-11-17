import { TestBed } from '@angular/core/testing';
import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { MatSnackBar } from '@angular/material/snack-bar';
import { problemInterceptor } from './problem-interceptor';

describe('problemInterceptor', () => {
  let http: HttpClient;
  let backend: HttpTestingController;
  const open = vi.fn();

  beforeEach(() => {
    open.mockReset();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([problemInterceptor])),
        provideHttpClientTesting(),
        { provide: MatSnackBar, useValue: { open } },
      ],
    });
    http = TestBed.inject(HttpClient);
    backend = TestBed.inject(HttpTestingController);
  });

  it('shows detail, else title, and rethrows', () => {
    // Arrange
    const errors: number[] = [];
    http.get('/api/a').subscribe({ error: (e: { status: number }) => errors.push(e.status) });
    http.get('/api/b').subscribe({ error: (e: { status: number }) => errors.push(e.status) });

    // Act
    backend.expectOne('/api/a').flush({ title: 'Conflict', detail: 'Already registered.' }, { status: 409, statusText: 'Conflict' });
    backend.expectOne('/api/b').flush({ title: 'Invalid repository' }, { status: 400, statusText: 'Bad Request' });

    // Assert
    expect(open.mock.calls.map((c) => c[0])).toEqual(['Already registered.', 'Invalid repository']);
    expect(errors).toEqual([409, 400]);
  });

  it('status 0 shows "Can\'t reach the server"', () => {
    // Arrange
    http.get('/api/a').subscribe({ error: () => undefined });

    // Act
    backend.expectOne('/api/a').error(new ProgressEvent('error'), { status: 0 });

    // Assert
    expect(open.mock.calls[0][0]).toBe("Can't reach the server");
  });
});
