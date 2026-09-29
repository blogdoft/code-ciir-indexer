# Projects CRUD

**Status: concluído.** `GET /api/indexer/projects[?name=][&page=][&page_size=]` (paginado),
`GET /api/indexer/projects/{projectId}`, `POST /api/indexer/projects`,
`PUT /api/indexer/projects/{projectId}` e `DELETE /api/indexer/projects/{projectId}` estão
implementados.

## Contexto

`code-ciir-api` (`code3rag`) chegou a expor CRUD completo de `projects` mesmo não sendo dona da
tabela. Ter dois serviços com CRUD independente sobre a mesma tabela, sem mecanismo de
coordenação, era um risco conscientemente aceito e documentado em
`code-ciir-api/.specs/03-projects-endpoint.md`. Este trabalho moveu o CRUD completo para cá — o
dono real da tabela — e `code-ciir-api` deixou de expor rotas REST de projetos, eliminando o
duplo escritor. Este serviço é o único que cria, altera e exclui projetos; o upload de CIIR
(`02-upload-ciir-garage.md`) só referencia um projeto já cadastrado.

## Contrato

JSON em `camelCase`. O identificador exposto é o `public_id` (UUIDv7) do projeto, chamado `id` no
corpo e `{projectId}` na rota; a chave numérica interna nunca sai do serviço.

`ProjectResponse`: `{ id, name, gitUrl, gitRawUrl, createdAt, updatedAt }`.

- `GET /api/indexer/projects?name=&page=&page_size=` — paginado (page zero-based, default 0;
  `page_size` default 20, máx. 100), filtro parcial case-insensitive por nome (`name`, opcional; vazio ou ausente = sem filtro; `*` é curinga e é convertido para `%` na camada de banco). A ordenação é sempre
  crescente pelo nome do projeto (`ORDER BY name`), antes da paginação. Os parâmetros de
  query mantêm os nomes `page`/`page_size`; o corpo da resposta é
  `{items, page, pageSize, totalCount, totalPages}`. 400 se `name` (acima de 200 caracteres)/`page`/`page_size` for
  inválido.
- `GET /api/indexer/projects/{projectId}` — 200 com o projeto; 400 se `projectId` não for um
  UUID; 404 sem corpo se não existir.
- `POST /api/indexer/projects` — cria um projeto (`name` obrigatório; `gitUrl`/`gitRawUrl`
  opcionais, valores em branco gravados como `null`). Um nome duplicado é 409. 201 com
  `Location` para `GET /api/indexer/projects/{id}`. Não há modelo/dimensão de embedding por
  projeto: o modelo é configuração do deploy (`Embeddings:*`) e fica registrado por documento
  em `ciir_documents` (colunas de `projects` removidas pela migration `20260927000000`).
- `PUT /api/indexer/projects/{projectId}` — substitui todos os campos (replace completo). 200
  com o registro atualizado; 404 se o id não existe; 409 se o novo nome já pertence a outro
  projeto.
- `DELETE /api/indexer/projects/{projectId}` — 204 sem corpo; 404 se o id não existe.
  `ciir_documents`, `ciir_relations`, `indexing_runs`, `ciir_uploads` e `code_query_feedback`
  têm FK para `projects.id` sem `ON DELETE CASCADE`: um projeto com qualquer um desses
  dependentes não pode ser excluído. Hoje a violação de FK não é tratada e a resposta é `500`
  sem corpo (não um `409`).

## Implementação

- `IProjectStore` (`Ciir.Indexer.Application.Ports`) tem `SearchAsync`, `ExistsByNameAsync`,
  `InsertAsync`, `UpdateAsync`, `DeleteAsync` e `GetByPublicIdAsync`, ao lado de `GetByIdAsync`
  (id interno, usado pelo worker de upload). Uma única implementação (`ProjectStore`,
  Infrastructure.PostgreSql) cobre todas as operações sobre a tabela `projects`.
- Validação/orquestração em `Ciir.Indexer.Application.UseCases`: `ListProjects`, `CreateProject`,
  `UpdateProject`, `DeleteProject` (uma classe por caso de uso, seguindo o padrão já usado por
  `SubmitCiirUpload`/`RegisterCiirUpload`/etc.), mais `ProjectFailures`/`ProjectValidation`
  compartilhados. Um `GET` por id não precisa de validação além do parse do id, então
  `ProjectsController.GetAsync` chama `IProjectStore.GetByPublicIdAsync` diretamente, sem uma classe de
  caso de uso dedicada - mesmo padrão já usado por `IndexationsController.GetAsync` e
  `CiirUploadsController.GetAsync`.
- `ProjectsController` (`api/indexer/projects`) segue o mesmo padrão de
  `IndexationsController`/`CiirUploadsController`: `[ApiController]`, `Result<T>.Map` para
  mapear falhas de domínio em Problem Details via `FailureResults.ToActionResult`.
- `Ciir.Indexer.Core.Project` expõe `CreatedAt`/`UpdatedAt`.

## Achado replicado de `code-ciir-api`

`[ApiController]` reescreve um `NotFoundResult()` vazio em um corpo JSON de Problem Details
automaticamente, a menos que `ApiBehaviorOptions.SuppressMapClientErrors = true` seja
configurado - sem isso, um 404 devolvia corpo, violando o contrato ("404 sem corpo") já
documentado (mas não garantido) pelos controllers existentes. Corrigido em `Program.cs`,
beneficiando `IndexationsController`/`CiirUploadsController` também, que já reivindicavam esse
comportamento em seus comentários XML sem tê-lo de fato garantido.

## Verificação de saída

- Testes de integração (Testcontainers) cobrindo `SearchAsync`, `ExistsByNameAsync`,
  `InsertAsync`, `UpdateAsync`, `DeleteAsync` e `GetByIdAsync` — `ProjectStoreTests`.
- Testes de unidade cobrindo validação de campos, conflito de nome e paginação —
  `ListProjectsTests`, `CreateProjectTests`, `UpdateProjectTests`, `DeleteProjectTests`.
- Testes de controller (`IProjectStore` substituído via NSubstitute) cobrindo os cinco verbos —
  `ProjectsControllerTests`.
- Suíte completa (`dotnet test`) e `dotnet format --verify-no-changes` verdes após a mudança.
