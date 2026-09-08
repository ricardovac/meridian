import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { TransfersService } from './transfers.service';

const UUID_V4 = /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

describe('TransfersService', () => {
  let service: TransfersService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(TransfersService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('omits accountId when listing every transfer of the user', () => {
    service.list({ page: 1, pageSize: 10 }).subscribe();

    const request = httpMock.expectOne((candidate) => candidate.url === '/api/transfers');
    expect(request.request.params.has('accountId')).toBe(false);
    expect(request.request.params.get('page')).toBe('1');
    expect(request.request.params.get('pageSize')).toBe('10');
    request.flush({ items: [], total: 0, page: 1, pageSize: 10 });
  });

  it('sends accountId when listing a single account', () => {
    service.list({ accountId: 'account-1' }).subscribe();

    const request = httpMock.expectOne((candidate) => candidate.url === '/api/transfers');
    expect(request.request.params.get('accountId')).toBe('account-1');
    request.flush({ items: [], total: 0, page: 1, pageSize: 20 });
  });

  it('posts the transfer with a random Idempotency-Key', () => {
    service
      .create({
        sourceAccountId: 'account-1',
        destinationAccountId: 'account-2',
        amount: 10,
        description: 'Almoço',
      })
      .subscribe();

    const request = httpMock.expectOne('/api/transfers');
    expect(request.request.method).toBe('POST');
    expect(request.request.headers.get('Idempotency-Key')).toMatch(UUID_V4);
    request.flush({});
  });

  it('sends a different Idempotency-Key on each create', () => {
    const first = createAndCaptureKey();
    const second = createAndCaptureKey();

    expect(first).not.toBe(second);
  });

  function createAndCaptureKey(): string | null {
    service
      .create({
        sourceAccountId: 'account-1',
        destinationAccountId: 'account-2',
        amount: 10,
        description: 'Almoço',
      })
      .subscribe();

    const request = httpMock.expectOne('/api/transfers');
    request.flush({});
    return request.request.headers.get('Idempotency-Key');
  }
});
