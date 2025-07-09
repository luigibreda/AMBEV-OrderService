# Use a imagem base do SDK do .NET para construir a aplicação
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
# Copia o arquivo de projeto e restaura as dependências
COPY ["AMBEV-OrderService.csproj", "./"]
RUN dotnet restore "AMBEV-OrderService.csproj"
# Copia todo o código-fonte
COPY . .
# Publica a aplicação em modo Release
RUN dotnet publish "AMBEV-OrderService.csproj" -c Release -o /app/publish

# Use a imagem base do ASP.NET para executar a aplicação
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
# Expõe a porta que sua aplicação ASP.NET Core usa (geralmente 80 para HTTP e 443 para HTTPS)
EXPOSE 80
EXPOSE 443
# Define a variável de ambiente para que a aplicação ouça em todas as interfaces
ENV ASPNETCORE_URLS=http://+:80
# Define o ponto de entrada da sua aplicação
ENTRYPOINT ["dotnet", "AMBEV-OrderService.dll"]