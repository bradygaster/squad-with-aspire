FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/TodoApi/TodoApi.csproj src/TodoApi/
RUN dotnet restore src/TodoApi/TodoApi.csproj

COPY src/TodoApi/ src/TodoApi/
RUN dotnet publish src/TodoApi/TodoApi.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "TodoApi.dll"]
