import { Injectable } from '@angular/core';
import { HttpClient }  from '@angular/common/http';
import { API }         from '../constants/api.constants';
import { RagAnswer, RagAskRequest, RagIngestResult } from '../models/rag.models';

/**
 * Client for the retrieval-augmented Q&A feature.
 *   ingest() — (re)build the pgvector index for an endpoint's captured webhooks.
 *   ask()    — ask a natural-language question grounded in that indexed history.
 */
@Injectable({ providedIn: 'root' })
export class RagService {
  constructor(private http: HttpClient) {}

  ingest(endpointId: string) {
    return this.http.post<RagIngestResult>(API.endpoints.ragIngest(endpointId), {});
  }

  ask(endpointId: string, question: string, topK = 5) {
    const body: RagAskRequest = { question, topK };
    return this.http.post<RagAnswer>(API.endpoints.ragAsk(endpointId), body);
  }
}
