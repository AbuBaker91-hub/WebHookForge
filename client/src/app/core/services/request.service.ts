import { Injectable }     from '@angular/core';
import { HttpClient }      from '@angular/common/http';
import { API }             from '../constants/api.constants';
import { IncomingRequest, PagedResult } from '../models/request.models';

@Injectable({ providedIn: 'root' })
export class RequestService {
  constructor(private http: HttpClient) {}

  getByEndpoint(endpointId: string, page = 1, pageSize = 25) {
    return this.http.get<PagedResult<IncomingRequest>>(
      API.endpoints.requests(endpointId),
      { params: { page, pageSize } }
    );
  }

  getById(id: string) { return this.http.get<IncomingRequest>(API.requests.byId(id)); }

  purge(endpointId: string, olderThanDays = 30) {
    return this.http.delete<{ value: number }>(
      API.endpoints.requests(endpointId),
      { params: { olderThanDays } }
    );
  }

  analyze(id: string) {
    return this.http.post<{ analysis: string }>(API.requests.analyze(id), {});
  }
}
