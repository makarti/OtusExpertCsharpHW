FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props .
COPY Parser.SourceGenerators/Parser.SourceGenerators.csproj Parser.SourceGenerators/
COPY Parser/Parser.csproj Parser/
RUN dotnet restore Parser/Parser.csproj

COPY Parser.SourceGenerators/ Parser.SourceGenerators/
COPY Parser/ Parser/
WORKDIR /src/Parser
RUN dotnet publish Parser.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

ENV PARSER_HOST=0.0.0.0
ENV PARSER_PORT=8080
ENV OTEL_EXPORTER_OTLP_ENDPOINT=http://otel-collector:4317

EXPOSE 8080

ENTRYPOINT ["dotnet", "Parser.dll"]
