import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MatDialogModule } from '@angular/material/dialog';
import { provideRouter } from '@angular/router';
import { Transfer } from '../../core/models';
import { DashboardPage } from './dashboard-page';

const DEPOSIT: Transfer = {
  id: 'transfer-1',
  sourceAccountId: 'system-account',
  destinationAccountId: 'account-1',
  amount: 50,
  currency: 'BRL',
  description: 'Deposit',
  status: 'Completed',
  createdAt: '2026-01-15T12:00:00Z',
};

describe('DashboardPage', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [DashboardPage, MatDialogModule],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();
  });

  it('asks for the transfers of every account, without an accountId filter', () => {
    TestBed.createComponent(DashboardPage);
    const httpMock = TestBed.inject(HttpTestingController);

    const transfers = httpMock.expectOne((request) => request.url === '/api/transfers');

    expect(transfers.request.method).toBe('GET');
    expect(transfers.request.params.has('accountId')).toBe(false);
    expect(transfers.request.params.get('page')).toBe('1');
    expect(transfers.request.params.get('pageSize')).toBe('10');

    transfers.flush({ items: [], total: 0, page: 1, pageSize: 10 });
    httpMock.expectOne('/api/accounts').flush([]);
    httpMock.verify();
  });

  it('renders the recent transfers returned by the unfiltered listing', async () => {
    const fixture = TestBed.createComponent(DashboardPage);
    const httpMock = TestBed.inject(HttpTestingController);

    httpMock
      .expectOne((request) => request.url === '/api/transfers')
      .flush({ items: [DEPOSIT], total: 1, page: 1, pageSize: 10 });
    httpMock.expectOne('/api/accounts').flush([]);
    await fixture.whenStable();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.textContent).toContain('Transferências recentes');
    expect(compiled.textContent).not.toContain('Nenhuma transferência ainda.');
    expect(compiled.querySelectorAll('table tbody tr')).toHaveLength(1);
    httpMock.verify();
  });
});
