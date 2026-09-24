# Imagem da API. Runtime chiseled: sem shell, sem gerenciador de pacotes, usuário não-root por padrão.
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props Banking.slnx ./
COPY src/ src/
RUN dotnet publish src/Banking.Api --configuration Release --output /app -p:UseAppHost=false

# "-extra" traz tzdata e ICU: o calendário contábil usa o fuso America/Sao_Paulo.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Banking.Api.dll"]
