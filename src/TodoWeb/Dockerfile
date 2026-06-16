FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /src
COPY ["src/TodoWeb/TodoWeb/TodoWeb.csproj", "src/TodoWeb/TodoWeb/"]
COPY ["src/TodoWeb/TodoWeb.Client/TodoWeb.Client.csproj", "src/TodoWeb/TodoWeb.Client/"]
RUN dotnet restore "src/TodoWeb/TodoWeb/TodoWeb.csproj"
COPY . .
WORKDIR /src/src/TodoWeb/TodoWeb
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "TodoWeb.dll"]
