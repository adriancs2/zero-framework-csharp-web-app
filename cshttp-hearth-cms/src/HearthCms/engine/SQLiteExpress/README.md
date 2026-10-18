# SQLiteExpress

[![C#](https://img.shields.io/badge/C%23-7.3%2B-239120?logo=csharp&logoColor=white)](https://learn.microsoft.com/en-us/dotnet/csharp/)
[![.NET Framework](https://img.shields.io/badge/.NET%20Framework-4.6%2B-512BD4?logo=.net&logoColor=white)](https://dotnet.microsoft.com/download/dotnet-framework)
[![.NET Standard](https://img.shields.io/badge/.NET%20Standard-2.0%2B-512BD4?logo=.net&logoColor=white)](https://learn.microsoft.com/en-us/dotnet/standard/net-standard)
[![.NET](https://img.shields.io/badge/.NET-6%20%7C%208%20%7C%209%2B-512BD4?logo=.net&logoColor=white)](https://dotnet.microsoft.com/)
[![SQLite](https://img.shields.io/badge/SQLite-3.24%2B-003B57?logo=sqlite&logoColor=white)](https://www.sqlite.org/)
[![Single-File](https://img.shields.io/badge/deploy-single--file-brightgreen)](#install)
[![License](https://img.shields.io/badge/license-Public%20Domain-lightgrey)](#license)

A lightweight, single-file C# class library for SQLite - designed for developers who want to stay close to SQL while removing the tedium around it.

SQLiteExpress mirrors the API of [MySqlExpress](https://github.com/adriancs2/MySqlExpress), giving you a consistent database interface across both MySQL and SQLite projects.

---

## Contents

- [The Design](#the-design)
- [Install](#install)
- [Quick Start](#quick-start)
- [Select](#select)
- [Execute](#execute)
- [ExecuteScalar](#executescalar)
- [Insert](#insert)
- [Update](#update)
- [InsertOrReplace](#insertorreplace)
- [InsertUpdate (Upsert)](#insertupdate-upsert)
- [Save / SaveList](#save--savelist)
- [GetObject / GetObjectList](#getobject--getobjectlist)
- [Class Field Binding Modes](#class-field-binding-modes)
- [Transactions](#transactions)
- [String Helpers](#string-helpers)
- [Table Operations (DDL)](#table-operations-ddl)
- [Attach / Detach Databases](#attach--detach-databases)
- [DB Info](#db-info)
- [Code Generation](#code-generation)
- [Supported Data Types](#supported-data-types)
- [Relationship with SQLiteHelper](#relationship-with-sqlitehelper)
- [License](#license)

---

## The Design

No ORM. No migrations. No DbContext. No LINQ-to-SQL translation layer.

You write SQL. SQLiteExpress handles the plumbing: parameterization, object binding, type conversion, CRUD generation. So you don't have to repeat yourself.

The idea is simple: wrap a raw `SQLiteCommand` and give it superpowers.

```csharp
using System.Data.SQLite;

using (SQLiteConnection conn = new SQLiteConnection(connString))
{
    conn.Open();
    using (SQLiteCommand cmd = conn.CreateCommand())
    {
        SQLiteExpress s = new SQLiteExpress(cmd);

        // You're ready. That's it.
    }
}
```

> Throughout this README, `s` is the `SQLiteExpress` instance.

---

## Install

SQLiteExpress is a set of single `.cs` files. There is no NuGet package to install for SQLiteExpress itself - you drop the source into your project.

**Step 1.** Install the `System.Data.SQLite` dependency:

| Target                  | NuGet Package                                                                                                      |
| ----------------------- | ------------------------------------------------------------------------------------------------------------------ |
| .NET Framework          | [Stub.System.Data.SQLite.Core.NetFramework](https://www.nuget.org/packages/Stub.System.Data.SQLite.Core.NetFramework) |
| .NET Standard / .NET 6+ | [Stub.System.Data.SQLite.Core.NetStandard](https://www.nuget.org/packages/Stub.System.Data.SQLite.Core.NetStandard)  |

**Step 2.** Copy these files into your project:

- `SQLiteExpress.cs` *(required)*
- `SQLiteTable.cs`, `SQLiteColumn.cs`, `SQLiteColumnList.cs`, `SQLiteEnums.cs` *(required for DDL and code generation)*

All files live in the `System.Data.SQLite` namespace, so any file that already has `using System.Data.SQLite;` picks them up automatically.

---

## Quick Start

```csharp
using System;
using System.Collections.Generic;
using System.Data.SQLite;

string connStr = "Data Source=demo.db;Version=3;";

using (SQLiteConnection conn = new SQLiteConnection(connStr))
{
    conn.Open();
    using (SQLiteCommand cmd = conn.CreateCommand())
    {
        SQLiteExpress s = new SQLiteExpress(cmd);

        // Create a table.
        SQLiteTable t = new SQLiteTable("player");
        t.Columns.Add(new SQLiteColumn("id", autoIncrement: true));
        t.Columns.Add(new SQLiteColumn("name", ColType.Text));
        t.Columns.Add(new SQLiteColumn("score", ColType.Decimal));
        s.CreateTable(t);

        // Insert a row.
        var dic = new Dictionary<string, object>
        {
            { "name",  "John Smith" },
            { "score", 99.5m }
        };
        s.Insert("player", dic);

        // Read it back.
        int count = s.ExecuteScalar<int>("select count(*) from player;");
        Console.WriteLine($"Rows: {count}");
    }
}
```

---

## Select

`Select` returns a `DataTable`.

```csharp
// All rows
DataTable dt = s.Select("select * from player;");

// With a dictionary of parameters
var dicParam = new Dictionary<string, object>
{
    { "@vid", 1 }
};
DataTable dt2 = s.Select("select * from player where id = @vid;", dicParam);

// With an explicit list of SQLiteParameter
var plist = new List<SQLiteParameter>
{
    new SQLiteParameter("@name", "John")
};
DataTable dt3 = s.Select("select * from player where name = @name;", plist);
```

---

## Execute

`Execute` runs any non-query statement (DDL, hand-written `INSERT/UPDATE/DELETE`, pragmas, etc.).

```csharp
s.Execute("create index if not exists idx_player_name on player(name);");

var dicParam = new Dictionary<string, object>
{
    { "@vid", 5 }
};
s.Execute("delete from player where id = @vid;", dicParam);
```

---

## ExecuteScalar

Returns a single value. The generic form converts for you.

```csharp
int count     = s.ExecuteScalar<int>("select count(*) from player;");
string name   = s.ExecuteScalar<string>("select name from player where id = 1;");
decimal total = s.ExecuteScalar<decimal>("select sum(score) from player;");
DateTime when = s.ExecuteScalar<DateTime>("select date_register from player where id = 1;");

// Non-generic returns object
object raw = s.ExecuteScalar("select count(*) from player;");

// With parameters
var dicParam = new Dictionary<string, object>
{
    { "@n", "alice" }
};
long age = s.ExecuteScalar<long>("select age from player where name = @n;", dicParam);
```

---

## Insert

`Insert` takes a table name and a `Dictionary<string, object>` of column → value.

```csharp
var dic = new Dictionary<string, object>
{
    { "code",          "P001" },
    { "name",          "John Smith" },
    { "date_register", DateTime.Now },
    { "tel",           "0123456789" },
    { "email",         "john@mail.com" },
    { "status",        1 }
};

s.Insert("player", dic);

long newId = s.LastInsertId; // SELECT last_insert_rowid()
```

---

## Update

The default `Update` overloads append `LIMIT 1` for safety. Pass `updateSingleRow: false` to remove that cap.

```csharp
// Single-column condition (updates one matching row)
var data = new Dictionary<string, object>
{
    { "name", "John Smith Updated" },
    { "tel",  "0999888777" }
};
s.Update("player", data, "id", 1);
```

```csharp
// Same payload, update every matching row
s.Update("player", data, "status", 1, updateSingleRow: false);
```

```csharp
// Multi-column condition
var data = new Dictionary<string, object>
{
    { "name", "John Smith Updated" }
};

var cond = new Dictionary<string, object>
{
    { "status", 1 },
    { "tel",    "0123456789" }
};

s.Update("player", data, cond);
```

```csharp
// Multi-column condition, no LIMIT 1
s.Update("player", data, cond, updateSingleRow: false);
```

---

## InsertOrReplace

Replaces the **entire row** on primary key conflict. Uses SQLite's `INSERT OR REPLACE`.

```csharp
var dic = new Dictionary<string, object>
{
    { "id",    1 },
    { "name",  "John" },
    { "score", 100 }
};

s.InsertOrReplace("player", dic);
```

---

## InsertUpdate (Upsert)

Updates **only specific columns** on conflict. Uses SQLite's `ON CONFLICT DO UPDATE` (requires SQLite 3.24+). Primary key columns are auto-detected from `pragma table_info`.

```csharp
var dic = new Dictionary<string, object>
{
    { "group_id",  1 },
    { "member_id", 1 },
    { "amount",    100 },
    { "level",     1 }
};

var lstUpdateCol = new List<string> { "amount", "level" };

s.InsertUpdate("group_member", dic, lstUpdateCol);
```

Include / exclude mode:

```csharp
var lstCols = new List<string> { "amount", "level" };

// include = true: update ONLY amount and level on conflict
s.InsertUpdate("group_member", dic, lstCols, include: true);

// include = false: update everything EXCEPT amount and level
s.InsertUpdate("group_member", dic, lstCols, include: false);
```

---

## Save / SaveList

Reflection-based wrappers that map a class object to a `Dictionary<string, object>` and call `InsertOrReplace`. Field and property names must match the column names.

```csharp
obPlayer player = new obPlayer();
player.code = "P001";
player.name = "John Smith";

s.Save("player", player);

// Bulk
var lst = new List<obPlayer> { player1, player2, player3 };
s.SaveList("player", lst);
```

> `Save` / `SaveList` use `INSERT OR REPLACE` semantics. If you need partial-column upsert, use [`InsertUpdate`](#insertupdate-upsert) instead.

---

## GetObject / GetObjectList

Bind a single row to an object, or a result set to a `List<T>`. Column names are matched against both fields and properties.

```csharp
// Single row
obPlayer p = s.GetObject<obPlayer>("select * from player where id = 1;");

// With parameters
var dicParam = new Dictionary<string, object>
{
    { "@vid", 1 }
};
obPlayer p2 = s.GetObject<obPlayer>("select * from player where id = @vid;", dicParam);

// Into an existing instance
obPlayer p3 = new obPlayer();
s.GetObject("select * from player where id = 1;", p3);

// List
List<obPlayer> lst = s.GetObjectList<obPlayer>("select * from player;");

// List with LIKE filter
var dicLike = new Dictionary<string, object>
{
    { "@vname", "%adam%" }
};
List<obPlayer> matches = s.GetObjectList<obPlayer>(
    "select * from player where name like @vname;", dicLike);
```

---

## Class Field Binding Modes

SQLiteExpress supports three mapping styles for POCOs. Pick whichever suits your codebase.

**Mode 1 - Private fields + public properties** *(recommended)*

Private field names match column names exactly. Public properties follow C# conventions. SQLiteExpress binds to the private fields.

```csharp
public class obPlayer
{
    int id = 0;
    string name = "";
    DateTime date_register = DateTime.MinValue;

    public int Id { get { return id; } set { id = value; } }
    public string Name { get { return name; } set { name = value; } }
    public DateTime DateRegister { get { return date_register; } set { date_register = value; } }
}
```

**Mode 2 - Public properties only**

Property names must match column names exactly.

```csharp
public class obPlayer
{
    public int id { get; set; }
    public string name { get; set; }
    public DateTime date_register { get; set; }
}
```

**Mode 3 - Public fields only**

Field names must match column names exactly.

```csharp
public class obPlayer
{
    public int id = 0;
    public string name = "";
    public DateTime date_register = DateTime.MinValue;
}
```

---

## Transactions

```csharp
try
{
    s.BeginTransaction();

    s.Insert("player", dic1);
    s.Insert("player", dic2);
    s.Update("player", data, "id", 1);

    s.Commit();
}
catch
{
    s.Rollback();
    throw;
}
```

---

## String Helpers

### Escape

Doubles single quotes so a string is safe to inline into SQL.

```csharp
string safe = s.Escape("Jane O'Brien"); // "Jane O''Brien"
```

### GetLikeString

Wraps each whitespace-separated token with `%`.

```csharp
string like    = s.GetLikeString("John Smith");           // "%John%Smith%"
string likeEsc = s.GetLikeString("Jane O'Brien", true);   // "%Jane%O''Brien%"
```

### GenerateContainsString

Builds a parameterized multi-word `LIKE` condition and appends it to a `StringBuilder`.

```csharp
StringBuilder sb = new StringBuilder();
sb.Append("select * from player where 1=1");

var dicParam = new Dictionary<string, object>();
s.GenerateContainsString("name", "john smith", sb, dicParam);

// sb:
//   select * from player where 1=1 and (`name` like @csname0 and `name` like @csname1)
// dicParam:
//   { "@csname0": "%john%", "@csname1": "%smith%" }

List<obPlayer> results = s.GetObjectList<obPlayer>(sb.ToString(), dicParam);
```

---

## Table Operations (DDL)

### CreateTable

```csharp
SQLiteTable table = new SQLiteTable("player");
table.Columns.Add(new SQLiteColumn("id", autoIncrement: true));         // integer primary key autoincrement
table.Columns.Add(new SQLiteColumn("code", ColType.Text));
table.Columns.Add(new SQLiteColumn("name", ColType.Text));
table.Columns.Add(new SQLiteColumn("score", ColType.Decimal));
table.Columns.Add(new SQLiteColumn(
    colName:       "created_at",
    colDataType:   ColType.DateTime,
    primaryKey:    false,
    autoIncrement: false,
    notNull:       true,
    defaultValue:  "2026-01-01 00:00:00"));

s.CreateTable(table);
```

Supported column types (`ColType`): `Text`, `DateTime`, `Integer`, `Decimal`, `BLOB`.

### Other operations

```csharp
s.DropTable("old_table");
s.RenameTable("old_name", "new_name");
s.CopyAllData("source_table", "dest_table");
s.UpdateTableStructure("player", newStructure); // safe schema migration
```

`UpdateTableStructure` rebuilds the target table under a temp name, copies all matching-column data across, drops the original, and renames the temp - a common SQLite idiom for `ALTER TABLE` limitations.

---

## Attach / Detach Databases

```csharp
s.AttachDatabase("other.db", "otherdb");

DataTable dt = s.Select("select * from otherdb.some_table;");

s.DetachDatabase("otherdb");
```

---

## DB Info

```csharp
DataTable status    = s.GetTableStatus();          // full sqlite_master
List<string> tables = s.GetTableList();            // table names (excludes sqlite_sequence)
DataTable cols      = s.GetColumnStatus("player"); // pragma table_info(player)
DataTable dbs       = s.ShowDatabase();            // pragma database_list
```

---

## Code Generation

Connect to a database and let SQLiteExpress write boilerplate for you. `FieldsOutputType` is a top-level enum in `System.Data.SQLite`.

```csharp
// Generate class fields from a table
string code = s.GenerateTableClassFields(
    "player", FieldsOutputType.PrivateFielsPublicProperties);

// Generate from a custom SELECT (joins, aliases, projections)
string joinCode = s.GenerateCustomClassField(
    "select a.*, b.year from player a inner join player_team b on a.id = b.player_id;",
    FieldsOutputType.PublicProperties);

// Dictionary template for Insert
string dicCode = s.GenerateTableDictionaryEntries("player");
// Dictionary<string, object> dic = new Dictionary<string, object>();
//
//             dic["id"] =
//             dic["code"] =
//             dic["name"] =
//             ...

// Parameter dictionary template
string paramCode = s.GenerateParameterDictionaryTable("player");

// Update column list (all non-PK columns) - feeds straight into InsertUpdate
string updateCols = s.GenerateUpdateColumnList("player_team");

// The CREATE TABLE SQL that SQLite stored
string createSql = s.GetCreateTableSql("player");
```

`FieldsOutputType` values:

| Value                          | Output                                     |
| ------------------------------ | ------------------------------------------ |
| `PrivateFielsPublicProperties` | Private backing fields + public properties |
| `PublicProperties`             | Public auto-properties only                |
| `PublicFields`                 | Public fields only                         |

---

## Supported Data Types

SQLiteExpress handles automatic conversion for:

`string`, `bool`, `byte`, `sbyte`, `short`, `ushort`, `int`, `uint`, `long`, `ulong`, `float`, `double`, `decimal`, `char`, `DateTime`, `byte[]`, `Guid`, `TimeSpan`

`null` and `DBNull` values convert to the type's default (`""`, `0`, `false`, `DateTime.MinValue`, etc.), so you never get a `NullReferenceException` reading a nullable column into a non-nullable field.

---

## Relationship with SQLiteHelper

[SQLiteHelper](https://github.com/adriancs2/SQLiteHelper.net) is the original lightweight SQLite utility library. SQLiteExpress is its successor - a complete rewrite that mirrors the [MySqlExpress](https://github.com/adriancs2/MySqlExpress) API while absorbing SQLiteHelper's table-management features.

**SQLiteHelper** continues to work as-is. No breaking changes. For new projects, **SQLiteExpress** is recommended.

| Feature                                 | SQLiteHelper | SQLiteExpress         |
| --------------------------------------- | ------------ | --------------------- |
| Select / Execute / ExecuteScalar        | ✓            | ✓                     |
| GetObject\<T\> / GetObjectList\<T\>     | ✓            | ✓                     |
| GetObject into existing instance        | -            | ✓                     |
| Insert (Dictionary)                     | ✓            | ✓                     |
| Update (single / multi condition)       | ✓            | ✓ (+ LIMIT 1 default) |
| InsertOrReplace                         | -            | ✓                     |
| InsertUpdate (Upsert)                   | -            | ✓                     |
| Save / SaveList (object)                | -            | ✓                     |
| GetLikeString / GenerateContainsString  | -            | ✓                     |
| Code generation                         | -            | ✓                     |
| Table DDL (CreateTable, etc.)           | ✓            | ✓                     |
| Attach / Detach database                | ✓            | ✓                     |

---

## License

Public Domain. No attribution required. Use it, fork it, rebrand it, ship it.
