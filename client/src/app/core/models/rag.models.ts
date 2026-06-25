export interface RagAskRequest {
  question: string;
  topK?:    number;
}

export interface RagCitation {
  requestId:  string;
  method:     string;
  path?:      string;
  receivedAt: string;
  score:      number;   // cosine similarity in [0,1]
  snippet:    string;
}

export interface RagAnswer {
  answer:          string;
  citations:       RagCitation[];
  chunksRetrieved: number;
}

export interface RagIngestResult {
  requestsProcessed: number;
  chunksIndexed:     number;
}
