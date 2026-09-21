# Autenticação opcional via Keycloak

**Status: concluído.** Quando a autenticação está ligada (`Keycloak:Enabled = true`), toda a API exige
um access token (`Authorization: Bearer <jwt>`) emitido pelo realm configurado. Quando está desligada
(o padrão), nada muda: todos os endpoints continuam abertos, como hoje. Ligar/desligar é uma chave
explícita, **não** deduzido de as demais configurações do Keycloak estarem preenchidas (ver
"Atualização - Chave explícita para ligar/desligar").

## Contexto

Hoje a API não tem nenhuma autenticação (`Program.cs` chama `UseAuthorization()` sem nenhum
esquema registrado). Isso é aceitável em desenvolvimento local, mas o serviço é exposto pelo
ingress `blogdoft.home.arpa/code-brain/api/indexer`, e três dos seus endpoints escrevem dados
(`POST/PUT/DELETE /api/indexer/projects`, `POST /api/indexer/ciir-uploads[/register]`). A
autenticação precisa ser **opt-in por configuração**, não por build/ambiente: o mesmo binário/imagem
roda aberto localmente e protegido no cluster, só mudando `Keycloak__*` no ConfigMap/Secret.

Escopo: **autenticação** apenas (o chamador apresenta um token válido do realm). Não há
autorização por role/scope/client - qualquer token válido acessa qualquer endpoint. Isso fica fora
de escopo até existir um requisito concreto.

## Configuração

Seção `Keycloak` (`appsettings.json` / variáveis `Keycloak__*`):

| Chave | Tipo | Padrão | Descrição |
|---|---|---|---|
| `Enabled` | bool | `false` | Liga/desliga a autenticação. Com `false`, todas as demais chaves desta seção são ignoradas. |
| `Authority` | string | *(vazio)* | URL do realm, ex.: `https://keycloak.home.arpa/realms/blogdoft`. Obrigatória quando `Enabled = true`. |
| `Audience` | string | *(vazio)* | Se preenchido, o claim `aud` do token precisa conter este valor. Se vazio, a audiência **não** é validada (por padrão o Keycloak emite `aud: account`, a menos que um *audience mapper* esteja configurado no client). |
| `ClientId` | string | *(vazio)* | `client_id` do client do realm que representa esta aplicação. É o **mesmo** client usado pelo botão **Authorize** do Swagger UI para redirecionar o usuário ao login do Keycloak (ver "Atualização - Login via Keycloak no Swagger UI"): a API e o Swagger compartilham um único `clientId`. Se vazio, o Swagger UI só aceita um token colado. |
| `RequireHttpsMetadata` | bool | `true` | Exige HTTPS para buscar o *discovery document*/JWKS. Só deve ser `false` contra um Keycloak local em HTTP. |

### Regra de habilitação

- **Ligada** = `Keycloak:Enabled` verdadeiro. Ausente ou `false` significa **desligada**: nenhuma
  das demais chaves é lida nem validada (uma `Authority` inválida não impede o startup com a
  autenticação desligada).
- Com `Enabled = true`, o startup falha se:
  - `Authority` estiver vazia ou só com espaços (ligar a autenticação sem dizer contra qual realm
    validar tokens não tem interpretação segura);
  - `Authority` não for uma URL absoluta `http`/`https`;
  - `Authority` for `http://` com `RequireHttpsMetadata = true` (falharia só na primeira
    requisição, com uma mensagem pior).
- Erros de configuração seguem o padrão já usado em `Program.cs`: `InvalidOperationException` com
  mensagem explicando a chave, capturada pelo `catch` do topo, que loga `LogCritical` e encerra.

## Comportamento

### Autenticação desligada (`Enabled = false`)

Nenhum esquema de autenticação, nenhuma política e nenhum middleware de autenticação é registrado.
Requisições sem `Authorization` funcionam exatamente como hoje; um header `Authorization` enviado
é simplesmente ignorado. O documento OpenAPI não menciona segurança.

### Autenticação ligada (`Enabled = true`)

- Esquema `JwtBearer` (`Microsoft.AspNetCore.Authentication.JwtBearer`), com `Authority` = a URL do
  realm. Assinatura validada pelas chaves do JWKS descoberto em
  `<Authority>/.well-known/openid-configuration` (cache/rotação de chaves a cargo do próprio
  handler); `iss` validado contra o issuer do discovery document; `exp`/`nbf` validados;
  `aud` validado apenas se `Audience` estiver configurado.
- **`FallbackPolicy` = `RequireAuthenticatedUser`**. Toda rota exige token *por padrão*, inclusive
  qualquer endpoint adicionado no futuro, sem depender de `[Authorize]` em cada controller (o
  desenho inverso - esquecer um atributo e deixar uma rota aberta - é o que se quer evitar).
