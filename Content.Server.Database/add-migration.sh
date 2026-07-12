#!/usr/bin/env bash

if [ -z "$1" ] ; then
    echo "Must specify migration name"
    exit 1
fi

dotnet dotnet-ef migrations add --context SqliteServerDbContext -o Migrations/Sqlite "$1"
dotnet dotnet-ef migrations add --context PostgresServerDbContext -o Migrations/Postgres "$1"
