# Projects CRUD

**Status: concluído.** `GET /api/projects[?name=][&page=][&page_size=]` (paginado),
`GET /api/projects/{projectId}`, `POST /api/projects`, `PUT /api/projects/{projectId}` e
`DELETE /api/projects/{projectId}` estão implementados.

## Contexto

`code-ciir-api` (`code3rag`) expunha CRUD completo de `projects` mesmo não sendo dona da tabela -
este serviço (`code-ciir-indexer`) já era o único escritor de fato, via
`IProjectStore.EnsureProjectAsync` (chamado implicitamente por `POST /api/indexations` e
`POST /api/ciir-uploads`, spec's "Atualização — Identidade de projeto informada pelo chamador").
Ter dois serviços com CRUD independente sobre a mesma tabela, sem mecanismo de coordenação, era um
risco conscientemente aceito e documentado em `code-ciir-api/.specs/03-projects-endpoint.md`
("Reversão"). Este trabalho move o CRUD completo para cá - o dono real da tabela - e reduz
`code-ciir-api`'s `/api/v1/projects` de volta a somente leitura (`GET`), eliminando o
double-writer.

## Contrato

Mesma forma de contrato de `code-ciir-api`'s antigo `/api/v1/projects`, adaptada ao `Project` já
existente aqui (que já inclui `gitUrl`/`gitRawUrl`, ausentes na versão original de
`code-ciir-api`):

- `GET /api/projects?name=&page=&page_size=` — paginado (page zero-based, default 0; page_size
  default 20, máx. 100), filtro parcial case-insensitive por nome. 200 com
  `{items, page, pageSize, totalCount, totalPages}`; 400 se `name`/`page`/`pageSize` for
  inválido.
- `GET /api/projects/{projectId}` — 200 com o projeto; 400 se `projectId` não for inteiro
  positivo; 404 sem corpo se não existir.
- `POST /api/projects` — cria um projeto diretamente (`name`, `embeddingModel`,
  `embeddingDimensions` obrigatórios; `gitUrl`/`gitRawUrl` opcionais). Ao contrário de
  `EnsureProjectAsync` (usado pelo fluxo de indexação), um nome duplicado aqui é 409, não um
  upsert silencioso. 201 com `Location` para `GET /api/projects/{id}`.
- `PUT /api/projects/{projectId}` — substitui todos os campos (replace completo). 200 com o
  registro atualizado; 404 se o id não existe; 409 se o novo nome já pertence a outro projeto.
- `DELETE /api/projects/{projectId}` — remove o projeto. 204 sem corpo; 404 se o id não existe.
  Não remove `ciir_documents`/`ciir_relations` associados - eles ficam apontando para um
  `project_id` inexistente se o projeto tinha documentos indexados.

## Implementação

- `IProjectStore` (`Ciir.Indexer.Application.Ports`) ganhou `SearchAsync`, `ExistsByNameAsync`,
  `InsertAsync`, `UpdateAsync`, `DeleteAsync`, ao lado do já existente
  `EnsureProjectAsync`/`GetByIdAsync` usado pelo fluxo de indexação. Uma única implementação
  (`ProjectStore`, Infrastructure.PostgreSql) cobre os dois conjuntos de operações sobre a mesma
  tabela `projects`.
- Validação/orquestração em `Ciir.Indexer.Application.UseCases`: `ListProjects`, `CreateProject`,
  `UpdateProject`, `DeleteProject` (uma classe por caso de uso, seguindo o padrão já usado por
  `StartIndexation`/`SubmitCiirUpload`/etc.), mais `ProjectFailures`/`ProjectValidation`
  compartilhados. Um `GET` por id não precisa de validação além do parse do id, então
  `ProjectsController.GetAsync` chama `IProjectStore.GetByIdAsync` diretamente, sem uma classe de
  caso de uso dedicada - mesmo padrão já usado por `IndexationsController.GetAsync` e
  `CiirUploadsController.GetAsync`.
- `ProjectsController` (`api/projects`) segue o mesmo padrão de
  `IndexationsController`/`CiirUploadsController`: `[ApiController]`, `Result<T>.Map` para
  mapear falhas de domínio em Problem Details via `FailureResults.ToActionResult`.
- `Ciir.Indexer.Core.Project` ganhou `CreatedAt`/`UpdatedAt` (colunas já existentes na tabela
  desde a migration `AddProjectIdentityFields`, mas não expostas no domínio até agora).

## Achado replicado de `code-ciir-api`

`[ApiController]` reescreve um `NotFoundResult()` vazio em um corpo JSON de Problem Details
automaticamente, a menos que `ApiBehaviorOptions.SuppressMapClientErrors = true` seja
configurado - sem isso, um 404 devolvia corpo, violando o contrato ("404 sem corpo") já
documentado (mas não garantido) pelos controllers existentes. Corrigido em `Program.cs`,
beneficiando `IndexationsController`/`CiirUploadsController` também, que já reivindicavam esse
comportamento em seus comentários XML sem tê-lo de fato garantido.

## Verificação de saída

- Testes de integração (Testcontainers) cobrindo `SearchAsync`, `ExistsByNameAsync`,
  `InsertAsync`, `UpdateAsync`, `DeleteAsync` além dos já existentes `EnsureProjectAsync`/
  `GetByIdAsync` — `ProjectStoreTests`.
- Testes de unidade cobrindo validação de campos, conflito de nome e paginação —
  `ListProjectsTests`, `CreateProjectTests`, `UpdateProjectTests`, `DeleteProjectTests`.
- Testes de controller (`IProjectStore` substituído via NSubstitute) cobrindo os cinco verbos —
  `ProjectsControllerTests`.
- Suíte completa (`dotnet test`) e `dotnet format --verify-no-changes` verdes após a mudança.
