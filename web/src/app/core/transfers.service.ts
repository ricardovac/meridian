import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { CreateTransferRequest, Paged, Transfer } from './models';

const TRANSFERS_URL = '/api/transfers';

export interface TransferListOptions {
  accountId?: string;
  page?: number;
  pageSize?: number;
}

@Injectable({ providedIn: 'root' })
export class TransfersService {
  private readonly http = inject(HttpClient);

  create(request: CreateTransferRequest): Observable<Transfer> {
    return this.http.post<Transfer>(TRANSFERS_URL, request, {
      headers: { 'Idempotency-Key': crypto.randomUUID() },
    });
  }

  list(options: TransferListOptions = {}): Observable<Paged<Transfer>> {
    let params = new HttpParams();
    if (options.accountId !== undefined) {
      params = params.set('accountId', options.accountId);
    }
    if (options.page !== undefined) {
      params = params.set('page', options.page);
    }
    if (options.pageSize !== undefined) {
      params = params.set('pageSize', options.pageSize);
    }
    return this.http.get<Paged<Transfer>>(TRANSFERS_URL, { params });
  }
}
