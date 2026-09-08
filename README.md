# ScriptManager
ScriptManager is a command-line tool for differential SQL script execution on a SQL Server
database. It records executed scripts in the target database and normally skips scripts that are
already present in that history.

## Usage

Run the scripts from the default `SQL` directory:

```bat
ScriptManager.exe /csName "MyCsName"
```

Run scripts from a specific directory and use a specific connection-string file:

```bat
ScriptManager.exe /csName "MyCsName" /sqlPath "../SQL" /csFile "Config/Database.config"
```

Run scripts for a particular environment and set the database version:

```bat
ScriptManager.exe /csName "MyCsName" /sqlPath "../SQL" /envCode "CLIENT-A" /csFile "Config/Database.config" /version "1.2.3.4"
```

`/csName` is required. If `/sqlPath` is omitted, ScriptManager uses the `SQL` directory relative
to its current working directory.

## Project and release layouts

The current repository contains one ScriptManager source project targeting .NET Framework 4.8:

```text
ScriptManager/
  ScriptManager.csproj
  App.config
  Config/
    Database.config
    ScriptManager.config
    Log4net.config
  ...source files...
```

Building this project produces one `ScriptManager.exe`. The repository does not include the SQL
release scripts; supply them separately through `/sqlPath`, or create an `SQL` directory in the
command's working directory to use the default path.

A deliverable can use the following generic layout:

```text
ScriptManager/
  RunScriptManager.bat
  runtime/
    ScriptManager.exe
    ScriptManager.exe.config
    Config/
      Database.config
      ScriptManager.config
      Log4net.config
  SQL/
    0000-tools/
    0100-v1.0.0/
    0110-v1.1.0/
```

The name `runtime` is not imposed by ScriptManager and may be changed. If the runtime directory is
renamed, moved, or duplicated, adapt the launcher BAT so it enters the correct directory before
starting the executable. Also update relative arguments such as `/sqlPath` and `/csFile` when the
directory depth changes.

From the `runtime` directory, the command has this general form:

```bat
ScriptManager.exe /csName "MyCsName" /csFile "Config/Database.config" /sqlPath "../SQL" /version "1.2.3.4"
```

## SQL script location and execution order

Place SQL scripts in the directory passed with `/sqlPath`, or in the default `SQL` directory.
Only files whose names end in `.sql` are selected. Subdirectories are scanned recursively.

For each directory, ScriptManager executes:

1. The eligible `.sql` files directly inside that directory, sorted by their full path.
2. Each subdirectory, sorted by its path, using the same recursive rules.

If the root `SQL` directory contains only version directories and no SQL files, ScriptManager
enters those directories in path order, then executes their files in path order. If SQL files are
placed directly in the root, those root files run before any version directory.

Numeric filename prefixes are recommended when the execution order matters. For example:

```text
SQL/
  001-create-database-objects.sql
  002-insert-reference-data.sql
  tables/
    001-create-users.sql
  views/
    001-create-reports.sql
```

The execution order is:

```text
SQL/001-create-database-objects.sql
SQL/002-insert-reference-data.sql
SQL/tables/001-create-users.sql
SQL/views/001-create-reports.sql
```

## Differential execution and history

By default, ScriptManager uses the following table in the target database:

```text
dbo.HistoriqueScriptSql
```

The table is created automatically when it does not exist. It stores the execution date, script
path and any execution error. Before running the selected files, ScriptManager reads this table
and skips paths that have already been recorded.

Execution attempts are recorded even when the SQL script fails. Consequently, a failed script is
also considered previously executed on the next run. Review or remove its history row before
retrying it, or deliberately run with `/disableScriptDiff 1`.

Using `/disableScriptDiff 1` disables both the history check and history insertion. All selected
scripts are then executed on every invocation.

## Environment-specific scripts

Scripts without an environment marker are eligible in every environment. To restrict a script,
put the environment code between `=` characters in its filename:

```text
010-common.sql
020-settings=CLIENT-A=.sql
020-settings=CLIENT-B=.sql
```

With `/envCode CLIENT-A`, `010-common.sql` and `020-settings=CLIENT-A=.sql` are selected, while
`020-settings=CLIENT-B=.sql` is ignored.

