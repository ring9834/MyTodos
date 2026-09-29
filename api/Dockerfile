FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src
COPY *.slnx .

# Copy ALL project files first (for restore layer caching)
COPY src/Todo.Api/*.csproj src/Todo.Api/
COPY src/Todo.Application/*.csproj src/Todo.Application/
COPY src/Todo.Domain/*.csproj src/Todo.Domain/
COPY src/Todo.Infrastructure/*.csproj src/Todo.Infrastructure/

# Restore (this layer is cached if csproj files don't change)
RUN dotnet restore src/Todo.Api/Todo.Api.csproj

# Copy ALL source code
COPY src/Todo.Api/ src/Todo.Api/
COPY src/Todo.Application/ src/Todo.Application/
COPY src/Todo.Domain/ src/Todo.Domain/
COPY src/Todo.Infrastructure/ src/Todo.Infrastructure/

RUN dotnet publish src/Todo.Api/Todo.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
RUN adduser -D -u 10001 appuser
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
USER 10001
EXPOSE 8080
ENTRYPOINT ["dotnet", "Todo.Api.dll"]