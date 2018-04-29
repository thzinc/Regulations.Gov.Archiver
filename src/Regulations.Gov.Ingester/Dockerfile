FROM microsoft/dotnet:2.0-runtime as base
ARG version
ENTRYPOINT [ "dotnet", "Regulations.Gov.Ingester.dll" ]

FROM microsoft/dotnet:2.0-sdk as build
ARG version
WORKDIR /build
COPY ./src/Regulations.Gov.Ingester/Regulations.Gov.Ingester.csproj ./src/Regulations.Gov.Ingester/
COPY ./src/Google/Google.csproj ./src/Google/
RUN dotnet new sln && \
    dotnet sln add ./src/Regulations.Gov.Ingester/Regulations.Gov.Ingester.csproj && \
    dotnet sln add ./src/Google/Google.csproj && \
    dotnet restore
COPY ./src/Regulations.Gov.Ingester/ ./src/Regulations.Gov.Ingester/
COPY ./src/Google/ ./src/Google/
RUN dotnet publish -c Release -o /app /p:Version=${version}

FROM base as final
WORKDIR /app
COPY --from=build /app .