Environment names can contain a more specific suffix separated by `-`. For example,
`020-settings=CLIENT=.sql` is also selected for `CLIENT-TEST`.

If `/envCode` is omitted, the environment name is read from `Config/ScriptManager.config`, relative
to the directory containing `ScriptManager.exe`:

```xml
<?xml version="1.0"?>
<appSettings>
  <add key="EnvironmentName" value="DEV" />
</appSettings>
```

## Database configuration

ScriptManager uses `/csName` to look up a connection string.

By default, connection strings are read from `Config/Database.config`, relative to the directory
containing `ScriptManager.exe`. A different file can be supplied with `/csFile`.

The file must use the following format:

```xml
<?xml version="1.0" encoding="utf-8" ?>
<connectionStrings>
  <add name="MyCsName"
       connectionString="Server=localhost;Database=MyDatabase;Integrated Security=True"
       providerName="System.Data.SqlClient" />
</connectionStrings>
```

The configured account must be allowed to execute the supplied scripts and to read, create and
write `dbo.HistoriqueScriptSql` when differential execution is enabled.

## Database version

When `/version` is supplied, ScriptManager creates or updates the database-level extended property
named `Version` after script execution. If the property already exists, it is updated only when its
current value compares lower than the supplied value. Versions use four numeric components, for
example `1.2.3.4`.

## Parameters

- `/csName connectionStringName`: connection-string name in the database configuration file;
  required.
- `/sqlPath pathToSqlDirectory`: root directory containing SQL scripts. The default is `SQL`.
- `/envCode environmentCode`: override the environment from `Config/ScriptManager.config`.
- `/csFile pathToConfigFile`: use a different connection-string file. The default is
  `Config/Database.config` next to `ScriptManager.exe`.
- `/disableScriptDiff 1`: run every selected script without reading or writing execution history.
  The default is `0`.
- `/version versionString`: create or update the database-level `Version` extended property.

SQL files must be UTF-8 encoded. If one file fails, ScriptManager logs the error and continues with
the remaining files.

# ScriptManagerModern

ScriptManagerModern is the opt-in successor to ScriptManager. It keeps the same differential SQL
execution model and command-line parameters, but is built and delivered independently so the
existing SQL Server 2012, 2014 and 2016 executables remain available unchanged.

Build `ScriptManagerModern.sln` in the `Release|Any CPU` configuration. The deployable output is
the complete contents of `ScriptManagerModern/bin/Release/net48`; copy all of it into the
livrable's `Modern` directory rather than copying only the executable.

The executable remains named `ScriptManager.exe`. The `Modern` directory and the separate BAT
entry point identify the new implementation, while source namespaces remain `ScriptManager`.

Use the new executable directly from the `Modern` directory:

```bat
ScriptManager.exe /csName "MyCsName" /sqlPath "../SQL" /csFile "Config/Database.config" /version "3.5.0.3"
```

Development and release livrables use separate Modern entry points:

- `ScriptManager.dev/ConfigVS-ScriptManagerModern.bat` uses the Visual Studio database
  configuration and does not supply `/version`.
- `ScriptManager.bin/ScriptManagerAgendisModern.bat` supplies the release version through
  `/version`; the packaging process must replace an empty version placeholder with the actual
  release version.

Existing BAT files and the legacy `2012`, `2014` and `2016` directories remain unchanged and are
the immediate fallback.

Unlike the legacy 2016 executable, Modern does not compare version labels as strings. When all
eligible SQL scripts finish successfully, the value supplied through `/version` is written
unconditionally to the database-level `Version` extended property. This supports both legacy
labels such as `3.4#56` and four-component labels such as `3.5.0.3`. If any SQL script fails,
Modern leaves the database version unchanged and returns a non-zero exit code.

Failed scripts remain retryable: Modern records the failed attempt for diagnostics but only treats
history rows without an error message as completed scripts. A later run therefore retries the
failed script instead of skipping it and incorrectly advancing the database version.

Modern requires .NET Framework 4.8 and is intended to connect to SQL Server 2012 and later. Before
making it the default for a client, validate it against that client's database and deployment
environment.

