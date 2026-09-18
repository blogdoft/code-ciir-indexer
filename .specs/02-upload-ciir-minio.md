# Upload de CIIR via MinIO e Worker de Processamento Assíncrono

## 1. Objetivo

Adicionar um segundo ponto de entrada de indexação ao `code-ciir-indexer`, complementar ao já
existente `POST /api/indexations` (path local).

O novo endpoint recebe o arquivo `ciir.jsonl` diretamente via HTTP, sabendo que ele pode chegar a
200 MB. Diferente do fluxo por path, aqui a aplicação não pode confiar que o processamento cabe
dentro do ciclo de vida de uma única requisição — a ingestão (receber e guardar o arquivo) e o
processamento (gerar embeddings, resolver relações, persistir) precisam ser desacoplados.

O arquivo enviado:

```text
upload HTTP (projectId + ciirFile)
   │
   ▼
bucket MinIO (pasta de nome aleatório)
   │
   ▼
registro "pendente" em tabela dedicada
   │
   ▼
Worker (BackgroundService) faz polling
   │
   ▼
reaproveita o motor de indexação existente (RunIndexation)
   │
   ▼
arquivo apagado do MinIO + registro "processado"
```

Este documento assume conhecimento de `01-Spec-inicial.md` (contrato CIIR, `projects`,
`indexing_runs`, `RunIndexation`, embeddings) e não repete o que já está definido lá.

---

## 2. Relação com a decisão anterior de não usar fila

`01-Spec-inicial.md` §63 lista "queue" como algo a não implementar. Essa decisão valia para o
fluxo por path local: um único processo, path já confiável no disco do servidor, sem necessidade
de sobreviver a um restart no meio do processamento.

O upload muda o cenário:

```text
arquivo grande (até 200 MB)
   +
cliente externo, rede não confiável
   +
não pode travar a aplicação
   =
ingestão e processamento precisam ser desacoplados
por um mecanismo durável, não por um canal em memória
```

O `IndexationChannel` (`Channel<Guid>` em memória) que já existe para o fluxo por path não
serve aqui: ele é perdido se o processo reiniciar no meio de um processamento. Por isso este
documento introduz uma fila **durável**, baseada em tabela PostgreSQL com `SELECT ... FOR UPDATE
SKIP LOCKED`, e não em uma abstração de fila genérica nem em infraestrutura de mensageria externa
(RabbitMQ, Kafka etc. continuam fora de escopo).

---

## 3. Escopo confirmado

```text
enviado nesta versão:
  ciir.jsonl (multipart/form-data)
  projectId (obrigatório — identifica um projeto já cadastrado)

NÃO enviado nesta versão:
  código-fonte do projeto

NÃO feito por este endpoint:
  criação ou atualização de projeto (diferente do fluxo por path, que faz
  create-or-update por nome via IProjectStore.EnsureProjectAsync)
```

O projeto referenciado por `projectId` precisa já existir (cadastrado previamente — hoje isso só
acontece como efeito colateral do fluxo por path `POST /api/indexations`, já que não existe um
endpoint dedicado de cadastro de projeto). Se não existir, o upload é rejeitado.

---

## 4. Contrato do endpoint de upload

```http
POST /api/ciir-uploads
Content-Type: multipart/form-data; boundary=...
```

Partes do multipart:

```text
projectId   (texto/número, obrigatório — id de um projeto já cadastrado)
ciirFile    (arquivo, obrigatório, extensão .jsonl, até o limite configurado)
```

Resposta (aceito):

```json
{
  "uploadId": "019...",
  "status": "pending"
}
```

Erros (mesma convenção de `Failure.Code` = `"{status}-{slug}"` já usada por
`IndexationsController`):

```text
400-project-id-required       projectId ausente, em branco ou não numérico
404-project-not-found         projectId não corresponde a nenhum projeto cadastrado
400-ciir-file-required        parte ciirFile ausente
400-invalid-extension         ciirFile sem extensão .jsonl
413-file-too-large            ciirFile maior que o limite configurado
429-too-many-uploads          limite de uploads simultâneos atingido
503-object-storage-unavailable  MinIO inacessível no momento do upload
```

