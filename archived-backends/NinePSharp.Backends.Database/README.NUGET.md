# NinePSharp.Backends.Database

Database backend plugin for exposing SQL/NoSQL data through 9P.

## Supported providers in this package
- SQL: SQLite, SQL Server, PostgreSQL, MySQL.
- NoSQL/Cloud DB: DynamoDB, Firestore, Cosmos DB.

## Install
```bash
dotnet add package NinePSharp.Backends.Database
```

Use with `NinePSharp` + `NinePSharp.Server.Abstractions` in host/server composition.
