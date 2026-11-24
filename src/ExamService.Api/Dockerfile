FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/ExamService.Domain/*.csproj src/ExamService.Domain/
COPY src/ExamService.Application/*.csproj src/ExamService.Application/
COPY src/CoachingPlatform.Shared/*.csproj src/CoachingPlatform.Shared/
COPY src/ExamService.Infrastructure/*.csproj src/ExamService.Infrastructure/
COPY src/ExamService.Api/*.csproj src/ExamService.Api/
RUN dotnet restore src/ExamService.Api/ExamService.Api.csproj
COPY src/ ./src/
RUN dotnet publish src/ExamService.Api/ExamService.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "ExamService.Api.dll"]