A validação de `projectId` acontece **antes** de iniciar o streaming do arquivo para o MinIO —
não faz sentido receber 200 MB para só então descobrir que o projeto não existe. Isso implica
receber e validar as partes de texto do multipart (`projectId`) antes de começar a consumir a
parte de arquivo (`ciirFile`), o que é natural em uma leitura sequencial via `MultipartReader`
desde que o cliente envie `projectId` como a primeira parte do formulário — documentar essa
exigência de ordem no contrato do endpoint (§9 detalha o motivo técnico).

O endpoint só aceita e guarda o arquivo — nenhuma leitura/validação de conteúdo CIIR acontece
aqui. Isso é responsabilidade do `RunIndexation` já existente, reaproveitado pelo Worker (§9).

### 4.1. Endpoint de consulta de status

```http
GET /api/ciir-uploads/{id}
```

```json
{
  "id": "019...",
  "projectId": 42,
  "status": "processed",
  "createdAt": "2026-09-16T12:00:00Z",
  "processingStartedAt": "2026-09-16T12:01:00Z",
  "processedAt": "2026-09-16T12:04:32Z",
  "indexationId": "019...",
  "error": null
}
```

`404` (sem corpo) se o id não existir — mesmo padrão de `GET /api/indexations/{id}`.

`indexationId` só aparece depois que o Worker cria o `indexing_run` correspondente (pode ser
`null` enquanto o upload ainda está `pending`).

---

## 5. Armazenamento no MinIO

```text
bucket:  configurado (ex.: "ciir-uploads")
chave:   {pasta-aleatória}/ciir.jsonl
pasta:   Guid.NewGuid() em formato "N", gerada pelo servidor
```

A chave do objeto **nunca** é derivada do nome de arquivo enviado pelo cliente — sempre
`{guid}/ciir.jsonl`. Isso evita colisão entre uploads, path traversal na chave, e vazamento do
nome original do arquivo no armazenamento.

O envio do endpoint para o MinIO deve ser **streaming direto**, sem buffer completo em memória
nem cópia intermediária em disco:

```text
corpo da requisição HTTP
   │  (MultipartReader, sem model binding de IFormFile)
   ▼
parte "projectId" (lida e validada primeiro)
   │
   ▼
stream da parte "ciirFile"
   │  (contagem de bytes em tempo real, aborta se exceder o limite)
   ▼
IObjectStorage.UploadAsync (SDK MinIO)
```

Usar `MultipartReader` (`Microsoft.AspNetCore.WebUtilities`) manualmente em vez de
`[FromForm] IFormFile` — o binding automático de `IFormFile` pode materializar a parte inteira
antes do handler rodar, o que é exatamente o que este endpoint não pode fazer com um arquivo de
200 MB.

---

## 6. Modelo de dados — tabela `ciir_uploads`

Estrutura conceitual:

```sql
CREATE TABLE ciir_uploads (
    id                     uuid PRIMARY KEY,
    project_id             bigint NOT NULL REFERENCES projects (id),
    bucket                 text NOT NULL,
    object_key             text NOT NULL,
    status                 text NOT NULL DEFAULT 'pending',
    created_at             timestamptz NOT NULL DEFAULT now(),
    processing_started_at  timestamptz NULL,
    processed_at           timestamptz NULL,
    retry_count            integer NOT NULL DEFAULT 0,
    error                  text NULL,
    indexing_run_id        uuid NULL REFERENCES indexing_runs (id)
);

CREATE INDEX ix_ciir_uploads_status_created_at
    ON ciir_uploads (status, created_at);

CREATE INDEX ix_ciir_uploads_project_id
    ON ciir_uploads (project_id);
```

`project_id` é `NOT NULL` e referencia `projects(id)` diretamente — reforça no banco a regra de
que este fluxo nunca opera sobre um projeto inexistente. `indexing_run_id` é preenchido pelo
Worker assim que o `indexing_run` correspondente é criado — antes disso fica `NULL`. Ele é o elo
de rastreabilidade entre "o arquivo chegou" e "o resultado da indexação" (que já tem seu próprio
detalhamento — contadores, erro — em `indexing_runs`).

