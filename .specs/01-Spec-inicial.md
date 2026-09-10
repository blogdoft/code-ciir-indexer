# CIIR PostgreSQL Vector Indexer

## 1. Objetivo

Criar uma aplicação .NET responsável exclusivamente por importar arquivos CIIR no formato JSONL para PostgreSQL com pgvector.

A aplicação será denominada conceitualmente:

```text
CIIR Indexer
```

Sua responsabilidade termina na persistência estruturada dos dados e na geração dos embeddings.

O sistema NÃO será responsável por:

```text
pesquisa semântica
RAG
graph expansion
reranking
LLM
geração de respostas
análise de código-fonte
geração de CIIR
interpretação de regras de negócio
```

Essas funcionalidades serão implementadas por componentes posteriores.

O resultado desta aplicação deverá fornecer uma base adequada para que outros sistemas possam realizar:

```text
vector similarity search
hybrid search
dependency graph traversal
call graph traversal
graph expansion
impact analysis
code navigation
RAG
```

---

# 2. Entrada

A aplicação será uma REST API.

O endpoint principal deverá receber o caminho de um arquivo CIIR JSONL acessível pelo servidor,
junto com a identidade do projeto ao qual essa importação pertence (ver "Atualização — Identidade
de projeto informada pelo chamador").

Exemplo conceitual:

```http
POST /api/indexations
Content-Type: application/json
```

```json
{
  "projectName": "MyRepo.Api",
  "path": "/data/ciir/ciir.jsonl",
  "gitUrl": "https://github.com/org/myrepo",
  "gitRawUrl": "https://raw.githubusercontent.com/org/myrepo"
}
```

`projectName` é obrigatório; `gitUrl`/`gitRawUrl` são opcionais.

Resposta:

```json
{
  "indexationId": "019...",
  "status": "accepted"
}
```

O processamento deverá ocorrer através de um caso de uso desacoplado da camada HTTP.

A API é apenas um entry point.

Nenhuma regra de indexação poderá existir nos controllers/endpoints.

Arquitetura conceitual:

```text
REST API
   │
   ▼
Application
   │
   ├── CIIR Document Import
   │
   ├── Relation Import
   │
   ├── Embedding Generation
   │
   └── Relation Resolution
   │
   ▼
Infrastructure
   │
   ├── PostgreSQL
   ├── pgvector
   ├── filesystem
   └── embedding provider
```

---

# 3. Contrato CIIR

O arquivo deverá seguir o JSON Schema CIIR fornecido ao projeto.

Cada linha do JSONL representa exatamente uma entidade CIIR.

O indexador NÃO deverá modificar a semântica definida pelo CIIR.

Em particular:

```text
CIIR id
```

é a identidade determinística do elemento.

Ela deverá ser preservada integralmente.

Não gerar um novo identificador semântico para:

```text
method
type
property
field
event
namespace
project
etc.
```

O banco poderá possuir uma chave surrogate interna, mas ela será apenas uma implementação de persistência.

Exemplo:

```text
ciir_documents.id          = bigint interno
ciir_documents.ciir_id     = "sha256:..."
```

`ciir_id` representa a identidade real do nó.

---

# 4. Princípio fundamental do processamento

A indexação deverá executar três estágios lógicos:

```text
                 ┌─────────────────────────┐
                 │      ciir.jsonl         │
                 └────────────┬────────────┘
                              │
              ┌───────────────┴───────────────┐
              ▼                               ▼

      PROCESSO 1                       PROCESSO 2
   Document Import                  Relation Import
         +                                │
 Embedding Generation                    │
         │                                │
         ▼                                ▼
 ciir_documents                     ciir_relations
              │                       │
              └──────────┬────────────┘
                         ▼
                 PROCESSO 3
              Relation Resolution
                         │
                         ▼
             preencher source_id
                 e target_id
```

Os processos 1 e 2 deverão ser independentes.

Eles poderão executar concorrentemente.

O processo 2 NÃO poderá depender de as entidades já estarem inseridas em `ciir_documents`.

O arquivo JSONL poderá, portanto, ser lido duas vezes.

Essa é uma decisão intencional.

Não manter todo o conteúdo do arquivo em memória apenas para evitar uma segunda leitura.

O projeto deverá priorizar:

```text
streaming
baixo consumo de memória
bounded batches
```

---

# 5. Primeira leitura — importação dos documentos

A resolução do projeto acontece uma única vez por execução, ANTES de iniciar a leitura do JSONL —
a partir do `projectName` informado no request (ver "Atualização — Identidade de projeto informada
pelo chamador"), não por registro.

O primeiro processo deverá ler o JSONL sequencialmente.

Para cada registro:

```text
JSON line
   │
   ▼
Deserialize
   │
   ▼
Validate
   │
   ▼
Compare existing document
   │
   ├── new ───────────────► generate embedding
   │
   ├── hash changed ──────► generate embedding
   │
   └── hash unchanged ────► reuse existing embedding
   │
   ▼
UPSERT ciir_documents (project_id já resolvido)
```

Nunca carregar todo o JSONL em memória.

---

# 6. Embedding

O campo utilizado para geração do vetor será exclusivamente:

```text
embeddingText
```

Não construir novamente o texto a partir de:

```text
documentation
relations
conditions
symbol
source
```

O CIIR já realizou essa projeção semântica.

Portanto:

```text
CIIR
    │
    └── embeddingText
             │
             ▼
      Embedding Provider
             │
             ▼
          vector
```

---

# 7. Hash do embedding

Persistir:

```text
embedding_text_hash
```

Esse valor deverá preferencialmente utilizar o próprio:

```text
embeddingTextHash
```

fornecido pelo CIIR.

Como mecanismo defensivo, a aplicação poderá recalcular SHA-256 sobre o `embeddingText` recebido e validar se o valor corresponde ao informado pelo CIIR.

Em caso de divergência, a indexação deverá falhar para aquele registro ou para a execução, conforme política configurada.

Nunca considerar silenciosamente um hash inconsistente como válido.

---

# 8. Indexação incremental

A chave lógica de um documento será:

```text
project + ciir_id
```

Para um registro existente:

```text
existing.embedding_text_hash
            =
incoming.embedding_text_hash
```

significa:

```text
não gerar embedding novamente
```

O vetor existente deverá ser preservado.

Quando:

```text
existing.embedding_text_hash
            !=
incoming.embedding_text_hash
```

o embedding deverá ser recalculado.

Fluxo:

```text
CIIR id existe?
      │
   ┌──┴──┐
   │     │
  não   sim
   │     │
   ▼     ▼
embed   hash igual?
           │
        ┌──┴──┐
       sim   não
        │     │
        ▼     ▼
      reuse  embed
```

IMPORTANTE:

`embeddingTextHash` controla apenas o vetor.

Ele NÃO deverá impedir reprocessamento de metadados ou relações.

---

# 9. Modelo de embedding por projeto

A tabela de projetos deverá registrar qual modelo de embedding foi utilizado.

Conceitualmente:

```text
projects
----------------------------
id
name
git_url
git_raw_url
embedding_model
embedding_dimensions
created_at
updated_at
```

`git_url`/`git_raw_url` são opcionais e vêm do request de `POST /api/indexations` (ver "Atualização
— Identidade de projeto informada pelo chamador").

Exemplo:

```text
Payments.Application
bge-m3
1024
```

Essa informação é importante porque um vetor somente pode ser interpretado corretamente em conjunto com o modelo que o produziu.

Embeddings produzidos por modelos diferentes não deverão ser comparados como se pertencessem ao mesmo espaço vetorial.

---

# 10. Restrição de dimensionalidade

O pgvector deverá utilizar dimensionalidade explícita:

```sql
vector(<dimensions>)
```

A dimensão deverá ser definida pela configuração da implantação.

Exemplo conceitual:

```text
EmbeddingDimensions = 1024
```

gera uma coluna equivalente a:

```sql
embedding vector(1024)
```

A aplicação deverá validar que o embedding retornado pelo provider possui exatamente a dimensão configurada.

A v1 deverá assumir uma única dimensionalidade de embedding por instalação do indexador.

Isso mantém:

```text
schema simples
índice ANN simples
queries simples
operabilidade previsível
```

Suporte simultâneo a modelos com dimensionalidades diferentes deverá ser considerado evolução futura.

---

# 11. Tabela `projects`

Estrutura conceitual:

```sql
projects
(
    id                    bigint PK,
    name                  text NOT NULL,
    git_url               text,
    git_raw_url           text,
    embedding_model       text NOT NULL,
    embedding_dimensions  integer NOT NULL,
    created_at            timestamptz NOT NULL,
    updated_at            timestamptz NOT NULL
)
```

Criar restrição adequada para identidade do projeto.

Na v1:

```text
project.name
```

poderá ser considerado a identidade lógica do projeto — informada pelo chamador de
`POST /api/indexations`, nunca derivada de um registro CIIR individual (ver "Atualização —
Identidade de projeto informada pelo chamador").

`git_url`/`git_raw_url` são a primeira extensão prevista abaixo (`repository`/`project_path`), já
implementada como campos opcionais.

A arquitetura deverá permitir introduzir futuramente:

```text
branch
commit
project_external_id
```

sem alterar a identidade CIIR já persistida.

---

# 12. Tabela `ciir_documents`

A tabela representa os nós do futuro grafo e os documentos pesquisáveis semanticamente.

Estrutura conceitual:

```sql
ciir_documents
(
    id                       bigint PK,

    project_id               bigint NOT NULL FK projects,

    ciir_id                  text NOT NULL,

    schema_version           text NOT NULL,
    kind                     text NOT NULL,
    language                 text NOT NULL,

    symbol_name              text,
    symbol_qualified_name    text,
    symbol_canonical_name    text,
    symbol_container         text,

    source_path              text,

    embedding_text           text,
    embedding_text_strategy  text,
    embedding_text_hash      text,

    embedding                vector(<dimensions>),

    content                  jsonb NOT NULL,

    last_seen_run_id         uuid,

    created_at               timestamptz NOT NULL,
    updated_at               timestamptz NOT NULL
)
```

Criar:

```text
UNIQUE(project_id, ciir_id)
```

O campo:

```text
content jsonb
```

deverá guardar o registro CIIR completo.

Isso é importante porque evita perder informações do contrato como:

```text
documentation
comments
conditions
controlFlow
type metadata
method metadata
extensions
source locations
```

Não transformar todas as propriedades CIIR em colunas relacionais sem uma necessidade concreta de pesquisa.

Extrair para colunas somente dados relevantes para:

```text
identidade
filtragem
join
indexação
pesquisa
observabilidade
```

---

# 13. Por que armazenar `content jsonb`

O PostgreSQL não deverá ser apenas uma cópia normalizada de todas as propriedades do CIIR.

O CIIR é um contrato evolutivo.

Armazenar o documento original permite:

```text
CIIR v1.0
CIIR v1.1
CIIR v1.x
```

sem exigir imediatamente novas colunas para cada propriedade adicionada ao contrato.

A estratégia deverá ser:

```text
dados usados frequentemente
        ↓
columns

documento completo
        ↓
jsonb
```

---

# 14. Índice vetorial

Criar índice pgvector adequado para cosine similarity.

Preferência inicial:

```text
HNSW
```

Conceitualmente:

```sql
CREATE INDEX ...
ON ciir_documents
USING hnsw (embedding vector_cosine_ops);
```

O índice deverá considerar apenas documentos com:

```text
embedding IS NOT NULL
```

quando tecnicamente apropriado.

A implementação futura da pesquisa poderá fazer:

```text
query embedding
       │
       ▼
pgvector
       │
       ▼
nearest CIIR nodes
```

---

# 15. Segunda leitura — relações

O segundo processo deverá abrir o mesmo JSONL novamente.

Para cada documento:

```text
document.id
    │
    └── source CIIR id

document.relations[]
    │
    ├── kind
    ├── target.id
    ├── target.symbol
    ├── resolution.status
    ├── resolution.origin
    ├── resolution.reason
    └── location
```

Cada relação deverá produzir um registro independente em:

```text
ciir_relations
```

---

# 16. Tabela `ciir_relations`

Estrutura conceitual:

```sql
ciir_relations
(
    id                       bigint PK,

    project_id               bigint NOT NULL FK projects,

    source_ciir_id           text NOT NULL,
    target_ciir_id           text,

    source_document_id       bigint NULL FK ciir_documents,
    target_document_id       bigint NULL FK ciir_documents,

    kind                     text NOT NULL,

    target_symbol            text NOT NULL,

    resolution_status        text NOT NULL,
    resolution_origin        text NOT NULL,
    resolution_reason        text,

    source_path              text,
    start_line               integer,
    start_column             integer,
    end_line                 integer,
    end_column               integer,

    last_seen_run_id         uuid,

    created_at               timestamptz NOT NULL,
    updated_at               timestamptz NOT NULL
)
```

---

# 17. Dupla representação da identidade nas relações

Uma relação deverá possuir duas formas de identificação.

## Identidade CIIR

```text
source_ciir_id
target_ciir_id
```

São valores vindos diretamente do artifact.

## Identidade relacional

```text
source_document_id
target_document_id
```

São foreign keys para:

```text
ciir_documents.id
```

Exemplo:

```text
source_ciir_id
sha256:AAA...

target_ciir_id
sha256:BBB...

             depois da resolução

source_document_id = 1823
target_document_id = 9281
```

Essa redundância é proposital.

Ela permite importar relações antes de os documentos estarem disponíveis.

---

# 18. Foreign keys nullable

As duas foreign keys deverão ser nullable:

```text
source_document_id NULL
target_document_id NULL
```

Mesmo que normalmente o source possa ser resolvido imediatamente, o importer NÃO deverá depender disso.

Durante a primeira etapa da relação:

```text
source_document_id = NULL
target_document_id = NULL
```

Depois:

```text
relation resolver
        │
        ├── resolve source_ciir_id
        │
        └── resolve target_ciir_id
```

---

# 19. Relações externas

O CIIR permite relações cujo alvo não possui entidade interna.

Exemplo:

```text
System.String.IsNullOrEmpty
```

Nesses casos poderá existir:

```text
target_symbol
```

mas não:

```text
target_ciir_id
```

Portanto:

```text
target_document_id
```

permanecerá `NULL`.

Isso NÃO representa erro.

Deverá ser preservada a informação do CIIR:

```text
resolution_status = external
resolution_origin = framework
```

---

# 20. Resolução das foreign keys

Depois que:

```text
Document Import
```

e:

```text
Relation Import
```

forem concluídos com sucesso, executar o processo:

```text
Relation Resolution
```

Primeiro source:

```sql
UPDATE ciir_relations r
SET source_document_id = d.id
FROM ciir_documents d
WHERE ...
```

resolvendo:

```text
r.source_ciir_id = d.ciir_id
```

e considerando o projeto quando necessário.

Depois target:

```sql
UPDATE ciir_relations r
SET target_document_id = d.id
FROM ciir_documents d
WHERE ...
```

baseado em:

```text
r.target_ciir_id = d.ciir_id
```

Não executar milhares de queries individuais:

```text
SELECT ... WHERE ciir_id = ...
```

por relação.

A resolução deverá ser feita através de operações SQL set-based.

---

# 21. Grafo resultante

Depois da resolução teremos conceitualmente:

```text
ciir_documents

    10 PaymentService.Authorize()
    11 IPaymentGateway.Authorize()
    12 Order.Total
```

e:

```text
ciir_relations

source | kind  | target
-------|-------|-------
10     | calls | 11
10     | reads | 12
```

Isso forma diretamente:

```text
PaymentService.Authorize
        │
        ├── CALLS ──► IPaymentGateway.Authorize
        │
        └── READS ──► Order.Total
```

Não será necessário um graph database para reconstruir esse grafo.

PostgreSQL poderá representar as arestas através das duas foreign keys.

---

# 22. Relações suportadas

O indexador NÃO deverá possuir regras específicas para cada relação além das necessárias para validação.

Inicialmente deverão ser preservadas pelo menos:

```text
contains
inherits
implements
overrides
calls
constructs
reads
writes
throws
catches
```

O valor deverá vir diretamente do CIIR.

---

# 23. Índices para relações

Criar pelo menos:

```text
(project_id)
(source_ciir_id)
(target_ciir_id)
(source_document_id)
(target_document_id)
(kind)
```

Índices compostos importantes:

```text
(source_document_id, kind)
(target_document_id, kind)
```

Esses índices permitirão operações futuras como:

```text
quem este método chama?
```

```sql
WHERE source_document_id = ?
AND kind = 'calls'
```

e:

```text
quem chama este método?
```

```sql
WHERE target_document_id = ?
AND kind = 'calls'
```

Mesmo que o CIIR armazene somente:

```text
A CALLS B
```

a consulta inversa poderá descobrir:

```text
B CALLED_BY A
```

sem persistir uma segunda relação.

---

# 24. Não duplicar relações inversas

Nunca transformar automaticamente:

```text
A CALLS B
```

em:

```text
A CALLS B
B CALLED_BY A
```

A aresta deverá existir uma única vez.

Consultas futuras podem atravessá-la nos dois sentidos.

---

# 25. Identidade de uma relação

Relações também precisam ser idempotentes.

Definir uma chave determinística de persistência baseada em algo equivalente a:

```text
project
source_ciir_id
kind
target_ciir_id
target_symbol
location
```

A localização é importante porque o mesmo método pode chamar o mesmo alvo várias vezes.

Exemplo:

```csharp
gateway.Send();
gateway.Send();
```

O sistema precisa decidir explicitamente se isso representa:

```text
1 aresta agregada
```

ou:

```text
2 ocorrências da relação
```

Para a v1, preservar as ocorrências individuais quando `location` estiver disponível.

Isso mantém fidelidade ao CIIR.

---

# 26. Runs de indexação

Criar uma entidade operacional:

```text
indexing_runs
```

Estrutura conceitual:

```text
id
path
status
started_at
finished_at
documents_processed
documents_inserted
documents_updated
embeddings_generated
embeddings_reused
relations_processed
relations_resolved
relations_unresolved
error
```

Status possíveis:

```text
pending
running
resolving_relations
completed
failed
cancelled
```

Essa entidade é operacional e não faz parte do CIIR.

---

# 27. `last_seen_run_id`

Tanto documentos quanto relações deverão registrar:

```text
last_seen_run_id
```

Isso resolve um problema importante da indexação incremental.

Considere que a primeira CIIR contém:

```text
A
B
C
```

A segunda contém:

```text
A
B
```

Sem controle de execução, `C` permaneceria para sempre no banco.

Durante uma execução:

```text
A -> last_seen_run = X
B -> last_seen_run = X
```

Ao final bem-sucedido:

```text
project documents
WHERE last_seen_run != X
```

representam entidades removidas da nova CIIR.

Somente depois da conclusão bem-sucedida da importação essas entidades poderão ser removidas.

Nunca remover registros antigos durante uma execução que posteriormente falhe.

---

# 28. Relações obsoletas

A mesma estratégia vale para relações.

Considere:

```text
run 1

A CALLS B
A CALLS C
```

e depois:

```text
run 2

A CALLS B
```

A relação:

```text
A CALLS C
```

precisa desaparecer.

Portanto relações também deverão utilizar:

```text
last_seen_run_id
```

e serem removidas somente depois do sucesso da nova execução.

---

# 29. Ordem de finalização

A execução deverá seguir:

```text
1. criar indexing_run

2. iniciar Document Import
3. iniciar Relation Import

4. aguardar ambos

5. Relation Resolution

6. validar resultados

7. remover relações obsoletas

8. remover documentos obsoletos

9. marcar indexing_run como completed
```

A ordem de exclusão deverá respeitar FKs:

```text
relations
   ↓
documents
```

---

# 30. Transações

Não abrir uma única transação envolvendo milhões de registros e todo o arquivo.

Isso poderá gerar:

```text
locks longos
WAL excessivo
uso excessivo de memória
rollback caro
problemas operacionais
```

Utilizar transações pequenas por batch.

Por exemplo:

```text
500
1000
2000
```

registros.

O tamanho deverá ser configurável.

A atomicidade lógica da execução será controlada por:

```text
indexing_runs
last_seen_run_id
cleanup somente no sucesso
```

---

# 31. Batch processing

Evitar:

```text
INSERT individual
```

para grandes arquivos sempre que possível.

A infraestrutura PostgreSQL deverá ser preparada para utilizar operações em lote.

Preferir:

```text
COPY
```

ou:

```text
batched INSERT/UPSERT
```

quando compatível com a necessidade de incrementalidade.

A implementação deve abstrair a estratégia de persistência da camada Application.

---

# 32. Geração de embeddings em batch

O provider deverá permitir, quando suportado:

```text
GenerateEmbeddingsAsync(
    IReadOnlyCollection<string> texts)
```

em vez de obrigatoriamente:

```text
1 chamada HTTP por documento
```

O tamanho do batch será configurável.

Exemplo:

```text
EmbeddingBatchSize = 32
```

Fluxo:

```text
JSONL
  │
  ▼
documents requiring embeddings
  │
  ▼
bounded batch
  │
  ▼
embedding provider
  │
  ▼
vectors
  │
  ▼
database batch
```

---

# 33. Backpressure

O sistema deverá utilizar buffers limitados.

Não permitir:

```text
filesystem rápido
      +
embedding lento
      =
milhões de objetos esperando em memória
```

Utilizar mecanismos como:

```text
Channel<T>
```

com capacidade limitada, ou solução equivalente.

A escolha concreta fica para implementação.

---

# 34. Embedding provider

Criar uma fronteira arquitetural para geração de embeddings.

Exemplo conceitual:

```csharp
public interface IEmbeddingGenerator
{
    string Model { get; }

    int Dimensions { get; }

    Task<IReadOnlyList<EmbeddingResult>> GenerateAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken);
}
```

A assinatura é ilustrativa.

Não acoplar Application a:

```text
Ollama
OpenAI
Azure OpenAI
ONNX
HTTP específico
```

A implementação inicial poderá escolher um provider configurável.

---

# 35. Persistência

Application não deverá conhecer:

```text
NpgsqlConnection
SQL
pgvector
COPY
```

Criar fronteiras específicas para infraestrutura.

Evitar, entretanto, criar um repository genérico artificial.

Preferir contratos orientados aos casos reais de uso:

```text
ICiirDocumentWriter
ICiirRelationWriter
IProjectStore
IIndexingRunStore
IRelationResolver
```

ou nomes equivalentes.

---

# 36. Estrutura da solution

Criar aproximadamente:

```text
src/

  Ciir.Indexer.Core/
  Ciir.Indexer.Application/
  Ciir.Indexer.Infrastructure.PostgreSql/
  Ciir.Indexer.Infrastructure.Embeddings/
  Ciir.Indexer.Api/

tests/

  Ciir.Indexer.Core.Tests/
  Ciir.Indexer.Application.Tests/
  Ciir.Indexer.Infrastructure.PostgreSql.Tests/
  Ciir.Indexer.Api.Tests/
```

Se uma camada não possuir responsabilidade real, não criá-la artificialmente.

---

# 37. Responsabilidade de `Core`

Conter somente conceitos independentes de infraestrutura.

Exemplos:

```text
IndexingRun
IndexingStatus
CiirIdentity
EmbeddingTextHash
EmbeddingModel
```

Não depender de:

```text
ASP.NET Core
Npgsql
pgvector
filesystem
HTTP client
```

---

# 38. Responsabilidade de `Application`

Implementar os casos de uso.

Exemplo:

```text
StartIndexation
ImportDocuments
ImportRelations
ResolveRelations
FinalizeIndexation
```

Responsável por orchestration.

Não escrever SQL.

Não gerar HTTP responses.

---

# 39. Responsabilidade da infraestrutura PostgreSQL

Implementar:

```text
projects persistence
documents UPSERT
relations UPSERT
relation resolution
cleanup
indexes
pgvector mapping
migrations
```

Utilizar PostgreSQL de forma eficiente.

Não esconder recursos importantes do PostgreSQL atrás de abstrações genéricas.

---

# 40. Responsabilidade da API

A API deverá:

```text
validar request
resolver DI
chamar Application
retornar status
expor acompanhamento da execução
```

Não deverá:

```text
abrir JSONL
gerar embedding
executar SQL
resolver relações
```

---

# 41. Endpoint de consulta da execução

Disponibilizar:

```http
GET /api/indexations/{id}
```

Exemplo:

```json
{
  "id": "019...",
  "status": "running",
  "documents": {
    "processed": 14234,
    "inserted": 320,
    "updated": 75,
    "embeddingsGenerated": 91,
    "embeddingsReused": 14143
  },
  "relations": {
    "processed": 58213,
    "resolved": 0
  }
}
```

Depois:

```json
{
  "id": "019...",
  "status": "completed",
  "documents": {
    "processed": 18245,
    "inserted": 331,
    "updated": 91,
    "embeddingsGenerated": 103,
    "embeddingsReused": 18142
  },
  "relations": {
    "processed": 52173,
    "resolved": 51960,
    "unresolved": 213
  }
}
```

---

# 42. Validação do path

Como a API recebe um path existente no servidor, tratar o valor como input não confiável.

Implementar:

```text
normalização do path
verificação de existência
verificação de arquivo
extensão esperada
proteção contra acesso fora das raízes permitidas
```

Configuração sugerida:

```text
AllowedInputRoots
```

Exemplo:

```json
{
  "Indexer": {
    "AllowedInputRoots": [
      "/data/ciir"
    ]
  }
}
```

Não permitir que um request arbitrário leia:

```text
/etc/passwd
/secrets/*
```

ou qualquer arquivo disponível ao processo.

---

# 43. Validação do JSONL

Processar linha a linha.

Em caso de JSON inválido, registrar:

```text
line number
error category
message
```

Exemplo:

```text
Invalid CIIR at line 18274.
```

Não guardar todas as linhas inválidas em memória.

---

# 44. Compatibilidade de schema

Persistir:

```text
schema_version
```

por documento.

A aplicação deverá declarar explicitamente quais **major versions** de CIIR aceita.

Exemplo:

```text
SupportedMajorVersions:
- 1
```

A verificação deverá ser feita pela major version do `schemaVersion` recebido, não por
correspondência exata de string contra "major.minor". Um incremento de minor version dentro de uma
major suportada (ex.: `1.0` → `1.1`) é, por definição (semver) e pela própria seção 13 ("CIIR v1.0
/ v1.1 / v1.x"), uma evolução compatível — adição de campos opcionais — e deverá continuar sendo
aceito sem exigir atualização do indexador a cada nova minor version publicada pelo gerador CIIR.

Não tentar interpretar silenciosamente uma major version desconhecida — essa sim deverá continuar
gerando erro por registro (`UnsupportedSchemaVersion`).

---

# 45. Conteúdo sem `embeddingText`

Nem todo futuro `kind` precisa necessariamente ser semanticamente indexável.

Quando não existir `embeddingText`:

```text
embedding = NULL
embedding_text_hash = NULL
```

O documento ainda poderá ser persistido como nó do grafo.

Não inventar embeddingText.

---

# 46. Resolução de relações

A ausência de `target_document_id` não significa necessariamente erro.

Classificar pelo CIIR.

Exemplos:

```text
external
framework
dynamic
unresolved
ambiguous
```

Preservar exatamente:

```text
resolution.status
resolution.origin
resolution.reason
```

O indexador não deverá tentar melhorar ou reinterpretar a análise estática realizada pelo gerador CIIR.

---

# 47. Métricas de resolução

Ao final registrar:

```text
relationsTotal
sourceResolved
targetResolved
externalTargets
unresolvedTargets
ambiguousTargets
dynamicTargets
```

Separar:

```text
target não deve ser resolvido
```

de:

```text
target deveria existir mas não foi encontrado
```

Por exemplo:

```text
external/framework
```

não é falha do indexador.

---

# 48. Logging

Utilizar:

```text
Microsoft.Extensions.Logging
```

Logging estruturado.

Exemplo conceitual:

```text
IndexationId
ProjectId
ProjectName
FilePath
CIIRId
BatchNumber
```

Não fazer logging integral de:

```text
embeddingText
embedding vector
CIIR JSON
```

por padrão.

---

# 49. CancellationToken

Toda operação I/O assíncrona relevante deverá aceitar e respeitar:

```csharp
CancellationToken
```

Incluindo:

```text
leitura do JSONL
PostgreSQL
embedding provider
relation resolution
```

---

# 50. Observabilidade

Registrar pelo menos:

```text
duration
records/sec
embedding requests
embedding cache hits via hash
embedding generation duration
database batch duration
relations/sec
relation resolution duration
failed records
```

Não introduzir uma plataforma de observabilidade específica no Core/Application.

---

# 51. Concorrência

Document Import e Relation Import podem executar simultaneamente porque ambos dependem somente do artifact.

Conceitualmente:

```text
Task.WhenAll(
    ImportDocumentsAsync(...),
    ImportRelationsAsync(...)
)
```

Isso NÃO significa paralelizar indiscriminadamente cada linha.

A concorrência interna deverá ser controlada.

Especialmente o embedding provider deverá possuir limite configurável.

---

# 52. Idempotência

Indexar duas vezes exatamente o mesmo arquivo deverá resultar em:

```text
0 novos documentos
0 embeddings recalculados
0 relações semanticamente duplicadas
```

Os registros poderão ter:

```text
last_seen_run_id
updated_at
```

atualizados conforme a estratégia escolhida.

O estado semanticamente observável deverá permanecer o mesmo.

---

# 53. Atualização

Se somente `embeddingText` de um método mudar:

```text
document encontrado
        │
        ▼
embeddingTextHash mudou
        │
        ▼
generate embedding
        │
        ▼
update vector
```

Se não mudar:

```text
document encontrado
        │
        ▼
embeddingTextHash igual
        │
        ▼
preserve vector
```

---

# 54. Mudança do modelo

Mesmo com:

```text
embeddingTextHash igual
```

o vetor NÃO é reutilizável se o modelo mudou.

A regra correta será:

```text
embeddingTextHash igual
AND
embedding model igual
AND
embedding dimensions iguais
```

Somente nesse caso o embedding existente poderá ser reutilizado.

Portanto a decisão de regeneração deve ser conceitualmente:

```text
needsEmbedding =
    document does not exist
    OR embeddingTextHash changed
    OR embeddingModel changed
    OR embeddingDimensions changed
    OR vector is null
```

---

# 55. Alteração do modelo do projeto

Quando um projeto previamente indexado mudar de modelo:

```text
bge-m3
   ↓
novo-modelo
```

todos os documentos que possuem embedding deverão ser reprocessados, mesmo que:

```text
embeddingTextHash
```

não tenha mudado.

Não misturar silenciosamente vetores de modelos diferentes dentro do mesmo projeto.

---

# 56. Pesquisa futura

Embora esta aplicação NÃO implemente pesquisa, o schema deverá permitir futuramente algo equivalente a:

```sql
SELECT
    id,
    ciir_id,
    kind,
    symbol_qualified_name,
    embedding <=> @query_embedding AS distance
FROM ciir_documents
WHERE project_id = @project
  AND embedding IS NOT NULL
ORDER BY embedding <=> @query_embedding
LIMIT 20;
```

E expansão de grafo:

```sql
SELECT *
FROM ciir_relations
WHERE source_document_id = @document
   OR target_document_id = @document;
```

A implementação dessas consultas fica explicitamente fora desta aplicação.

---

# 57. Reconstrução futura do call graph

A estrutura deverá permitir:

```text
seed semantic search
        │
        ▼
method A
        │
        ├── CALLS B
        ├── CALLS C
        └── READS D
```

e posteriormente:

```text
A
├── B
│   └── E
└── C
    └── F
```

através de queries recursivas ou aplicação cliente.

Nenhuma informação extra deverá ser necessária além de:

```text
ciir_documents
ciir_relations
```

---

# 58. Integridade referencial

Foreign keys:

```text
ciir_documents.project_id
    → projects.id

ciir_relations.project_id
    → projects.id

ciir_relations.source_document_id
    → ciir_documents.id

ciir_relations.target_document_id
    → ciir_documents.id
```

As duas últimas deverão aceitar NULL.

Configurar regras de DELETE conscientemente.

Preferencialmente:

```text
document deletion
     ↓
related relation cleanup
```

sem deixar relações apontando para IDs inexistentes.

---

# 59. Testes obrigatórios

Criar testes para pelo menos:

```text
novo documento gera embedding
hash igual não gera embedding
hash alterado gera embedding

modelo alterado gera embedding novamente

document UPSERT é idempotente

relations são importadas na segunda leitura

relation pode existir antes do document FK

source FK é resolvida posteriormente

target FK é resolvida posteriormente

external target permanece NULL

target inexistente permanece NULL

relações duplicadas não são criadas

document removido do novo JSONL é removido após sucesso

relation removida do JSONL é removida após sucesso

execução com falha não limpa dados válidos anteriores

arquivo grande é processado sem carregamento integral

cancelamento é propagado

embedding dimension incorreta falha

CIIR id é preservado integralmente
```

---

# 60. Testes de integração PostgreSQL

Os testes de integração deverão utilizar PostgreSQL real com extensão pgvector.

Não substituir os testes importantes de:

```text
vector
FK
UPSERT
HNSW
SQL relation resolution
cleanup
```

por um provider em memória.

Utilizar containers de teste quando apropriado.

---

# 61. Performance test

Criar pelo menos um teste ou benchmark capaz de gerar um JSONL sintético grande.

Validar que o consumo de memória cresce de forma limitada.

O requisito não deverá ser:

```text
arquivo 10x maior
=
memória 10x maior
```

O pipeline deverá permanecer aproximadamente bounded pelo tamanho dos batches.

---

# 62. Qualidade

Aplicar:

```text
SOLID
DRY
KISS
YAGNI
Separation of Concerns
Dependency Inversion
Composition over inheritance
```

Seguir boas práticas compatíveis com Sonar:

```text
nullable reference types
CancellationToken
async I/O
argument validation
structured logging
resource disposal
sem secrets hardcoded
sem warnings relevantes
complexidade cognitiva controlada
sem suppressions injustificadas
```

Não criar abstrações apenas para satisfazer padrões.

---

# 63. Não implementar agora

Não implementar:

```text
semantic search API
RAG API
question answering
LLM
reranking
hybrid search
graph expansion
graph traversal API
Neo4j
Qdrant
ElasticSearch
OpenSearch
query embedding endpoint
Kubernetes
queue
repository cloning
Git integration
CIIR generation
source-code analysis
```

A responsabilidade termina em:

```text
CIIR JSONL
    │
    ▼
PostgreSQL + pgvector
```

---

# 64. Critérios de aceite

A implementação estará completa quando:

## Importação

Um CIIR JSONL válido puder ser enviado através da API e seus registros forem importados linha a linha.

## Embeddings

Todo documento com `embeddingText` possuir embedding correspondente.

## Incrementalidade

Reindexar o mesmo CIIR não recalcular embeddings desnecessariamente.

## Mudança

Alterar `embeddingTextHash` provocar recálculo do vetor.

## Modelo

Alterar o modelo provocar recálculo dos embeddings.

## Relações

Todas as relações CIIR forem persistidas.

## Foreign keys

Após a importação, relações internas resolvíveis possuírem:

```text
source_document_id
target_document_id
```

preenchidos.

## External

Relações externas puderem permanecer com:

```text
target_document_id = NULL
```

sem serem consideradas erro.

## Grafo

For possível consultar:

```text
outgoing edges
incoming edges
```

a partir de qualquer documento.

## Idempotência

Executar duas vezes o mesmo arquivo não gerar duplicações.

## Exclusões

Entidades removidas de uma nova CIIR não permanecerem indefinidamente como dados atuais.

## Memória

O arquivo não for carregado integralmente em memória.

## Qualidade

Todos os testes passarem e a solution compilar sem warnings relevantes.

---

# 65. Modelo lógico final

```text
                         ┌─────────────────────┐
                         │      projects       │
                         │─────────────────────│
                         │ id                  │
                         │ name                │
                         │ embedding_model     │
                         │ dimensions          │
                         └─────────┬───────────┘
                                   │
                    ┌──────────────┴──────────────┐
                    │                             │
                    ▼                             ▼
       ┌────────────────────────┐      ┌─────────────────────────┐
       │     ciir_documents     │      │     ciir_relations      │
       │────────────────────────│      │─────────────────────────│
       │ id                     │◄─────│ source_document_id NULL │
       │ project_id             │      │ target_document_id NULL │──┐
       │ ciir_id                │      │ source_ciir_id          │  │
       │ kind                   │      │ target_ciir_id          │  │
       │ symbol_*               │      │ kind                    │  │
       │ embedding_text         │      │ target_symbol           │  │
       │ embedding_text_hash    │      │ resolution_*            │  │
       │ embedding VECTOR       │      │ project_id              │  │
       │ content JSONB          │      └─────────────────────────┘  │
       └────────────────────────┘                  ▲                │
                    ▲                             │                │
                    └─────────────────────────────┴────────────────┘
```

O documento representa um nó.

A relação representa uma aresta.

O vetor representa a posição semântica do nó.

Esses três elementos deverão permanecer conceitualmente separados.

---

# 66. Fluxo arquitetural final

```text
POST /api/indexations
          │
          ▼
    StartIndexation
          │
          ▼
   indexing_runs
          │
          ├─────────────────────────────┐
          │                             │
          ▼                             ▼
 read ciir.jsonl                 read ciir.jsonl
          │                             │
          ▼                             ▼
 Document Import                 Relation Import
          │                             │
     hash check                         │
          │                             │
       changed?                         │
      ┌───┴───┐                         │
      │       │                         │
     yes      no                        │
      │       │                         │
      ▼       ▼                         │
   Embedding reuse vector               │
      │       │                         │
      └───┬───┘                         │
          ▼                             ▼
 ciir_documents                  ciir_relations
          │                             │
          └─────────────┬───────────────┘
                        ▼
                Relation Resolver
                        │
             ┌──────────┴──────────┐
             ▼                     ▼
       resolve source        resolve target
             │                     │
             └──────────┬──────────┘
                        ▼
                  stale cleanup
                        │
                        ▼
               indexing completed
```

Esta separação deverá ser considerada uma restrição arquitetural central da implementação.


# Atualização — Fingerprint do Embedding

## Hash do conteúdo vs. fingerprint do embedding

O sistema deverá distinguir dois hashes com responsabilidades diferentes.

### `embedding_text_hash`

Representa exclusivamente o conteúdo semântico recebido do CIIR.

Seu valor deverá corresponder a:

```text
CIIR.embeddingTextHash
```

Conceitualmente:

```text
SHA-256(embeddingText)
```

Esse campo deverá ser preservado porque faz parte do contrato CIIR e permite identificar mudanças na projeção semântica independentemente da tecnologia utilizada para gerar o vetor.

### `embedding_fingerprint_hash`

O indexador deverá gerar um segundo hash responsável por determinar se um vetor armazenado ainda é válido.

Ele deverá considerar obrigatoriamente:

```text
embeddingTextHash
embedding model
embedding dimensions
```

A composição deverá ser determinística e utilizar um formato canônico explicitamente definido.

Formato recomendado:

```text
embeddingTextHash
+
"\n"
+
embeddingModel
+
"\n"
+
embeddingDimensions
```

Exemplo conceitual:

```text
sha256:64e89f...
bge-m3
1024
```

O fingerprint será:

```text
SHA-256(
    embeddingTextHash + "\n" +
    embeddingModel + "\n" +
    embeddingDimensions
)
```

O valor persistido deverá utilizar o formato:

```text
sha256:<64 hexadecimal characters>
```

Exemplo:

```text
sha256:a413ea...
```

A composição deverá utilizar UTF-8 e representação decimal invariant da dimensionalidade.

A implementação deverá possuir testes garantindo que a mesma combinação sempre produza exatamente o mesmo fingerprint.

---

# Regra de validade do embedding

A decisão sobre reutilização do vetor deverá ser baseada em:

```text
embedding_fingerprint_hash
```

e não somente em:

```text
embedding_text_hash
```

Fluxo:

```text
incoming CIIR
      │
      ▼
embeddingTextHash
      │
      ├── embedding model
      │
      └── embedding dimensions
      │
      ▼
Embedding Fingerprint
      │
      ▼
compare with stored fingerprint
      │
   ┌──┴───┐
   │      │
 equal  different
   │      │
   ▼      ▼
 reuse   generate
 vector  embedding
```

Portanto:

```text
stored.embedding_fingerprint_hash
=
incoming.embedding_fingerprint_hash
```

significa:

```text
o vetor armazenado ainda é válido
```

Enquanto:

```text
stored.embedding_fingerprint_hash
!=
incoming.embedding_fingerprint_hash
```

significa:

```text
o vetor DEVE ser recalculado
```

---

# Mudanças que invalidam o vetor

Qualquer uma das seguintes alterações deverá resultar automaticamente em um fingerprint diferente.

## Alteração do conteúdo

Antes:

```text
embeddingTextHash = sha256:AAA
model = bge-m3
dimensions = 1024
```

Depois:

```text
embeddingTextHash = sha256:BBB
model = bge-m3
dimensions = 1024
```

Resultado:

```text
fingerprint changed
→ regenerate embedding
```

## Alteração do modelo

Antes:

```text
embeddingTextHash = sha256:AAA
model = bge-m3
dimensions = 1024
```

Depois:

```text
embeddingTextHash = sha256:AAA
model = nomic-embed-text
dimensions = 1024
```

Resultado:

```text
fingerprint changed
→ regenerate embedding
```

## Alteração da dimensionalidade

Antes:

```text
embeddingTextHash = sha256:AAA
model = bge-m3
dimensions = 1024
```

Depois:

```text
embeddingTextHash = sha256:AAA
model = bge-m3
dimensions = 768
```

Resultado:

```text
fingerprint changed
→ regenerate embedding
```

Mesmo que `embeddingText` não tenha sofrido nenhuma alteração.

---

# Alteração da tabela `ciir_documents`

A tabela deverá armazenar ambos os valores:

```sql
embedding_text             text,
embedding_text_strategy    text,

embedding_text_hash        text,
embedding_fingerprint_hash text,

embedding                  vector(<dimensions>)
```

Responsabilidades:

```text
embedding_text_hash
    ↓
identidade do conteúdo semântico

embedding_fingerprint_hash
    ↓
identidade do vetor produzido
```

Não utilizar esses dois conceitos como sinônimos.

---

# Metadados do vetor

Além do fingerprint, o documento deverá permitir identificar explicitamente como seu embedding foi produzido.

Persistir:

```text
embedding_model
embedding_dimensions
```

diretamente no documento ou através da referência ao projeto/modelo adotado.

Preferencialmente, manter no documento os dados necessários para auditoria do vetor efetivamente armazenado.

Estrutura conceitual atualizada:

```sql
ciir_documents
(
    id                         bigint PK,

    project_id                 bigint NOT NULL FK projects,

    ciir_id                    text NOT NULL,

    schema_version             text NOT NULL,
    kind                       text NOT NULL,
    language                   text NOT NULL,

    symbol_name                text,
    symbol_qualified_name      text,
    symbol_canonical_name      text,
    symbol_container           text,

    source_path                text,

    embedding_text             text,
    embedding_text_strategy    text,
    embedding_text_hash        text,

    embedding_model            text,
    embedding_dimensions       integer,
    embedding_fingerprint_hash text,

    embedding                  vector(<dimensions>),

    content                    jsonb NOT NULL,

    last_seen_run_id           uuid,

    created_at                 timestamptz NOT NULL,
    updated_at                 timestamptz NOT NULL
)
```

Isso permitirá saber exatamente qual configuração produziu o vetor atualmente armazenado, mesmo que a configuração do projeto seja posteriormente alterada.

---

# Algoritmo de decisão

O processo de importação deverá executar conceitualmente:

```text
read CIIR document
       │
       ▼
embeddingTextHash
       │
       ▼
resolve current embedding model
       │
       ▼
resolve current dimensions
       │
       ▼
calculate embeddingFingerprint
       │
       ▼
document exists?
       │
   ┌───┴────┐
   │        │
  no       yes
   │        │
   ▼        ▼
embed    fingerprint equal?
              │
          ┌───┴────┐
          │        │
         yes       no
          │        │
          ▼        ▼
        reuse     embed
        vector
```

A regra fica reduzida a:

```text
needsEmbedding =
    document does not exist
    OR embedding is null
    OR stored.embedding_fingerprint_hash
       != calculated.embedding_fingerprint_hash
```

Não será necessário espalhar comparações individuais como:

```text
hash changed?
model changed?
dimensions changed?
```

pelo código.

Toda a decisão deverá ficar encapsulada no cálculo e comparação do fingerprint.

---

# Componente responsável pelo fingerprint

A regra deverá existir em um componente coeso.

Exemplo conceitual:

```csharp
public interface IEmbeddingFingerprintGenerator
{
    string Generate(
        string embeddingTextHash,
        string model,
        int dimensions);
}
```

Ou implementação equivalente caso uma interface separada não se justifique.

O importante é que a montagem da string canônica e o cálculo do SHA-256 não fiquem duplicados em diferentes partes da aplicação.

---

# Testes adicionais obrigatórios

Adicionar:

```text
mesmo text hash + mesmo modelo + mesma dimensão
    → mesmo fingerprint

text hash diferente
    → fingerprint diferente

modelo diferente
    → fingerprint diferente

dimensão diferente
    → fingerprint diferente

mesmo fingerprint + vetor existente
    → embedding não é recalculado

modelo alterado
    → embedding é recalculado

dimensão alterada
    → embedding é recalculado

conteúdo alterado
    → embedding é recalculado
```

Deverá existir também um golden test para o algoritmo.

Exemplo de input conhecido:

```text
embeddingTextHash = sha256:abc...
model = bge-m3
dimensions = 1024
```

deverá sempre produzir exatamente o mesmo SHA-256 esperado.

Isso impede mudanças acidentais futuras no algoritmo de composição.

---

# Regra arquitetural

O CIIR responde:

```text
"O conteúdo semântico mudou?"
```

através de:

```text
embeddingTextHash
```

O indexador responde:

```text
"O vetor armazenado ainda representa corretamente esse
conteúdo usando a configuração de embedding atual?"
```

através de:

```text
embedding_fingerprint_hash
```

Essa separação deverá ser preservada.

---

# Atualização — Logging estruturado em JSON

Todo log emitido pela aplicação deverá utilizar formato JSON, sem exceção.

Isso inclui:

```text
logs da própria aplicação (Microsoft.Extensions.Logging)
logs de bibliotecas de terceiros (ex.: FluentMigrator)
logs emitidos antes da conclusão do host (falhas de configuração)
```

Não deverá existir nenhum provider de logging configurado que escreva texto plano diretamente no
console (ex.: announcers próprios de bibliotecas de terceiros), pois isso quebraria a garantia de
que toda a saída de log é JSON parseável.

Quando uma biblioteca de terceiros oferecer duas formas de integração — (a) um logger próprio que
escreve texto plano diretamente na saída padrão, ou (b) integração via `Microsoft.Extensions.Logging`
— utilizar exclusivamente a opção (b), para que a mensagem passe pelos providers configurados pela
aplicação (e, portanto, pelo formatter JSON).

Não fazer logging integral de dados sensíveis ou de alto volume por padrão (ver §48):

```text
embeddingText
embedding vector
CIIR JSON
```

---

# Atualização — Falha de configuração e de banco de dados na inicialização

A aplicação deverá falhar rápido (fail-fast) quando não for possível concluir a inicialização por:

```text
configuração ausente ou inválida
   (ex.: seção de configuração obrigatória não encontrada,
    connection string ausente, configuração de embedding inválida)

impossibilidade de comunicação com o PostgreSQL na inicialização
   (ex.: banco inacessível durante a aplicação das migrations)
```

Nesses casos, a aplicação deverá:

```text
1. registrar um log de erro (nível Critical) estruturado em JSON,
   contendo a exceção e o motivo da falha

2. encerrar o processo (fail-fast), sem tentar servir requisições
   em estado degradado ou parcialmente inicializado
```

A aplicação NÃO deverá continuar em execução aceitando requisições HTTP se a etapa de
inicialização — validação de configuração e aplicação das migrations — não for concluída com
sucesso.

Essa regra aplica-se à inicialização (composition root). Falhas ocorridas durante a execução de uma
indexação específica (ex.: erro pontual de banco de dados ao processar um lote) seguem a estratégia
própria de tratamento de erro de execução (`indexing_runs.status = failed`, ver §26), e não devem
derrubar o processo inteiro.

---

# Atualização — Nome da tabela de versionamento de schema (FluentMigrator)

O FluentMigrator utiliza, por padrão, uma tabela chamada `VersionInfo` para controlar quais
migrations já foram aplicadas.

Como esse banco de dados pode ser compartilhado com outras aplicações (cada uma com seu próprio
controle de migrations), o nome padrão `VersionInfo` deverá ser substituído por um nome específico
da aplicação, no formato:

```text
<nome-da-aplicação>-VersionInfo
```

Para o CIIR Indexer, o nome da aplicação utilizado é `ciir-indexer` (mesma convenção adotada nos
nomes de container do `docker-compose.yml`), portanto:

```text
ciir-indexer-VersionInfo
```

O índice único interno do FluentMigrator sobre essa tabela (`UC_Version` por padrão) também deverá
ser renomeado de forma correspondente. Diferente dos nomes de coluna, o nome de um índice é um
identificador global ao schema do PostgreSQL — mantê-lo como `UC_Version` colidiria com o índice
homônimo de qualquer outra aplicação (ou instalação anterior desta mesma aplicação) que também use
o nome padrão do FluentMigrator no mesmo schema.

Essa customização deverá ser implementada através de `IVersionTableMetaData`, e não através de
convenção implícita de nomes ou de scripts SQL manuais.

---

# Atualização — Preenchimento de `target_ciir_id` via resolução por símbolo

Complementa a extensão descrita nas seções "Atualização — Fingerprint do Embedding" (relação entre
`embedding_text_hash` e `embedding_fingerprint_hash`) e o mecanismo de resolução por símbolo já
documentado no código (`IRelationResolver`), que existe porque, na prática, o gerador CIIR C# nunca
preenche `relation.target.id`.

Como consequência, ao resolver `target_document_id` através do fallback por símbolo — cruzando
`ciir_relations.target_symbol` com `ciir_documents.symbol_canonical_name`, dentro do mesmo projeto
e apenas quando exatamente um documento corresponde — a aplicação já descobre, nesse mesmo passo, a
identidade CIIR do documento encontrado (`ciir_documents.ciir_id`).

Essa identidade deverá ser persistida também em:

```text
ciir_relations.target_ciir_id
```

e não apenas em `target_document_id`.

## Por que isso é necessário

A seção 17 ("Dupla representação da identidade nas relações") estabelece que toda relação deverá
possuir, quando resolvida, tanto a identidade CIIR (`source_ciir_id`/`target_ciir_id`) quanto a
identidade relacional (`source_document_id`/`target_document_id`). Sem esse preenchimento
adicional, uma relação resolvida por símbolo ficaria com a identidade relacional preenchida mas a
identidade CIIR ainda `NULL` — uma inconsistência que a seção 17 não previa, porque assumia (como o
literal da seção 20) que `target_ciir_id` sempre viria diretamente do CIIR.

## Regra

```text
resolução por símbolo encontra exatamente um documento
        │
        ▼
target_document_id = documento.id
target_ciir_id      = documento.ciir_id
```

Ambos os campos deverão ser preenchidos na mesma operação SQL (mesmo `UPDATE`), nunca em passos
separados que possam divergir entre si.

## O que NÃO muda

- A resolução literal por `ciir_id` (seção 20) continua funcionando como antes: quando
  `relation.target.id` já vem preenchido pelo CIIR, `target_ciir_id` já é conhecido desde a
  importação e não depende deste mecanismo.
- Este preenchimento só ocorre para relações cujo `resolution_status` reportado pelo CIIR seja
  `resolved` (seção 46) — external/dynamic/ambiguous/unresolved nunca têm `target_ciir_id`
  inventado.
- Quando a resolução por símbolo encontra mais de um documento correspondente (ambiguidade
  genuína, seção sobre `match_count`), nem `target_document_id` nem `target_ciir_id` são
  preenchidos — a linha permanece sem resolução, como já era o caso.

## Testes adicionais obrigatórios

```text
resolução por símbolo bem-sucedida
    → target_ciir_id preenchido com o ciir_id do documento encontrado

resolução por símbolo ambígua (mais de um documento)
    → target_ciir_id permanece NULL

relação com status externo/dinâmico cujo símbolo coincidiria com um documento
    → target_ciir_id permanece NULL
```

---

# Atualização — `resolution.origin = "solution"` e resolução entre projetos

A partir do CIIR `schemaVersion` 1.1, `resolution.origin` passou a incluir um valor adicional:

```text
solution
```

Diferente de:

```text
project
```

(o alvo está no MESMO projeto do documento de origem), `origin: solution` significa que o alvo está
em um **projeto diferente da mesma solução analisada** — por exemplo, `CodeRag.Api` chamando um
método definido em `CodeRag.Application`.

Essa distinção deverá ser preservada exatamente como reportada pelo CIIR (seção 46), assim como
`project`, `dependency`, `framework`, `runtime`, `external_service` e `unknown`.

## Impacto na resolução de relações

A resolução por símbolo (ver "Atualização — Preenchimento de `target_ciir_id` via resolução por
símbolo") busca o documento correspondente a `target_symbol` comparando com
`ciir_documents.symbol_canonical_name`.

Para relações `origin: project`, essa busca deverá continuar restrita ao mesmo `project_id` da
relação — o alvo está, por definição, no mesmo projeto.

Para relações `origin: solution`, a busca deverá abranger **todos os projetos tocados pela
execução atual** (não apenas o projeto da relação), já que o alvo está, por definição, em outro
projeto da mesma solução.

```text
origin = project
      │
      ▼
buscar documento-alvo APENAS no project_id da relação

origin = solution
      │
      ▼
buscar documento-alvo em QUALQUER project_id tocado pela execução
```

## Por que dois passos separados, e não um único mais amplo

Ampliar a busca para todos os projetos apenas quando `origin = solution` — e nunca para `origin =
project` — é proposital: evita que uma relação `project` seja incorretamente resolvida contra um
documento de outro projeto que, por coincidência, tenha o mesmo `symbol_canonical_name` (por
exemplo, dois projetos distintos definindo cada um sua própria classe utilitária com nome idêntico).
A ampliação de escopo nunca deverá alterar o comportamento já existente para relações `origin:
project`.

A regra de desambiguação (`match_count = 1`, ver seção anterior) continua valendo, agora calculada
sobre o conjunto ampliado de documentos quando `origin = solution` — se o símbolo aparecer em mais
de um projeto da solução, a relação permanece sem resolução em vez de adivinhar qual ocorrência é a
correta.

## Testes adicionais obrigatórios

```text
relação com origin=solution cujo símbolo existe em um projeto diferente
    → target_document_id e target_ciir_id preenchidos com o documento do OUTRO projeto

relação com origin=project cujo símbolo só existe em outro projeto
    → permanece sem resolução (nunca deve buscar fora do próprio projeto)
```

---

# Atualização — Convenção de commits

Todos os commits deste repositório deverão seguir Conventional Commits (semantic commit messages).

Formato:

```text
<tipo>(<escopo opcional>): <resumo curto>
```

Modo imperativo, sem ponto final no resumo. Exemplos:

```text
feat: add X
fix: correct Y
refactor: simplify Z
chore: update W
test: add coverage for V
docs: document U
```

Essa regra já está registrada em `CLAUDE.md` ("## Commits"); esta seção existe para que a spec do
domínio também deixe explícito que a convenção se aplica a todo o histórico do projeto, não apenas
como preferência de tooling.

---

# Atualização — Identidade de projeto informada pelo chamador

## O bug

Cada linha do JSONL CIIR carrega seu próprio campo `project`. Até esta atualização,
`ImportDocuments` e `ImportRelations` resolviam esse campo POR REGISTRO, chamando
`EnsureProjectAsync(record.project, ...)` — cujo upsert usa `ON CONFLICT (name)`. Consequência:
dois repositórios git completamente diferentes que, por coincidência, possuem um componente
interno com o mesmo nome (ex.: um projeto chamado "Api" em cada um) acabavam mesclados na mesma
linha de `projects` — misturando o contexto de projeto entre repositórios não relacionados.

## A correção

`POST /api/indexations` passa a exigir `projectName` (não vazio) e aceitar opcionalmente
`gitUrl`/`gitRawUrl` (ver §2/§11). Um único projeto é resolvido UMA ÚNICA VEZ por execução — antes
mesmo de criar a linha de `indexing_runs` — e TODAS as linhas do JSONL daquela execução, tanto
documentos quanto relações, são vinculadas a esse projeto.

```text
POST /api/indexations
  { projectName, path, gitUrl?, gitRawUrl? }
        │
        ▼
EnsureProjectAsync(projectName, gitUrl, gitRawUrl, embeddingModel)
        │
        ▼
   project.Id
        │
        ▼
criar indexing_run com project_id
        │
        ▼
Document Import e Relation Import usam o MESMO project_id
para toda linha do arquivo, ignorando o campo "project" de cada registro
```

O campo `project` de cada registro CIIR continua sendo parseado e validado como presente (o
contrato CIIR em si não muda, §3) — ele só deixa de ser consultado para fins de identidade de
projeto no banco.

## Efeito colateral sobre `resolution_origin: "solution"`

A partir do CIIR schemaVersion 1.1, `resolution.origin` distingue `"project"` (alvo no mesmo
componente CIIR) de `"solution"` (alvo em outro componente CIIR da mesma solução analisada) — ver
"Atualização — `resolution.origin = "solution"` e resolução entre projetos". Aquela atualização
existe porque, no modelo antigo, um único arquivo JSONL podia gerar múltiplas linhas em `projects`
(uma por componente CIIR), então uma relação `"solution"` precisava buscar o documento-alvo em
outro `project_id`.

Com o modelo atual, uma única execução sempre resolve para um único `project_id` — os componentes
CIIR internos que antes viravam projetos separados agora convivem sob o mesmo projeto. Relações
`origin: "solution"` entre eles já compartilham o mesmo `project_id` e são resolvidas pelo passo
comum de resolução por símbolo (mesmo projeto), sem depender da busca entre projetos. Essa busca
entre projetos não foi removida — continua correta para o caso, agora mais raro, de alguém importar
deliberadamente dois `projectName` distintos com relações entre si — mas seu peso prático cai
bastante.

## Testes obrigatórios

```text
projectName ausente ou vazio
    → 400, nenhum indexing_run criado

registros com campo "project" diferente no mesmo arquivo
    → todos vinculados ao ÚNICO projectId informado no request

reenvio do mesmo projectName com gitUrl diferente
    → projects.git_url é atualizado (upsert)

dois requests com projectName diferentes, mesmo arquivo
    → dois projetos distintos, sem mistura de contexto
```
