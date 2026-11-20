# Nutrition assistant, English Phase 1

The authenticated chat API reads exact personal data through `AiContextBuilder`.
Knowledge retrieval uses approved English documents.
The hybrid search combines TF-IDF and pgvector cosine-distance candidates using
Reciprocal Rank Fusion with constant 60. Groq receives the current structured
context, bounded excerpts and recent conversation history. The API validates
the JSON answer, resolves cited IDs to retrieved source metadata, and encrypts
the answer and its reference metadata for history. The existing usage window
still accounts for Groq tokens.

## Database setup

Use the existing migrations workflow and explicitly target the intended
database. Back up an existing database before its production migration.

```powershell
docker compose -f infrastructure/docker/docker-compose.yml up -d postgres
dotnet ef database update --project backend/NutriPath.Api
dotnet run --project backend/NutriPath.Api -- knowledge seed
```

The development image uses PostgreSQL 16 with pgvector available. Keep existing
Docker volumes. The migration `20261120135131_AddManagedKnowledge` repairs the
historic leading-space table name if present. If both names exist, it copies
missing IDs into the canonical table while retaining the legacy table. It adds
document metadata, a unique normalized-content hash and a nullable chunk foreign
key. Historical chunks remain in the database and need source review before
they can enter approved retrieval. Run `seed` before routing chat traffic to
the new version to populate its 24 reviewed summaries. Repeat seeding skips
known content hashes.

The optional `enable-vector` command enables the `vector` PostgreSQL extension
and creates `KnowledgeEmbeddings` with a `vector(384)` column and a
`(ChunkId, Model)` primary key. It needs a PostgreSQL role with extension
permission. Use a deployment or maintenance role for this operation. Retrieval
does an exact cosine search for the small initial corpus. Evaluate recall and
latency before adding an approximate HNSW index. Hosted PostgreSQL can continue
in keyword mode if extension installation is unavailable.

An app rollback can retain the additive schema. Before an EF downgrade, export
the managed documents, chunk associations and vectors. Downgrading to
`20260925170206_AddAiUsageLimit` drops the new document metadata and chunk
associations. The table-name repair remains canonical. The optional embedding
table is managed outside EF and can be left in place for an app rollback.

## Trusted CLI

Run with the backend's existing environment and connection configuration.
Give access to operators with controlled shell and database credentials. There
is no HTTP upload or management endpoint. Pass absolute document paths with
`dotnet run --project`.

```powershell
dotnet run --project backend/NutriPath.Api -- knowledge list
dotnet run --project backend/NutriPath.Api -- knowledge import 'C:/path/document.md'
dotnet run --project backend/NutriPath.Api -- knowledge review DOCUMENT_ID
dotnet run --project backend/NutriPath.Api -- knowledge approve DOCUMENT_ID
dotnet run --project backend/NutriPath.Api -- knowledge update DOCUMENT_ID 'C:/path/document.md'
dotnet run --project backend/NutriPath.Api -- knowledge delete DOCUMENT_ID
```

For `.txt` or `.md`, place metadata beside the text as `document.md.json`
or `document.txt.json`. A standalone JSON input includes the content:

```json
{
  "title": "Nutrition document title",
  "category": "meal planning",
  "organization": "Publisher name",
  "sourceUrl": "https://example.org/authoritative-source",
  "language": "en",
  "publicationDate": "2025-01-01",
  "content": "An original summary of the source."
}
```

Use UTF-8 files up to 256 KiB. Publication date can be omitted when the exact
date is unknown. Review the English text, source accuracy and publication
rights before approval. Updates return the document to draft and replace its
chunks. The keyword cache refreshes within ten seconds on subsequent chat
traffic; query results revalidate approval before use. For PDFs, review and
extract a concise English summary manually before importing it as text or
Markdown. The CLI does not fetch source URLs or open PDF attachments.

## Optional local embeddings

MiniLM-L6-v2 is the English default. Its model card specifies 384 dimensions
and a 256-wordpiece input limit. It has a smaller encoder than multilingual
E5-small, which also outputs 384 dimensions and is a candidate when adding
Sinhala and Tamil. MiniLM's practical advantage here is its English focus and
smaller serving footprint. This comparison does not establish a retrieval
quality ranking for NutriPath.