Migração a acrescentar em `Ciir.Indexer.Infrastructure.PostgreSql/Migrations/Migrations/`:
`M20260917000000_AddCiirUploads.cs` (FluentMigrator, mesmo padrão de
`M20260909010000_AddProjectIdentityFields.cs`).

---

## 7. Máquina de estados do upload

```text
pending
   │  Worker reivindica (claim)
   ▼
processing
   │
   ├── indexação concluída com sucesso ──► processed
   │
   ├── indexação terminou em falha/cancelamento ──► failed
   │
   └── Worker morre no meio do processamento
          │  (processing_started_at mais antigo que o timeout configurável)
          ▼
       reivindicado de novo (retry_count + 1)
          │
          ├── retry_count < MaxRetryCount ──► volta para processing
          │
          └── retry_count >= MaxRetryCount ──► failed (sem mais tentativas)
```

`processed` e `failed` são estados terminais — nenhum dos dois volta a ser reivindicado pelo
Worker. A diferença entre eles: `processed` é sucesso; `failed` é "não vamos tentar de novo",
seja porque a indexação terminou com erro determinístico (não adianta tentar de novo o mesmo
arquivo), seja porque o worker travou/morreu repetidamente ao processá-lo, seja porque o projeto
referenciado deixou de existir entre o upload e o processamento (§8, passo 4.b).

Essa distinção (`failed` como terceiro estado, além do `pending`/`processing`/`processado`
descritos originalmente) é uma decisão de robustez deste documento: sem ela, um arquivo CIIR
malformado que sempre faz `RunIndexation` terminar em `Failed` ficaria sendo reprocessado pelo
Worker para sempre. `MaxRetryCount` só existe para o caso de o processo do Worker morrer no meio
do processamento (crash, OOM) — nunca para re-tentar um erro de conteúdo, que é determinístico e
não muda ao tentar de novo.

Em ambos os estados terminais o objeto correspondente é apagado do MinIO — não há razão para
reter um arquivo de até 200 MB que já foi processado (com sucesso ou não) e não será reprocessado.

---

## 8. Worker de processamento (`BackgroundService`)

Mesma família de mecanismo já usada por `IndexationWorker` — um `BackgroundService` do
`Microsoft.Extensions.Hosting`, registrado via `AddHostedService<CiirUploadWorker>()`. A
diferença é a origem do trabalho: em vez de ler de um `Channel<Guid>` em memória, faz polling na
tabela `ciir_uploads`.

Ciclo (repete enquanto a aplicação estiver de pé):

```text
1. Marcar como "failed" (e apagar do MinIO) qualquer registro "processing" travado há mais
   de X minutos E que já esgotou as tentativas.

2. Reivindicar o registro mais antigo elegível:
   status = 'pending'
   OU (status = 'processing' E travado há mais de X minutos E ainda não esgotou as tentativas)

3. Se nada foi reivindicado: aguardar o intervalo configurado (padrão 1 minuto) e voltar ao
   passo 1.

4. Se um registro foi reivindicado:
   a. Baixar o objeto do MinIO para um diretório de staging local exclusivo deste upload
      (streaming disco-a-disco, sem carregar em memória).
   b. Buscar o projeto por upload.ProjectId (IProjectStore.GetByIdAsync — novo método, ver §9).
      Se não existir mais, marcar o upload como "failed" (projeto removido após o upload) e
      encerrar este ciclo sem chamar RunIndexation.
   c. Reaproveitar IProjectStore.EnsureProjectAsync(project.Name, project.GitUrl,
      project.GitRawUrl, embeddingModelAtual, ct) — mesma chamada que o fluxo por path já faz,
      usada aqui só para atualizar embedding_model/dimensions do projeto se o provedor
      configurado mudou desde o cadastro (spec 01 §55). Não cria projeto novo, pois o nome já
      existe.
   d. Criar um indexing_run apontando para o path de staging (reaproveita
      IIndexingRunStore.CreateAsync com project.Id).
   e. Vincular indexing_run_id ao registro de upload.
   f. Executar RunIndexation.ExecuteAsync — o mesmo motor já usado pelo fluxo por path, sem
      nenhuma duplicação de lógica de embeddings/relações.
   g. Apagar o arquivo de staging local (sempre, mesmo se a indexação falhar).
   h. Apagar o objeto do MinIO.
   i. Marcar o upload como "processed" (se o indexing_run terminou Completed) ou "failed" (caso
      contrário), com processed_at = agora.
   j. Voltar imediatamente ao passo 1, sem esperar — só espera quando não há nada a fazer.
```