- Sem token, token malformado, expirado, com assinatura/issuer/audiência inválidos:
  `401 Unauthorized`, `WWW-Authenticate: Bearer`, corpo `application/problem+json` no mesmo formato
  dos demais erros da API (`ProblemResults`), com `detail` genérico (não vaza o motivo específico da
  validação do token).
- Não há `403`: sem autorização por role/scope, um token válido sempre passa.

### Exceções deliberadas a "todas as URLs" (permanecem anônimas)

| Rota | Motivo |
|---|---|
| `GET /health` | O `readinessProbe` do Kubernetes (`.eng/k8s/deployment.yaml`) chama esta rota sem token; exigir autenticação tiraria o pod de serviço. Não expõe dado nenhum. |
| `GET /api/indexer/openapi/{documentName}.json` e Swagger UI em `/api/indexer/swagger` | Um navegador não consegue anexar um Bearer token à navegação para a página do Swagger UI, então protegê-las as tornaria inacessíveis. São só documentação (nenhum dado de projeto/upload). "Try it out" continua exigindo token - ver abaixo. |

Qualquer outra rota, existente ou futura, exige token.

### OpenAPI / Swagger UI (só com a autenticação ligada)

O documento OpenAPI ganha um *security scheme* `Bearer` (`type: http`, `scheme: bearer`,
`bearerFormat: JWT`) e um *security requirement* global. O Swagger UI passa a exibir o botão
**Authorize**, onde se cola o access token, e "Try it out" envia `Authorization: Bearer ...`.
Com a autenticação desligada, o documento não muda.

## Implementação

Autenticação é uma preocupação da borda HTTP (adaptador de entrada), portanto vive **só** em
`Ciir.Indexer.Api` - `Core`/`Application` não são tocados.

- `Authentication/KeycloakOptions.cs` - `Enabled` (default `false`), `Authority`, `Audience`,
  `RequireHttpsMetadata` (default `true`), `SectionName = "Keycloak"`, e
  `FromConfiguration(IConfiguration)`, que devolve `null` com a autenticação desligada e, ligada,
  aplica as validações de startup acima.
- `Authentication/KeycloakAuthenticationExtensions.cs`:
  - `AddKeycloakAuthentication(this IServiceCollection, KeycloakOptions)` - registra `JwtBearer`
    (incl. `OnChallenge` que escreve o `problem+json` do 401) e o `FallbackPolicy`;
  - `AddKeycloakSecurityScheme(this OpenApiOptions)`/`OpenApi/BearerSecurityDocumentTransformer.cs` -
    adiciona o scheme/requirement ao documento OpenAPI.
- `Program.cs`:
  - lê `KeycloakOptions.FromConfiguration` uma vez, no topo;
  - se não nulo: `AddKeycloakAuthentication` + transformer do OpenAPI + `app.UseAuthentication()`;
  - `MapHealthChecks("/health")` e `MapOpenApi(...)` ganham `.AllowAnonymous()`
    (no-op quando nada exige autenticação);
  - ordem dos middlewares passa a ser `UseAuthentication` → `UseAuthorization` → `UseRateLimiter`
    (antes o rate limiter vinha primeiro). Assim uma requisição não autenticada é recusada com 401
    **antes** de ocupar um dos `Uploads:MaxConcurrentUploads` slots de upload.
- Dependência nova: `Microsoft.AspNetCore.Authentication.JwtBearer` (última versão compatível com
  .NET 10, alinhada às demais `Microsoft.AspNetCore.*` do projeto). Nenhum pacote `BlogDoFT.Libs.*`
  cobre validação de token de entrada: `BlogDoFT.Libs.Keycloak` é um provedor de token
  *client-credentials* (saída), e `BlogDoFT.Libs.Api` não tem suporte a autenticação.
- `appsettings.json`: seção `Keycloak` com `Enabled: false`, `Authority`/`Audience` vazios e
  `RequireHttpsMetadata: true` (documenta o formato).
- `Directory.Build.props` expõe os tipos `internal` de todo projeto que não seja de teste ao projeto
  de teste de mesmo nome + `.Tests` (por convenção, ex.: `Ciir.Indexer.Api` →
  `Ciir.Indexer.Api.Tests`), via `InternalsVisibleTo`, em vez de um item por `.csproj`. Aqui isso dá
  acesso ao transformer do OpenAPI, que é `internal`.
