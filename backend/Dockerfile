FROM mcr.microsoft.com/dotnet/sdk:9.0

WORKDIR /src

COPY . .
RUN dotnet restore FinMate.sln

ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "run", "--project", "FinMate.API", "--no-launch-profile"]