A reivindicação (passo 2) precisa ser atômica mesmo que mais de uma instância do serviço rode ao
mesmo tempo (`SELECT ... FOR UPDATE SKIP LOCKED` em uma única instrução):

```sql
WITH proximo AS (
    SELECT id
    FROM ciir_uploads
    WHERE status = 'pending'
       OR (
            status = 'processing'
            AND processing_started_at < now() - (@StuckTimeoutMinutes || ' minutes')::interval
            AND retry_count < @MaxRetryCount
          )
    ORDER BY created_at ASC
    LIMIT 1
    FOR UPDATE SKIP LOCKED
)
UPDATE ciir_uploads u
SET status = 'processing',
    processing_started_at = now(),
    retry_count = CASE WHEN u.status = 'processing' THEN u.retry_count + 1 ELSE u.retry_count END
FROM proximo
WHERE u.id = proximo.id
RETURNING u.*;
```

E o passo 1 (falha permanente por esgotamento de tentativas):

```sql
UPDATE ciir_uploads
SET status = 'failed',
    processed_at = now(),
    error = 'Excedeu o número máximo de tentativas após ficar travado em processamento.'
WHERE status = 'processing'
  AND processing_started_at < now() - (@StuckTimeoutMinutes || ' minutes')::interval
  AND retry_count >= @MaxRetryCount
RETURNING id, bucket, object_key;
```

`RunIndexation.ExecuteAsync` nunca lança exceção (contrato já estabelecido em
`01-Spec-inicial.md`) — sempre deixa o `indexing_run` em um status terminal
(`Completed`/`Failed`/`Cancelled`). O passo 4.i só precisa ler esse status final; não precisa de
try/catch em torno da chamada em si, só em torno do download/staging/lookup de projeto (passos
4.a–4.c, que podem falhar por problemas de MinIO/disco/projeto removido antes mesmo de chegar ao
`RunIndexation`).

O download do MinIO gera um path **local, gerado pelo próprio servidor**
(`{StagingDirectory}/{uploadId:N}/ciir.jsonl`) — nunca derivado de entrada do cliente. Por isso
este path **não passa** por `InputPathResolver`/`AllowedInputRoots`: aquela validação existe para
um path que o cliente controla (o fluxo por path local); aqui o path é sempre construído pelo
próprio Worker.

---

## 9. Portas e adaptadores (arquitetura hexagonal)

`IProjectStore` (já existente) ganha um novo método, usado tanto pelo endpoint (validar
`projectId` antes de subir o arquivo) quanto pelo Worker (recuperar nome/git urls para o
`EnsureProjectAsync` do passo 8.c):

```csharp
public interface IProjectStore
{
    // ... EnsureProjectAsync já existente, sem mudanças ...

    Task<Project?> GetByIdAsync(long id, CancellationToken cancellationToken = default);
}
```

Novas portas em `Ciir.Indexer.Application/Ports/`:

