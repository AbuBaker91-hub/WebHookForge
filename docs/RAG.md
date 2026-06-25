# RAG — "Ask your webhooks"

Retrieval-augmented Q&A over an endpoint's captured webhook history. This doc is both the
feature reference and an **interview-defense brief**: it explains not just *what* the code does
but *why* each decision was made, so the design can be discussed confidently.

---

## What it does

> "Were there any failed payments, and why?" → a plain-English answer grounded in the actual
> captured webhooks, with citations back to the specific requests.

Instead of keyword search, it searches by **meaning**: every captured request is converted to a
vector, the question is converted to a vector, and the closest requests (by cosine similarity)
are fed to an LLM that answers using only that retrieved context.

---

## Pipeline (two phases)

**Ingest** (`POST /api/endpoints/{id}/rag/ingest`)
1. Read the endpoint's captured `IncomingRequest`s from SQL Server (cap: newest 1000).
2. Flatten each into a document (method, path, content-type, headers, body) — `TextChunker.BuildDocument`.
3. Split into overlapping windows (1800 chars, 200 overlap) — `TextChunker.Split`.
4. Embed in batches of 64 via OpenAI `text-embedding-3-small` (1536-dim) — `OpenAiEmbeddingService`.
5. Full-rebuild upsert into pgvector (`request_chunks`) — idempotent per endpoint.

**Ask** (`POST /api/endpoints/{id}/rag/ask`)
1. Embed the question (same model).
2. `ORDER BY embedding <=> queryVector LIMIT topK` — cosine distance, HNSW index (`RagService.AskAsync`).
3. Build a grounded prompt with the retrieved chunks as numbered context.
4. Generate the answer with the **user's** provider (Claude/Gemini/Groq) via `IAiAnalysisService.CompleteAsync`.
5. Return answer + citations (source request id, method, path, similarity score, snippet).

---

## Architecture decisions (the "why")

**Why a separate PostgreSQL + pgvector store instead of the existing SQL Server DB?**
Vector similarity search is a different workload from OLTP — it needs an ANN index (HNSW) and a
native vector type. pgvector is the mature, purpose-built option. Isolating it means the vector
index can be re-indexed, scaled, or dropped without touching the transactional database, and the
core app keeps running even if Postgres is down (the RAG services register only when
`ConnectionStrings:RagVectorStore` is set). Trade-off: two stores to operate, and citations carry
denormalised request metadata so there's no cross-database join.

**Why `text-embedding-3-small`?** Strong quality-per-dollar, 1536 dims (small index, fast search),
and the de-facto default — cheap enough to embed full webhook history. `text-embedding-3-large`
(3072 dims) is the swap if retrieval quality needs a bump; the column dimension would change with it.

**Why HNSW + cosine?** HNSW gives fast approximate nearest-neighbour at scale; cosine similarity is
the standard metric for normalised text embeddings (direction matters, not magnitude).

**Why chunk with overlap?** Most webhook bodies fit one chunk, but large payloads are split so no
single embedding has to represent too much. The 200-char overlap keeps context that straddles a
boundary present in at least one chunk.

**Why is the embedding key server-side but the answer key per-user?** Embedding is an
infrastructure cost paid once per ingest, so it uses one server key (`Rag:EmbeddingApiKey`). The
answer is interactive and user-attributable, so it reuses each user's own provider key — the exact
same encrypted-at-rest plumbing as the AI-analysis feature (no second secret path).

**How is multi-tenant isolation guaranteed?** Every retrieval is `WHERE EndpointId == …` and gated
by `AccessGuard.RequireEndpointAccessAsync` (workspace membership) before any query runs. One
endpoint can never retrieve another's chunks.

**How does it avoid hallucination?** The prompt instructs the model to answer *only* from the
retrieved context and to say so when the context is insufficient; citations let a human verify.

---

## Likely interview questions

- *How would you scale ingestion?* Background job + incremental (embed only new requests) instead of
  full rebuild; the per-request delete-and-replace pattern already supports incremental.
- *How do you evaluate retrieval quality?* Golden Q→expected-source set, measure recall@k; tune
  chunk size/overlap and topK. (Not yet implemented — honest gap.)
- *Cost controls?* Batch embeddings (already 64/call), cap history (1000), cache identical queries,
  consider a cheaper/local embedding model.
- *Why not a managed vector DB (Pinecone/Qdrant)?* pgvector keeps it self-hosted (the project's
  whole premise) and Postgres-simple; a managed store is the move only at much larger scale.

---

## File map

| Concern | File |
|---|---|
| Embedding contract | `Application/Common/Interfaces/IEmbeddingService.cs` |
| RAG contract | `Application/Common/Interfaces/IRagService.cs` |
| DTOs | `Application/DTOs/Rag/RagDtos.cs` |
| Settings | `Application/Common/Settings/RagSettings.cs` |
| Vector entity | `Infrastructure/Rag/RequestChunk.cs` |
| Vector DbContext | `Infrastructure/Rag/RagDbContext.cs` (+ `RagDbContextFactory`, `Rag/Migrations/`) |
| Chunker | `Infrastructure/Rag/TextChunker.cs` |
| Embeddings (OpenAI) | `Infrastructure/Services/OpenAiEmbeddingService.cs` |
| Orchestration | `Infrastructure/Services/RagService.cs` |
| API | `API/Controllers/RagController.cs` |
| UI | `client/src/app/features/rag/rag-ask.component.*` ("Ask AI" tab) |
| Generation reuse | `IAiAnalysisService.CompleteAsync` in `Infrastructure/Services/AiAnalysisService.cs` |

## Tests

- `Unit/TextChunkerTests.cs` — chunk window/overlap/coverage (always runs).
- `Unit/OpenAiEmbeddingServiceTests.cs` — request shape, header auth, index-ordered parsing (always runs, no network).
- `Integration/RagVectorStoreTests.cs` — real pgvector cosine ranking + per-endpoint scoping (runs when Postgres is up; no paid API).
- `tests/rag-smoke.ps1` — full end-to-end against a running stack (also prints ingest/ask timings).

## Run it

```bash
docker compose -f docker-compose.rag.yml up -d
dotnet ef database update --context RagDbContext \
  --project src/WebhookForge.Infrastructure --startup-project src/WebhookForge.API
# set Rag:EmbeddingApiKey (OpenAI) + ConnectionStrings:RagVectorStore in appsettings.Development.json
dotnet run --project src/WebhookForge.API
# UI: open an endpoint → "Ask AI" tab → Re-index → ask
```