- `.eng/k8s/configmap.yaml`: bloco **comentado** com `Keycloak__Authority`/`Keycloak__Audience` -
  a URL real do realm é decisão de quem faz o deploy, e deixar o exemplo ativo trancaria o
  cluster para uma URL inventada. Nenhum Secret novo: token de entrada só precisa de chave
  pública (JWKS).
- `README.md`: seção "Authentication" com a tabela de configuração, as exceções anônimas e um
  exemplo de `curl` com Bearer token.

## Fora de escopo

- Autorização por role/scope/client (ver "Contexto").
- Obtenção de token pelo chamador (fluxo OAuth2 do lado do cliente; Swagger UI só recebe o token
  colado).
- `ValidIssuer` separado de `Authority` (Keycloak acessado por um hostname interno e tokens
  emitidos por um hostname público exigiriam isso; adicionar quando houver o caso real).
- Um serviço Keycloak no `docker-compose.yml` de desenvolvimento.

## Verificação

Testes em `Ciir.Indexer.Api.Tests`, sem Keycloak real: um host mínimo (`TestServer`) registra
`AddKeycloakAuthentication` com a `Configuration` do `JwtBearerOptions` pré-preenchida com uma
chave simétrica de teste (o handler não tenta buscar o discovery document), e tokens de teste são
assinados com essa chave.

- `KeycloakOptionsTests`: sem seção / `Enabled` ausente ou `false` → `null` (desligada), mesmo com
  `Authority`/`Audience`/`ClientId` preenchidos ou com uma `Authority` inválida;
  `Enabled = true` com `Authority` vazia/em branco → erro; `Authority` relativa/não-http → erro;
  `http://` com `RequireHttpsMetadata = true` → erro; `http://` com `false` → ok; configuração
  completa → opções preenchidas.
- `KeycloakAuthenticationTests`, host **habilitado**: sem token → 401 + `WWW-Authenticate: Bearer` +
  `application/problem+json`; token com assinatura errada → 401; token expirado → 401; token com
  issuer errado → 401; token válido → 200; `Audience` configurada e token sem ela → 401; `Audience`
  configurada e token com ela → 200; `/health` sem token → 200; um endpoint qualquer sem
  `[Authorize]` sem token → 401 (prova a `FallbackPolicy`).
- `KeycloakAuthenticationTests`, host **sem** Keycloak: requisição sem token → 200.
- `BearerSecurityDocumentTransformerTests`: o documento ganha o scheme `Bearer` e o requirement
  global.
- `dotnet format` → `dotnet build` (zero warnings) → `dotnet test` verdes.

### Conferência manual (feita, antes da chave `Enabled`)

Rodada quando a autenticação ainda era deduzida da `Authority`; a parte que depende do `Enabled` foi
reverificada na atualização abaixo. Contra o app real (`dotnet run`, Postgres/MinIO locais) e um servidor OIDC falso servindo
discovery document + JWKS RSA (tokens RS256 assinados com `openssl`), exercitando o fluxo real de
descoberta de chaves do `JwtBearer`:

- Sem `Keycloak__Authority`: todas as rotas → 200, um `Authorization: Bearer junk` é ignorado, o
  documento OpenAPI não tem `securitySchemes`.
- Com `Authority`: `/health`, documento OpenAPI e Swagger UI → 200 sem token; sem token, token
  lixo, assinatura errada, expirado e issuer errado → 401 (`WWW-Authenticate: Bearer`,
  `application/problem+json`), inclusive em `POST /projects`, `POST /ciir-uploads` e numa rota
  inexistente; token válido → 200 (e 404 numa rota inexistente); documento OpenAPI com o scheme
  `Bearer` e `security` global.
- Com `Audience`: token com a audiência → 200; com `aud: account` ou sem `aud` → 401.
- `Audience` sem `Authority`, `Authority` relativa e `http://` com `RequireHttpsMetadata=true`
  encerram o processo com exit code 1.

### Correção adjacente: falhas de startup agora são logadas

O `catch` do topo de `Program.cs` chamava `Environment.Exit(1)` antes de o `LoggerFactory` (console
assíncrono) descarregar, então **nenhuma** falha de startup - antiga ou as novas desta spec - chegava
a imprimir a mensagem `LogCritical` (reproduzido no `HEAD` anterior com um `Embeddings:Provider`
inválido: exit code 1, log vazio). O `LoggerFactory` agora é descartado (o que drena a fila) antes do
`Environment.Exit(1)`. Verificado com `Embeddings:Provider` inválido e com cada uma das três
configurações inválidas de `Keycloak`: exit code 1 e o `Critical` com a exceção completa no stdout.

## Atualização - Login via Keycloak no Swagger UI

