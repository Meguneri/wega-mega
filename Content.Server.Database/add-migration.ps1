#!/usr/bin/env pwsh

param([String]$name)

if ($name -eq "")
{
    Write-Error "must specify migration name"
    exit
}

dotnet dotnet-ef migrations add --context SqliteServerDbContext -o Migrations/Sqlite $name
dotnet dotnet-ef migrations add --context PostgresServerDbContext -o Migrations/Postgres $name
