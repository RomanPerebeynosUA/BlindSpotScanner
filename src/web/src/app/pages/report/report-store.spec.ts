import { TestBed } from '@angular/core/testing';
import { sampleReport } from '../../../testing/sample-report';
import { commitUrl, evidenceUrl, ReportStore } from './report-store';

describe('ReportStore', () => {
  let store: ReportStore;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [ReportStore] });
    store = TestBed.inject(ReportStore);
    store.report.set(sampleReport());
  });

  const visible = () => store.visibleControllers().map((c) => [c.controller, c.endpoints.map((e) => `${e.method} ${e.route}`)]);

  it('statusCounts counts endpoints by status', () => {
    // Act
    const counts = store.statusCounts();

    // Assert
    expect(counts).toEqual({ all: 5, covered: 3, weak: 1, skipped: 0, none: 1, unverified: 0 });
  });

  it('filter weak shows only BooksController with GET api/books/{id}', () => {
    // Act
    store.statusFilter.set('weak');

    // Assert
    expect(visible()).toEqual([['BooksController', ['GET api/books/{id}']]]);
  });

  it('search "cancel" finds POST api/orders/{id}/cancel', () => {
    // Act
    store.search.set('CANCEL');

    // Assert
    expect(visible()).toEqual([['OrdersController', ['POST api/orders/{id}/cancel']]]);
  });

  it('search matches evidence citations', () => {
    // Act
    store.search.set('Create_persists');

    // Assert
    expect(visible()).toEqual([['BooksController', ['POST api/books']]]);
  });

  it('search matches the controller name', () => {
    // Act
    store.search.set('orderscontroller');

    // Assert
    expect(visible()).toEqual([['OrdersController', ['POST api/orders', 'POST api/orders/{id}/cancel']]]);
  });

  it('evidenceUrl builds a blob link at the pinned sha', () => {
    // Act
    const url = evidenceUrl(sampleReport().meta, 'tests/Bookstore.IntegrationTests/BooksApiTests.cs::List_returns_seeded_books');

    // Assert
    expect(url).toBe('https://github.com/example-org/bookstore-api/blob/a1b2c3d4e5f60718293a4b5c6d7e8f9012345678/tests/Bookstore.IntegrationTests/BooksApiTests.cs');
  });

  it('evidenceUrl encodes spaces and #', () => {
    // Act
    const url = evidenceUrl(sampleReport().meta, 'tests/My Tests/C#Tests.cs::A');

    // Assert
    expect(url.endsWith('/tests/My%20Tests/C%23Tests.cs')).toBe(true);
  });

  it('commitUrl links the pinned commit', () => {
    // Act
    const url = commitUrl(sampleReport().meta);

    // Assert
    expect(url).toBe('https://github.com/example-org/bookstore-api/commit/a1b2c3d4e5f60718293a4b5c6d7e8f9012345678');
  });
});