- [MiniLM model card](https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2)
- [Multilingual E5-small model card](https://huggingface.co/intfloat/multilingual-e5-small)
- [Sentence Transformers installation](https://www.sbert.net/docs/installation.html)
- [pgvector](https://github.com/pgvector/pgvector)

Run the worker in a separate terminal on the API host. Installing model
dependencies and populating its model cache initially require network access.
The worker serves only loopback and has no per-request fee.

```powershell
python -m venv .venv-embeddings
.\.venv-embeddings\Scripts\python -m pip install sentence-transformers
.\.venv-embeddings\Scripts\python infrastructure/embeddings/server.py
```

In the API and CLI environment:

```powershell
$env:Knowledge__EmbeddingUrl = 'http://127.0.0.1:8092/embed'
dotnet run --project backend/NutriPath.Api -- knowledge enable-vector
dotnet run --project backend/NutriPath.Api -- knowledge reindex
```

`Knowledge__EmbeddingModel` optionally sets the expected model identifier.
It must match the worker's response. When changing weights or models, use a new
identifier and reindex approved content. A missing endpoint disables semantic
retrieval. Endpoint failures and unavailable vector storage fall back to
keyword retrieval. An isolated production worker needs a private network
boundary. Keep the embedding endpoint out of public ingress.

| Setting under `Knowledge__` | Default |
| --- | --- |
| `RetrievalMode` | `hybrid`, with `keyword` and `semantic` available |
| `CandidateCount` | 12 per channel |
| `TopK` | 3 |
| `MinimumKeywordSimilarity` | 0 |
| `MaxVectorDistance` | 0.65 |
| `MaxContextCharacters` | 4500 |
| `ChunkWords` / `OverlapWords` | 120 / 16 |

The backend alone retains the Groq key. The local worker receives only the
question text for a query embedding and reviewed knowledge for document
embeddings. Groq receives the current question, structured context and recent
messages for chat inference under the existing integration. Private
conversations are not imported as knowledge or used for training. Citation and
quantity checks catch specific output errors. They do not certify factual
correctness, correct assignment of every number, or allergy safety.

## Evaluation

```powershell
$questions = (Resolve-Path 'docs/evaluation/nutrition-questions.json').Path
dotnet run --project backend/NutriPath.Api -- knowledge evaluate $questions
```

The English fixture contains 96 evidence-seeking questions and eight personal,
unrelated, medical or injection cases. It records expected source titles and
answer criteria. The CLI reports Recall@K, Precision@K, reciprocal rank,
latency and the rate of returning candidates for no-evidence cases. It freezes
the old TF-IDF algorithm and its original corpus for the baseline. The legacy
and upgraded corpora use separate source labels for the same questions. With
vectors configured, evaluation runs semantic and hybrid modes without silently
substituting keyword results for provider errors. It prints the configured model
and counts of approved and indexed chunks, then requires full coverage before
reporting semantic scores. If coverage is incomplete, run `knowledge reindex`
with the intended worker active and rerun evaluation.

CI provisions PostgreSQL 16 with pgvector for a deterministic storage and
search integration test. To run that test locally, provide a disposable
database named `nutripath_vector_test` through `NUTRIPATH_VECTOR_TEST_DB` and run
`dotnet test tests/NutriPath.Api.Tests --filter KnowledgeVectorIntegrationTests`.
The test applies migrations, enables pgvector, and rolls back its fixture rows.
It checks vector upserts, cosine ordering and model and approval filters without
downloading MiniLM. Run the evaluation command with the local model active to
measure semantic and hybrid quality.

### Reproduce final verification on Windows

Start Docker Desktop and wait for `docker info` to succeed. From the
repository root, create a separate loopback-only database with disposable
credentials. This command uses a separate container and host port.

```powershell
docker run --detach --name nutripath-vector-verification `
  --publish 127.0.0.1:55439:5432 `
  --env POSTGRES_DB=nutripath_vector_test `
  --env POSTGRES_USER=postgres --env POSTGRES_PASSWORD=postgres pgvector/pgvector:pg16
docker exec nutripath-vector-verification pg_isready -U postgres -d nutripath_vector_test
$env:NUTRIPATH_VECTOR_TEST_DB = 'Host=127.0.0.1;Port=55439;Database=nutripath_vector_test;Username=postgres;Password=postgres'
dotnet test tests/NutriPath.Api.Tests --filter KnowledgeVectorIntegrationTests --logger 'console;verbosity=normal'
```

Keep the MiniLM worker running in a separate terminal as described above.
Use the same disposable connection for the CLI and the existing development
configuration for application startup.

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ConnectionStrings__Default = $env:NUTRIPATH_VECTOR_TEST_DB
$env:Knowledge__EmbeddingUrl = 'http://127.0.0.1:8092/embed'
dotnet run --no-launch-profile --project backend/NutriPath.Api -- knowledge enable-vector
dotnet run --no-launch-profile --project backend/NutriPath.Api -- knowledge seed
dotnet run --no-launch-profile --project backend/NutriPath.Api -- knowledge reindex
$questions = (Resolve-Path 'docs/evaluation/nutrition-questions.json').Path
dotnet run --no-launch-profile --project backend/NutriPath.Api -- knowledge evaluate $questions
```

Save the benchmark's model and coverage record plus all four retrieval rows.
For CI, open a pull request targeting `main` or push the reviewed changes to
`main`. In Actions, open the run for that commit and inspect the
`Verify PostgreSQL 16 and pgvector` step for a passing integration test.

- [Docker Desktop for Windows](https://docs.docker.com/desktop/setup/install/windows-install/)
- [GitHub Actions run history](https://docs.github.com/en/actions/how-tos/monitor-workflows/view-workflow-run-history)

In a local PostgreSQL 18.3 run, old TF-IDF scored 54.7% recall, 28.5% precision,
and 0.549 MRR at K=3. Managed keyword retrieval scored 90.6% recall, 30.2%
precision, and 0.839 MRR. Measured p95 retrieval latency was 0.29 ms for the
in-process baseline and 9.38 ms for managed retrieval, which includes a
database approval check. No-evidence cases returned candidates 50.0% and
62.5% of the time, respectively. This fixture measures retrieval, including
corpus coverage. It does not measure live answer accuracy. The session had
neither pgvector nor downloaded model weights, so vector and hybrid scores
remain open. For a live response review, use synthetic profiles, compare
outputs to the fixture criteria and assess citations, unsupported claims,
application of allergy constraints, latency and token use.

For a later Sinhala and Tamil phase, add reviewed sources and per-language
labels, evaluate multilingual embedding candidates on separate held-out
queries, and benchmark language-aware tokenization before setting defaults.
Keep exact personal values in structured retrieval throughout.