```csharp
public interface IObjectStorage
{
    Task EnsureBucketExistsAsync(string bucket, CancellationToken ct = default);

    Task UploadAsync(
        string bucket, string objectKey, Stream content,
        long? contentLength, string contentType, CancellationToken ct = default);

    Task DownloadToFileAsync(
        string bucket, string objectKey, string destinationPath, CancellationToken ct = default);

    Task DeleteAsync(string bucket, string objectKey, CancellationToken ct = default);
}

public interface ICiirUploadStore
{
    Task<CiirUpload> CreateAsync(
        long projectId, string bucket, string objectKey, CancellationToken ct = default);

    Task<CiirUpload?> ClaimNextAsync(
        TimeSpan stuckProcessingTimeout, int maxRetryCount, CancellationToken ct = default);

    Task<IReadOnlyList<CiirUpload>> ReclaimExhaustedAsync(
        TimeSpan stuckProcessingTimeout, int maxRetryCount, CancellationToken ct = default);

    Task MarkIndexingRunAsync(Guid uploadId, Guid indexingRunId, CancellationToken ct = default);
    Task MarkProcessedAsync(Guid uploadId, CancellationToken ct = default);
    Task MarkFailedAsync(Guid uploadId, string error, CancellationToken ct = default);
    Task<CiirUpload?> GetAsync(Guid uploadId, CancellationToken ct = default);
}
```

Novos casos de uso em `Ciir.Indexer.Application/UseCases/`:

```text
SubmitCiirUpload        — endpoint POST: valida projectId (obrigatório, deve existir via
                           IProjectStore.GetByIdAsync), sobe o stream pro MinIO, cria o
                           registro "pending". Retorna Result<CiirUpload>. Nunca cria/atualiza
                           projeto.

ProcessNextCiirUpload   — chamado pelo Worker a cada ciclo: executa os passos 1, 2 e 4
                           descritos em §8. Retorna se processou algo ou não (para o Worker
                           decidir se espera o intervalo configurado ou tenta de novo já).
```

Novo projeto de adaptador (mesmo padrão de um projeto por provedor de embedding):
`Ciir.Indexer.Infrastructure.ObjectStorage.Minio/`

```text
MinioOptions.cs                  — Endpoint, AccessKey, SecretKey, UseSsl, BucketName
MinioObjectStorage.cs            — implementa IObjectStorage via SDK oficial "Minio" (NuGet)
ObjectStorageUnavailableException.cs   — mesma ideia de DatabaseUnavailableException
ServiceCollectionExtensions.cs   — AddMinioObjectStorage(MinioOptions)
```

`Ciir.Indexer.Infrastructure.PostgreSql/` ganha:

```text
CiirUploadStore.cs   — implementa ICiirUploadStore, Dapper puro, mesmo estilo de
                        IndexingRunStore.cs (PostgreSqlConnections.ExecuteAsync, sem
                        BlogDoFT.Libs.DapperUtils — este repo não usa esse pacote apesar de
                        referenciado; manter consistência com o que já existe, não introduzir
                        um padrão novo isolado)
```

`ProjectStore.cs` (já existente) ganha a implementação de `GetByIdAsync`.

`Ciir.Indexer.Core/` ganha:

```text
CiirUploadStatus.cs            — enum Pending, Processing, Processed, Failed
CiirUploadStatusExtensions.cs  — ToWireString()/Parse(), mesmo padrão de IndexingStatusExtensions
CiirUpload.cs                  — record de domínio (Id, ProjectId, Bucket, ObjectKey, Status,
                                  CreatedAt, ProcessingStartedAt?, ProcessedAt?, RetryCount,
                                  Error?, IndexingRunId?)
```

`Ciir.Indexer.Api/` ganha:

```text
Controllers/CiirUploadsController.cs   — POST (streaming multipart) + GET status
Contracts/SubmitCiirUploadResponse.cs
Contracts/CiirUploadStatusResponse.cs
Uploads/CiirUploadWorker.cs            — BackgroundService, mesmo formato de IndexationWorker.cs
```

`Program.cs` ganha: bind de `MinioOptions`/`UploadOptions` (padrão já usado —
`?? throw new InvalidOperationException(...)` para seção obrigatória), chamada a
`AddMinioObjectStorage(...)`, `EnsureBucketExistsAsync` como fail-fast eager na inicialização
(mesmo espírito de `app.Services.GetRequiredService<IEmbeddingGenerator>()`), registro de
`AddHostedService<CiirUploadWorker>()`, e a policy de rate limiting do §11.