Modern has no dependency on an installed SSMS version. It executes batches with the .NET Framework
SQL client and handles standalone `GO` separators itself, so it does not ship or load SMO or the
native BatchParser component. All application dependencies are delivered beside the executable;
the host machine only needs .NET Framework 4.8.

SQL files do not need to be converted to UTF-8 with BOM. Modern detects UTF-8 with or without a
BOM and UTF-16 with a BOM; historical files that are not valid UTF-8 are read as Windows-1252.

# ScriptRunner

ScriptRunner is a command-line tool that runs SQL scripts directly on a SQL Server database.
Unlike ScriptManager, it does not perform differential execution and does not keep a history of
previously executed scripts. Every selected script is executed each time ScriptRunner is started.

## Usage

Run every `.sql` file in a directory:

```bat
ScriptRunner.exe /csName "MyCsName" /sqlPath "../../SQL" /csFile "../../Database.config"
```

Run a single SQL file:

```bat
ScriptRunner.exe /csName "MyCsName" /sql "../../SQL/001-create-tables.sql" /csFile "../../Database.config"
```

A connection string can also be supplied directly instead of using `/csName` and `/csFile`:

```bat
ScriptRunner.exe /cs "Server=localhost;Database=MyDatabase;Integrated Security=True" /sql "../../SQL/001-create-tables.sql"
```

One of `/cs` or `/csName` is required. One of `/sqlPath` or `/sql` is also required.
If both `/sqlPath` and `/sql` are provided, `/sqlPath` is used.

## SQL script location and execution order

SQL files can be stored in any directory. Pass that directory to ScriptRunner with `/sqlPath`.
Only files whose names end in `.sql` are executed. Subdirectories are scanned recursively.

For each directory, ScriptRunner executes:

1. The `.sql` files directly inside that directory, sorted by their full path.
2. Each subdirectory, sorted by its path, using the same recursive rules.

This means ScriptRunner runs files in the root of `/sqlPath` before entering any subdirectory. It
then applies the same rule inside every subdirectory. ScriptManager and ScriptRunner use the same
file-and-directory traversal order. An apparent difference occurs when one tool's root `SQL`
directory contains only subdirectories while the other also contains root-level SQL files.

Numeric filename prefixes are recommended when the execution order matters. For example:

```text
SQL/
  001-create-database-objects.sql
  002-insert-reference-data.sql
  tables/
    001-create-users.sql
  views/
    001-create-reports.sql
```

The execution order is:

```text
SQL/001-create-database-objects.sql
SQL/002-insert-reference-data.sql
SQL/tables/001-create-users.sql
SQL/views/001-create-reports.sql
```

ScriptRunner does not check whether a script has already been executed. Running the same command
again runs all selected scripts again. Scripts should therefore be safe to rerun when necessary.

## Database configuration

When `/csName` is used, ScriptRunner looks up a connection string with that name.

By default, connection strings are read from `Config/Database.config`, relative to the directory
containing `ScriptRunner.exe`. A different configuration file can be supplied with `/csFile`.

The file must use the following format:

```xml
<?xml version="1.0" encoding="utf-8" ?>
<connectionStrings>
  <add name="MyCsName"
       connectionString="Server=localhost;Database=MyDatabase;Integrated Security=True"
       providerName="System.Data.SqlClient" />
</connectionStrings>
```

Alternatively, `/cs` accepts a complete connection string directly and does not require a
database configuration file.

## Parameters

- `/cs connectionString`: complete SQL Server connection string.
- `/csName connectionStringName`: name of a connection string in the database configuration file.
- `/csFile pathToConfigFile`: path to a connection strings file. The default is
  `Config/Database.config` next to `ScriptRunner.exe`.
- `/sqlPath pathToSqlDirectory`: recursively run every `.sql` file in the directory.
- `/sql pathToSqlFile`: run one SQL file.
- `/sqlOutput outputFile`: when one file is selected, write the rows of its first result table to
  a tab-separated output file.
- `/verbose 1|0`: show or hide detailed information. The default is `1`.

SQL files must be UTF-8 encoded. If one file fails, ScriptRunner logs the error and continues with
the remaining files.
 