**Status: concluído.** Colar um access token no Swagger UI funciona, mas obriga a obter o token por
fora. Com `Keycloak:ClientId` configurado, o botão **Authorize** passa a redirecionar o
usuário para a tela de login do Keycloak e, de volta, o Swagger UI já envia o token em "Try it out".

### Configuração

Só uma chave nova, `Keycloak:ClientId` (tabela acima). Ela é opcional:

- **Vazia** (padrão): nada muda - o Swagger UI só oferece o esquema `Bearer` (token colado).
- **Preenchida**: além do `Bearer`, o documento OpenAPI declara um esquema `OAuth2` e o Swagger UI
  é configurado para usá-lo. As duas formas de autenticar ficam disponíveis no diálogo
  **Authorize** (colar um token continua útil, p. ex. para um token de service account).
- Só tem efeito com `Enabled = true` (ver "Regra de habilitação").

**Um único `clientId`**: a aplicação e o Swagger UI são o mesmo client do Keycloak (não há um
`clientId` separado para o Swagger). Consequências: (a) o client é **público**, porque o Swagger UI
roda no navegador e não guarda segredo, então a API em si não usa `client secret`; (b) os tokens
emitidos no login do Swagger têm `azp = ClientId`; (c) `ClientId` **não** define a audiência
validada - `Audience` continua independente (vazia = não validada) e, se preenchida, o client
precisa de um *audience mapper* que a inclua (o Keycloak não põe o `clientId` em `aud` por padrão).

Não há `client secret`: é um client público com Authorization Code + **PKCE** (S256), o único fluxo
adequado a uma SPA/Swagger UI, que não consegue guardar segredo.

### Comportamento

- Esquema `OAuth2` no documento OpenAPI, fluxo `authorizationCode`:
  `authorizationUrl = <Authority>/protocol/openid-connect/auth`,
  `tokenUrl = <Authority>/protocol/openid-connect/token`, escopo `openid`. Os dois URLs são
  chamados pelo **navegador**, então `Authority` precisa ser alcançável de fora do cluster (é o
  caso do exemplo `https://keycloak.home.arpa/realms/...`); ver "Fora de escopo" para um hostname
  interno diferente do público.
- O requisito de segurança global passa a ser "`Bearer` **ou** `OAuth2`".
- `UseSwaggerUI` recebe `OAuthClientId(ClientId)`, `OAuthUsePkce()` e
  `OAuthScopes("openid")`.
- O `redirect_uri` é `<url do swagger>/oauth2-redirect.html` (página servida pelo próprio
  Swashbuckle, calculada no navegador a partir da URL atual - portanto correta também atrás do
  prefixo `/code-brain` do ingress).
- O token obtido é o mesmo tipo de access token do realm, validado pelo mesmo `JwtBearer`. Se
  `Audience` estiver configurada, o client do Swagger precisa de um *audience mapper* que a inclua,
  senão o token dele leva `401`.

### Pré-requisitos no Keycloak (quem faz o deploy)

Client no realm com: **Client authentication = off** (público), **Standard flow = on**,
**PKCE Code Challenge Method = S256**, **Valid redirect URIs** =
`https://blogdoft.home.arpa/code-brain/api/indexer/swagger/oauth2-redirect.html` (e o equivalente
local, p. ex. `http://localhost:5223/api/indexer/swagger/oauth2-redirect.html`) e **Web origins**
com a origem do Swagger (o Swagger UI troca o `code` pelo token com um `fetch` do navegador ao
`tokenUrl`, sujeito a CORS).

### Implementação

- `KeycloakOptions.ClientId`.
- `KeycloakAuthenticationExtensions.AddKeycloakAuthentication` também registra o `KeycloakOptions`
  como singleton, para o transformer do OpenAPI recebê-lo por injeção.
- `BearerSecurityDocumentTransformer` (renomeado para `KeycloakSecurityDocumentTransformer`, já que
  deixa de ser só `Bearer`) acrescenta o esquema `OAuth2` e o segundo requisito quando
  `ClientId` está preenchido.
- `Program.cs`: `UseSwaggerUI` configura `OAuthClientId`/`OAuthUsePkce`/`OAuthScopes` só nesse caso.
- `README.md` e o bloco comentado do `configmap.yaml` documentam a chave e os pré-requisitos.

### Fora de escopo

- Hostname público de login diferente do `Authority` usado pelo servidor (o mesmo motivo de
  `ValidIssuer` separado, acima).
- Client secret / fluxos confidenciais no Swagger UI.

### Verificação