O endpoint `POST /api/indexations` (path local) permanece exatamente como está — este documento
só adiciona um segundo ponto de entrada, não substitui o existente.

---

## 10. Configuração

```json
{
  "Minio": {
    "Endpoint": "minio.internal:9000",
    "AccessKey": "***",
    "SecretKey": "***",
    "UseSsl": true,
    "BucketName": "ciir-uploads"
  },
  "Uploads": {
    "MaxCiirFileSizeBytes": 209715200,
    "StuckProcessingTimeoutMinutes": 30,
    "PollingIntervalSeconds": 60,
    "MaxRetryCount": 3,
    "StagingDirectory": "/var/ciir-indexer/staging",
    "MaxConcurrentUploads": 3
  }
}
```

`209715200` = 200 MiB — o teto pedido, configurável para operadores ajustarem por ambiente.
`AccessKey`/`SecretKey` chegam por variável de ambiente/secret (`Minio__AccessKey` etc.), nunca
hardcoded — mesmo padrão já usado hoje para `ConnectionStrings__Database`.

Ambas as seções são obrigatórias (`Get<T>() ?? throw new InvalidOperationException(...)`), assim
como `EmbeddingOptions`/`IndexerPathOptions` hoje.

`docker-compose.yml` (`.eng/docker/docker-compose.yml`) ganha um serviço `minio` (imagem oficial
`minio/minio`) para desenvolvimento local — hoje não existe nenhum serviço MinIO no repositório
nem em nenhum repositório irmão da organização; este é o primeiro.

---

## 11. Segurança

```text
projectId obrigatório e validado contra a tabela projects antes de aceitar o arquivo
  — este endpoint nunca cria/atualiza projeto, só referencia um já existente

tamanho máximo de arquivo aplicado via IHttpMaxRequestBodySizeFeature, configurável
  (não hardcoded em atributo de compilação) — rejeita com 413 antes de terminar de ler
  um arquivo maior que o limite, contando bytes durante o streaming

extensão do arquivo (.jsonl) validada a partir do nome da parte multipart antes de
  iniciar o upload pro MinIO

chave do objeto sempre gerada pelo servidor (guid), nunca a partir do nome de
  arquivo enviado pelo cliente

path de staging local sempre gerado pelo servidor a partir do id do upload —
  nunca a partir de entrada do cliente

limite de uploads simultâneos via Microsoft.AspNetCore.RateLimiting (ConcurrencyLimiter),
  aplicado só a este endpoint — além do limite, responde 429 em vez de aceitar
  indefinidamente e arriscar exaurir memória/conexões

credenciais do MinIO só via configuração/secret, nunca hardcoded

TLS ao MinIO configurável (UseSsl)

EnsureBucketExistsAsync roda no startup (fail-fast) — credenciais/bucket inválidos
  derrubam a aplicação na inicialização, não silenciosamente no primeiro upload

nenhuma extração/execução do conteúdo enviado além do parser CIIR já existente
  (RunIndexation) — este documento não introduz nenhum parsing novo
```

---

## 12. Performance

```text
upload: stream direto do corpo HTTP pro MinIO via MultipartReader — nunca materializa
  o arquivo inteiro em memória nem em um arquivo temporário intermediário

download (Worker): stream direto do MinIO pro disco de staging — mesma lógica

RunIndexation reaproveitado processa o JSONL de staging exatamente como processa
  hoje um path local — já é streaming/bounded por spec (01-Spec-inicial.md §61),
  nenhuma lógica nova de leitura precisa ser criada

ingestão (endpoint) é barata e rápida (validar projectId + stream pro MinIO + um INSERT) —
  pode aceitar uploads mesmo enquanto o Worker está processando um backlog grande, porque
  as duas etapas estão desacopladas pela tabela

Worker processa um upload por vez, serializado — mesmo modelo do IndexationWorker
  já existente; SKIP LOCKED existe para permitir múltiplas instâncias no futuro
  sem duplicar trabalho, não para paralelizar dentro de uma única instância
```

---

