ARG SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0
ARG RUNTIME_IMAGE=mcr.microsoft.com/dotnet/aspnet:10.0

FROM ${SDK_IMAGE} AS build
ARG VERSION=0.0.0
ENV HUSKY=0
WORKDIR /src

COPY .editorconfig .editorconfig
COPY Directory.Build.props stylecop.json ./
COPY code-ciir-indexer.slnx ./
COPY src/Ciir.Indexer.Api/Ciir.Indexer.Api.csproj src/Ciir.Indexer.Api/
COPY src/Ciir.Indexer.Application/Ciir.Indexer.Application.csproj src/Ciir.Indexer.Application/
COPY src/Ciir.Indexer.Core/Ciir.Indexer.Core.csproj src/Ciir.Indexer.Core/
COPY src/Ciir.Indexer.Infrastructure.Embeddings.Abstractions/Ciir.Indexer.Infrastructure.Embeddings.Abstractions.csproj src/Ciir.Indexer.Infrastructure.Embeddings.Abstractions/
COPY src/Ciir.Indexer.Infrastructure.Embeddings.Ollama/Ciir.Indexer.Infrastructure.Embeddings.Ollama.csproj src/Ciir.Indexer.Infrastructure.Embeddings.Ollama/
COPY src/Ciir.Indexer.Infrastructure.Embeddings.OpenAI/Ciir.Indexer.Infrastructure.Embeddings.OpenAI.csproj src/Ciir.Indexer.Infrastructure.Embeddings.OpenAI/
COPY src/Ciir.Indexer.Infrastructure.PostgreSql/Ciir.Indexer.Infrastructure.PostgreSql.csproj src/Ciir.Indexer.Infrastructure.PostgreSql/

RUN dotnet restore src/Ciir.Indexer.Api/Ciir.Indexer.Api.csproj

COPY src/ src/

RUN dotnet publish src/Ciir.Indexer.Api/Ciir.Indexer.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:Version=${VERSION}

FROM ${RUNTIME_IMAGE} AS final
WORKDIR /app

COPY --from=build /app/publish ./

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

USER $APP_UID
ENTRYPOINT ["dotnet", "Ciir.Indexer.Api.dll"]