- `KeycloakOptionsTests`: `ClientId` preenchido com a autenticação ligada → aparado e exposto.
- `KeycloakSecurityDocumentTransformerTests`: sem `ClientId`, só `Bearer` (como antes); com
  ele, também `OAuth2` com `authorizationCode`, URLs derivados do `Authority` (inclusive com `/`
  final) e escopo `openid`, e dois requisitos alternativos.
- Conferência manual contra um **Keycloak real** (container, `start-dev`, realm importado com um client
  público `swagger` com PKCE S256 e um usuário de teste), com a API em `Keycloak__ClientId=swagger`:
  - o documento OpenAPI traz o esquema `OAuth2` com `authorizationUrl`/`tokenUrl` idênticos aos
    `authorization_endpoint`/`token_endpoint` do discovery document do Keycloak, e
    `security = [{Bearer}, {OAuth2: [openid]}]`;
  - a página do Swagger UI recebe `clientId`, `scopes: ["openid"]` e
    `usePkceWithAuthorizationCodeGrant: true` (`initOAuth`), e `oauth2-redirect.html` é servida;
  - o fluxo que o Authorize dispara, reproduzido por script (tela de login do Keycloak → login →
    redirect para `oauth2-redirect.html?code=...` → troca do `code` com `code_verifier`, sem
    secret): o token emitido (`azp=swagger`) dá `200` em `GET /api/indexer/projects`, e a mesma
    chamada sem token dá `401`.
  - **Não verificado**: o clique no botão **Authorize** dentro de um navegador (a extensão do
    Chrome não estava conectada). O que fica sem cobrir é só o JavaScript do próprio Swagger UI,
    alimentado pela configuração acima.

## Atualização - Chave explícita para ligar/desligar

**Status: concluído.** Antes, a autenticação era **deduzida**: `Keycloak:Authority` preenchida ligava,
vazia desligava, e `Audience`/`ClientId` sem `Authority` eram um erro de startup (para não abrir
a API por um `Authority` esquecido). Agora é uma chave própria, `Keycloak:Enabled`.

### Mudança de regra

| | Antes | Agora |
|---|---|---|
| O que liga a autenticação | `Authority` não vazia | `Enabled = true` |
| Padrão | desligada (`Authority` vazia) | desligada (`Enabled = false`) |
| `Authority` vazia com autenticação ligada | (não existia: era "desligada") | erro de startup |
| `Audience`/`ClientId` sem `Authority` | erro de startup | irrelevante: ignorados se `Enabled = false`; com `Enabled = true`, o erro é a `Authority` vazia |
| Chaves do Keycloak preenchidas com a autenticação desligada | ligavam a autenticação | ignoradas (a API fica aberta) |

A regra antiga de "configuração parcial" deixa de existir: ela existia justamente porque não havia
uma chave explícita. O risco que ela cobria (abrir a API por engano) agora é do próprio
`Enabled`: quem deixa `false`/ausente está pedindo a API aberta. O caso perigoso restante é o
inverso do que a regra antiga cobria - preencher `Authority`, esquecer o `Enabled` e achar que a
API está protegida - e por isso o README e o comentário do `configmap.yaml` dizem isso em destaque.

### Por que a chave explícita

Ligar/desligar por *presença* de outras configurações obriga a apagar `Authority` (e `Audience`,
`ClientId`) para desligar, perdendo a configuração, e faz com que preencher uma delas por
outro motivo mude o comportamento de segurança da API. Com `Enabled`, a configuração do realm pode
ficar registrada no ConfigMap e a autenticação ser ligada/desligada por uma única variável
(`Keycloak__Enabled`).

### Implementação

- `KeycloakOptions.Enabled` (bool, default `false`); `FromConfiguration` devolve `null` quando
  desligada, **sem validar** as demais chaves, e, ligada, exige `Authority` (mesmas validações de
  URL/HTTPS de antes). Nada muda no restante: `Program.cs`, `AddKeycloakAuthentication`, o
  transformer do OpenAPI e a configuração do Swagger UI continuam dependendo apenas de
  `keycloakOptions is not null`.
- `appsettings.json`: `Keycloak:Enabled = false`. `README.md` e o bloco comentado do
  `.eng/k8s/configmap.yaml` documentam a chave.

### Verificação

- `KeycloakOptionsTests` conforme descrito em "Verificação" acima (casos de `Enabled`).
- Conferência manual contra a API real: com `Keycloak__Authority`/`Audience`/`ClientId`
  preenchidos e **sem** `Keycloak__Enabled` → tudo aberto (200 sem token); com
  `Keycloak__Enabled=true` → 401 sem token e 200 com token válido; `Keycloak__Enabled=true` sem
  `Authority` → exit code 1 com o `Critical` logado.
- `dotnet format` → `dotnet build` (zero warnings) → `dotnet test` verdes.