## 13. Testes obrigatórios

```text
POST aceita um ciir.jsonl dentro do limite, com projectId de um projeto existente,
  e retorna 202 com status "pending"

POST rejeita projectId ausente/em branco/não numérico (400)

POST rejeita projectId que não corresponde a nenhum projeto cadastrado (404)

POST rejeita arquivo sem a parte ciirFile (400)

POST rejeita extensão diferente de .jsonl (400)

POST rejeita arquivo maior que o limite configurado sem terminar de
  receber o corpo inteiro (413)

POST responde 429 acima do limite de uploads simultâneos configurado

GET retorna 404 para id inexistente

GET reflete status/timestamps corretos em cada transição

claim (SKIP LOCKED) nunca reivindica o mesmo registro em duas execuções concorrentes

registro "processing" travado há mais tempo que o timeout é reivindicado de novo,
  com retry_count incrementado

registro que esgota MaxRetryCount é marcado "failed" e não é mais reivindicado

indexação bem-sucedida marca o upload "processed", apaga o objeto do MinIO e
  vincula indexing_run_id

indexação que termina em Failed/Cancelled marca o upload "failed" (sem retry) e
  apaga o objeto do MinIO

projeto removido entre o upload e o processamento marca o upload "failed" sem
  chamar RunIndexation

Worker aguarda o intervalo configurado quando não há nada pendente, e não aguarda
  entre dois registros pendentes consecutivos

arquivo de staging local é sempre removido, mesmo quando a indexação falha
```

Testes de integração devem usar PostgreSQL real (já é o padrão do repositório,
`Testcontainers.PostgreSql`) e MinIO real via `Testcontainers.Minio` — não substituir por
fakes/mocks na camada do adaptador, mesmo princípio já aplicado ao Postgres em
`01-Spec-inicial.md` §60. Testes de caso de uso (`ProcessNextCiirUpload`, `SubmitCiirUpload`)
continuam usando substitutos das portas (`NSubstitute`), como já é o padrão em
`StartIndexationTests`/`RunIndexationTests`.

---

## 14. Não implementar agora

```text
upload do código-fonte do projeto
criação/atualização de projeto por este endpoint (projeto precisa já existir)
extração de arquivos compactados
mensageria externa (RabbitMQ, Kafka, etc.)
múltiplos buckets por projeto
retry ilimitado
processamento paralelo de múltiplos uploads dentro da mesma instância
antivírus/malware scanning do conteúdo enviado
autenticação/autorização do endpoint (fora de escopo deste documento — mesmo nível
  de exposição que o endpoint POST /api/indexations já tem hoje)
```

---

## 15. Critérios de aceite

### Upload

Um `ciir.jsonl` de até o tamanho configurado pode ser enviado via `POST /api/ciir-uploads`,
informando o `projectId` de um projeto já cadastrado, e recebe um `uploadId` com status
`pending`.

### Validação de projeto

Um `projectId` ausente retorna 400; um `projectId` que não existe retorna 404 — em nenhum dos
dois casos o arquivo é armazenado no MinIO.

### Armazenamento

O arquivo fica no bucket configurado, sob uma chave `{pasta-aleatória}/ciir.jsonl`, até ser
processado.

### Processamento

O Worker reivindica o registro pendente mais antigo, executa a indexação reaproveitando
`RunIndexation` para o `projectId` do upload, e o resultado (contadores, erro) é o mesmo que o
fluxo por path já produziria para o mesmo conteúdo e projeto.

### Limpeza

Ao final do processamento (sucesso ou falha), o objeto correspondente não existe mais no MinIO, e
o arquivo de staging local não existe mais em disco.

### Resiliência

Um registro que fica "processing" além do timeout configurado é reivindicado de novo, até o
limite de tentativas; depois disso fica `failed` permanentemente.

### Sem impacto no fluxo existente

`POST /api/indexations` (path local) continua funcionando exatamente como antes.

### Qualidade

Build sem warnings, testes cobrindo os casos do §13, nenhuma duplicação da lógica de
embeddings/relações fora de `RunIndexation`.
