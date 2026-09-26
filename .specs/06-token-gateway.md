# Gateway de token (`POST /api/indexer/auth/token`)

## Contexto

Clientes não interativos (a pipeline do `ciir --send`, em `code-csharp-ciir`) precisam de um access
token do realm para chamar esta API quando `Keycloak:Enabled = true`. Hoje eles teriam de conhecer
o Keycloak (URL do realm, caminho do token endpoint, formato do grant). Esta spec faz **o indexer
ser o gateway**: o cliente troca `clientId` + `clientSecret` por um token **só conversando com o
indexer**, sem saber como (nem onde) o token é emitido.

Escopo: **apenas** o grant `client_credentials`. O endpoint não é um proxy genérico do Keycloak:
não aceita `grant_type`, `scope` nem qualquer outro parâmetro do cliente.

## Contrato

`POST /api/indexer/auth/token` — **anônimo** (é o que produz o token; ver "Exceções").

Corpo (`application/json`):

```json
{ "clientId": "ciir-pipeline", "clientSecret": "..." }
```

| Status | Quando | Corpo |
|---|---|---|
| `200` | Token emitido | `{ "accessToken": "<jwt>", "tokenType": "Bearer", "expiresIn": 300 }` (`expiresIn` em segundos; omitido se o Keycloak não informar) |
| `400` | `clientId` ou `clientSecret` ausente/em branco | `application/problem+json` |
| `401` | O Keycloak recusou as credenciais (client inexistente, secret errada, client sem *Service accounts*) | `application/problem+json`, `detail` genérico (não repassa a resposta do Keycloak) |
| `404` | Autenticação desligada (`Keycloak:Enabled = false`): não há token a emitir | sem corpo |
| `502` | O Keycloak está inacessível, deu timeout ou respondeu algo inválido | `application/problem+json` |

O `accessToken` é um token comum do realm: vale exatamente como um obtido direto do Keycloak
(`Authorization: Bearer <jwt>`), validado pelo mesmo `JwtBearer`.

## Comportamento

- O indexer faz `POST {Keycloak:Authority}/protocol/openid-connect/token` com
  `grant_type=client_credentials`, `client_id`, `client_secret` (form). Sem `scope`.
- O token endpoint é derivado de **`Authority`** (a URL pública), **não** de `MetadataAddress`:
  o Keycloak calcula o `iss` do token a partir do host da requisição; pedir o token pelo host
  interno emitiria um `iss` que o `JwtBearer` (que valida contra `Authority`) recusaria.
  Com `Keycloak:SkipCertificateValidation = true`, essa chamada também ignora erros de
  certificado, como o backchannel do `JwtBearer` (o mesmo host público já precisa ser alcançável
  para o JWKS).
- Timeout de 15 s na chamada ao Keycloak.
- Mapeamento: Keycloak `2xx` com `access_token` → `200`; `400`/`401` → `401`; qualquer outra
  resposta, falha de rede, timeout ou corpo sem `access_token` → `502`.
- **A secret nunca é registrada em log nem devolvida em erro.** Log de sucesso/falha registra só
  `clientId` e o status do Keycloak.
- Sem `Audience`/roles: continua valendo "qualquer token válido do realm" (spec 05). Se
  `Audience` estiver configurada, o client usado precisa do *audience mapper*, como antes.

## Exceções (permanece anônimo)

Acrescenta-se a `GET /health` e ao OpenAPI/Swagger: `POST /api/indexer/auth/token` — `[AllowAnonymous]`
explícito no *action*, pois a `FallbackPolicy` (spec 05) exigiria token para obter um token.

## Implementação

Preocupação da borda HTTP: vive só em `Ciir.Indexer.Api` (`Core`/`Application` não mudam).

- `Authentication/IKeycloakTokenClient.cs`, `KeycloakToken.cs`, `KeycloakTokenClient.cs` — cliente
  tipado (`HttpClient`) que fala com o Keycloak e devolve `Result<KeycloakToken>` com `Failure`
  codificado pelo status HTTP (`401-invalid-client`, `502-token-endpoint-unavailable`), no padrão do
  `FailureResults`.
- `Authentication/KeycloakTokenGatewayExtensions.cs` — `AddKeycloakTokenGateway(KeycloakOptions)`
  registra o cliente tipado (timeout, handler que ignora certificado quando configurado).
  Chamado em `Program.cs` **só** com a autenticação ligada; desligada, o `IKeycloakTokenClient` não
  existe e o controller responde `404`.
- `Controllers/AuthController.cs` (`api/indexer/auth`) e `Contracts/TokenRequest.cs`,
  `Contracts/TokenResponse.cs`.

## Fora de escopo

- Rate limiting/proteção contra tentativa e erro de secrets neste endpoint (o Keycloak não bloqueia
  força bruta de client secrets). Como o endpoint é anônimo, vale tratar na borda (ingress) ou numa
  spec própria.
- Outros grants (`password`, `refresh_token`), `scope`, cache de token, revogação.
- Hostname interno para o token endpoint (mesmo motivo de `ValidIssuer` separado, spec 05).

## Verificação

Testes em `Ciir.Indexer.Api.Tests`:

- `KeycloakTokenClientTests` (`HttpMessageHandler` falso): monta a URL a partir de `Authority`
  (com/sem `/` final) e o form `client_credentials`; `200` com `access_token` → token
  (`tokenType` padrão `Bearer`, `expiresIn` opcional); `400`/`401` → `401-invalid-client`;
  `500`, exceção de rede, timeout e corpo sem `access_token`/inválido → `502-…`; a secret não
  aparece em nenhuma mensagem de falha.
- `AuthControllerTests`: `clientId`/`clientSecret` ausente ou em branco → `400` sem chamar o
  Keycloak; sucesso → `200` com o corpo mapeado; falha `401`/`502` mapeada; cliente ausente
  (autenticação desligada) → `404`.
- `AuthControllerAnonymousAccessTests` (host com `Authority` e `FallbackPolicy`): a rota responde
  sem token, enquanto outra rota sem `AllowAnonymous` continua `401`.
- `dotnet format` → `dotnet build` (zero warnings) → `dotnet test` verdes.
