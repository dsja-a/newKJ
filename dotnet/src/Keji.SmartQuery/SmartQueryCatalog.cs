using System.Collections.Immutable;
using System.Globalization;
using Keji.Persistence;
using Microsoft.Data.Sqlite;

namespace Keji.SmartQuery;

public interface IKejiSmartQueryDataSourceAdministration
{
    Task UpsertAsync(KejiSmartQueryDataSource source, CancellationToken cancellationToken = default);
}

public sealed class SqliteKejiSmartQueryDataSourceCatalog :
    IKejiSmartQueryDataSourceCatalog, IKejiSmartQueryDataSourceAdministration
{
    private readonly ISqliteConnectionFactory _connections;
    private readonly TimeProvider _time;
    public SqliteKejiSmartQueryDataSourceCatalog(
        ISqliteConnectionFactory connections, TimeProvider? timeProvider = null)
    {
        _connections = connections;
        _time = timeProvider ?? TimeProvider.System;
    }

    public async Task<KejiSmartQueryDataSource?> GetAccessibleAsync(
        string dataSourceId, string userId, bool isAdmin = false,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT owner_user_id, display_name, dialect, host, port, database_name, username,
                   password_secret_reference, tls_mode, visibility, created_at_utc, updated_at_utc
            FROM smart_query_data_sources
            WHERE id=$id AND enabled=1
              AND (owner_user_id=$user OR visibility=2 OR $admin=1)
            ORDER BY CASE WHEN owner_user_id=$user THEN 0 ELSE 1 END
            LIMIT 1
            """;
        command.Parameters.AddWithValue("$id", dataSourceId);
        command.Parameters.AddWithValue("$user", userId);
        command.Parameters.AddWithValue("$admin", isAdmin ? 1 : 0);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        var owner = reader.GetString(0);
        var source = new KejiSmartQueryDataSource(
            dataSourceId, owner, (KejiSmartQueryDialect)reader.GetInt32(2), reader.GetString(3),
            reader.GetInt32(4), reader.GetString(5), reader.GetString(6),
            new(reader.GetString(7)), (KejiSmartQueryTlsMode)reader.GetInt32(8), [], [], true,
            reader.GetString(1), (KejiSmartQueryDataSourceVisibility)reader.GetInt32(9), [],
            DateTimeOffset.Parse(reader.GetString(10), CultureInfo.InvariantCulture),
            DateTimeOffset.Parse(reader.GetString(11), CultureInfo.InvariantCulture));
        await reader.DisposeAsync().ConfigureAwait(false);
        var schemas = await LoadSchemas(connection, source.Id, owner, cancellationToken).ConfigureAwait(false);
        var tables = await LoadTables(connection, source.Id, owner, cancellationToken).ConfigureAwait(false);
        var foreignKeys = await LoadForeignKeys(connection, source.Id, owner, cancellationToken).ConfigureAwait(false);
        return source with { AllowedSchemas = schemas, Tables = tables, ForeignKeys = foreignKeys };
    }
    public Task<KejiSmartQueryDataSource?> GetAccessibleAsync(
        string dataSourceId, string userId, CancellationToken cancellationToken) =>
        GetAccessibleAsync(dataSourceId, userId, false, cancellationToken);

    public async Task UpsertAsync(KejiSmartQueryDataSource source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!KejiSmartQueryValidation.IsSafeSource(source, source.Id, source.OwnerUserId, false))
            throw new ArgumentException("Unsafe data source.", nameof(source));
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            var now = _time.GetUtcNow();
            var created = source.CreatedAtUtc == default ? now : source.CreatedAtUtc;
            command.CommandText = """
                INSERT INTO smart_query_data_sources
                  (id,owner_user_id,display_name,dialect,host,port,database_name,username,
                   password_secret_reference,tls_mode,visibility,enabled,created_at_utc,updated_at_utc)
                VALUES($id,$owner,$display,$dialect,$host,$port,$database,$username,$secret,$tls,$visibility,$enabled,$created,$updated)
                ON CONFLICT(id,owner_user_id) DO UPDATE SET
                  display_name=excluded.display_name,dialect=excluded.dialect,host=excluded.host,
                  port=excluded.port,database_name=excluded.database_name,username=excluded.username,
                  password_secret_reference=excluded.password_secret_reference,tls_mode=excluded.tls_mode,
                  visibility=excluded.visibility,enabled=excluded.enabled,updated_at_utc=excluded.updated_at_utc
                """;
            Add(command, "$id", source.Id); Add(command, "$owner", source.OwnerUserId);
            Add(command, "$display", source.DisplayName); Add(command, "$dialect", (int)source.Dialect);
            Add(command, "$host", source.Host); Add(command, "$port", source.Port);
            Add(command, "$database", source.Database); Add(command, "$username", source.Username);
            Add(command, "$secret", source.SecretReference.Value); Add(command, "$tls", (int)source.TlsMode);
            Add(command, "$visibility", (int)source.Visibility); Add(command, "$enabled", source.Enabled ? 1 : 0);
            Add(command, "$created", created.ToString("O", CultureInfo.InvariantCulture));
            Add(command, "$updated", now.ToString("O", CultureInfo.InvariantCulture));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            foreach (var table in new[] { "smart_query_foreign_keys", "smart_query_columns",
                         "smart_query_tables", "smart_query_allowed_schemas" })
            {
                command.Parameters.Clear();
                command.CommandText = $"DELETE FROM {table} WHERE data_source_id=$id AND owner_user_id=$owner";
                Add(command, "$id", source.Id); Add(command, "$owner", source.OwnerUserId);
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            for (var i = 0; i < source.AllowedSchemas.Length; i++)
                await InsertSchema(command, source, source.AllowedSchemas[i], i, cancellationToken).ConfigureAwait(false);
            for (var i = 0; i < source.Tables.Length; i++)
            {
                await InsertTable(command, source, source.Tables[i], i, cancellationToken).ConfigureAwait(false);
                for (var j = 0; j < source.Tables[i].Columns.Length; j++)
                    await InsertColumn(command, source, source.Tables[i], source.Tables[i].Columns[j], j, cancellationToken)
                        .ConfigureAwait(false);
            }
            for (var i = 0; i < source.ForeignKeys.Length; i++)
                await InsertForeignKey(command, source, source.ForeignKeys[i], i, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<ImmutableArray<string>> LoadSchemas(
        SqliteConnection connection, string id, string owner, CancellationToken ct)
    {
        await using var command = Query(connection,
            "SELECT schema_name FROM smart_query_allowed_schemas WHERE data_source_id=$id AND owner_user_id=$owner ORDER BY ordinal",
            id, owner);
        var values = ImmutableArray.CreateBuilder<string>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false)) values.Add(reader.GetString(0));
        return values.ToImmutable();
    }
    private static async Task<ImmutableArray<KejiSmartQueryTable>> LoadTables(
        SqliteConnection connection, string id, string owner, CancellationToken ct)
    {
        await using var command = Query(connection, """
            SELECT schema_name,table_name,display_name,description,business_context,
                   qa_enabled,query_enabled,estimated_row_count
            FROM smart_query_tables WHERE data_source_id=$id AND owner_user_id=$owner ORDER BY ordinal
            """, id, owner);
        var rows = new List<(string Schema,string Name,string Display,string Description,string Context,bool Qa,bool Query,long Count)>();
        await using (var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false))
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
                rows.Add((reader.GetString(0),reader.GetString(1),reader.GetString(2),reader.GetString(3),
                    reader.GetString(4),reader.GetBoolean(5),reader.GetBoolean(6),reader.GetInt64(7)));
        var tables = ImmutableArray.CreateBuilder<KejiSmartQueryTable>();
        foreach (var row in rows)
            tables.Add(new(row.Name, await LoadColumns(connection,id,owner,row.Schema,row.Name,ct).ConfigureAwait(false),
                row.Query,row.Schema,row.Display,row.Description,row.Context,row.Qa,row.Count));
        return tables.ToImmutable();
    }
    private static async Task<ImmutableArray<KejiSmartQueryColumn>> LoadColumns(
        SqliteConnection connection,string id,string owner,string schema,string table,CancellationToken ct)
    {
        await using var command = Query(connection, """
            SELECT column_name,data_type,nullable,description,query_enabled,sensitive,ordinal
            FROM smart_query_columns WHERE data_source_id=$id AND owner_user_id=$owner
              AND schema_name=$schema AND table_name=$table ORDER BY ordinal
            """, id, owner);
        Add(command,"$schema",schema); Add(command,"$table",table);
        var values=ImmutableArray.CreateBuilder<KejiSmartQueryColumn>();
        await using var reader=await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while(await reader.ReadAsync(ct).ConfigureAwait(false))
            values.Add(new(reader.GetString(0),(KejiSmartQueryColumnType)reader.GetInt32(1),
                reader.GetBoolean(2),reader.GetString(3),reader.GetBoolean(4),reader.GetBoolean(5),reader.GetInt32(6)));
        return values.ToImmutable();
    }
    private static async Task<ImmutableArray<KejiSmartQueryForeignKey>> LoadForeignKeys(
        SqliteConnection connection,string id,string owner,CancellationToken ct)
    {
        await using var command=Query(connection, """
            SELECT name,principal_schema,principal_table,principal_column,dependent_schema,
                   dependent_table,dependent_column,query_enabled
            FROM smart_query_foreign_keys WHERE data_source_id=$id AND owner_user_id=$owner ORDER BY ordinal
            """,id,owner);
        var values=ImmutableArray.CreateBuilder<KejiSmartQueryForeignKey>();
        await using var reader=await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while(await reader.ReadAsync(ct).ConfigureAwait(false))
            values.Add(new(reader.GetString(0),reader.GetString(2),reader.GetString(3),reader.GetString(5),
                reader.GetString(6),reader.GetBoolean(7),reader.GetString(1),reader.GetString(4)));
        return values.ToImmutable();
    }

    private static async Task InsertSchema(SqliteCommand c,KejiSmartQueryDataSource s,string schema,int ordinal,CancellationToken ct)
    { c.Parameters.Clear(); c.CommandText="INSERT INTO smart_query_allowed_schemas VALUES($id,$owner,$schema,$ordinal)";
      Add(c,"$id",s.Id);Add(c,"$owner",s.OwnerUserId);Add(c,"$schema",schema);Add(c,"$ordinal",ordinal);
      await c.ExecuteNonQueryAsync(ct).ConfigureAwait(false); }
    private static async Task InsertTable(SqliteCommand c,KejiSmartQueryDataSource s,KejiSmartQueryTable t,int ordinal,CancellationToken ct)
    { c.Parameters.Clear(); c.CommandText="""INSERT INTO smart_query_tables VALUES($id,$owner,$schema,$table,$display,$description,$context,$qa,$query,$count,$ordinal)""";
      Add(c,"$id",s.Id);Add(c,"$owner",s.OwnerUserId);Add(c,"$schema",t.SchemaName);Add(c,"$table",t.Name);
      Add(c,"$display",t.DisplayName);Add(c,"$description",t.Description);Add(c,"$context",t.BusinessContext);
      Add(c,"$qa",t.QaEnabled?1:0);Add(c,"$query",t.QueryEnabled?1:0);Add(c,"$count",t.EstimatedRowCount);Add(c,"$ordinal",ordinal);
      await c.ExecuteNonQueryAsync(ct).ConfigureAwait(false); }
    private static async Task InsertColumn(SqliteCommand c,KejiSmartQueryDataSource s,KejiSmartQueryTable t,KejiSmartQueryColumn x,int ordinal,CancellationToken ct)
    { c.Parameters.Clear(); c.CommandText="""INSERT INTO smart_query_columns VALUES($id,$owner,$schema,$table,$column,$type,$nullable,$description,$query,$sensitive,$ordinal)""";
      Add(c,"$id",s.Id);Add(c,"$owner",s.OwnerUserId);Add(c,"$schema",t.SchemaName);Add(c,"$table",t.Name);Add(c,"$column",x.Name);
      Add(c,"$type",(int)x.Type);Add(c,"$nullable",x.Nullable?1:0);Add(c,"$description",x.Description);
      Add(c,"$query",x.QueryEnabled?1:0);Add(c,"$sensitive",x.Sensitive?1:0);Add(c,"$ordinal",ordinal);
      await c.ExecuteNonQueryAsync(ct).ConfigureAwait(false); }
    private static async Task InsertForeignKey(SqliteCommand c,KejiSmartQueryDataSource s,KejiSmartQueryForeignKey f,int ordinal,CancellationToken ct)
    { c.Parameters.Clear(); c.CommandText="""INSERT INTO smart_query_foreign_keys VALUES($id,$owner,$name,$ps,$pt,$pc,$ds,$dt,$dc,$query,$ordinal)""";
      Add(c,"$id",s.Id);Add(c,"$owner",s.OwnerUserId);Add(c,"$name",f.Name);Add(c,"$ps",f.PrincipalSchema);
      Add(c,"$pt",f.PrincipalTable);Add(c,"$pc",f.PrincipalColumn);Add(c,"$ds",f.DependentSchema);
      Add(c,"$dt",f.DependentTable);Add(c,"$dc",f.DependentColumn);Add(c,"$query",f.QueryEnabled?1:0);Add(c,"$ordinal",ordinal);
      await c.ExecuteNonQueryAsync(ct).ConfigureAwait(false); }
    private static SqliteCommand Query(SqliteConnection c,string sql,string id,string owner)
    { var cmd=c.CreateCommand();cmd.CommandText=sql;Add(cmd,"$id",id);Add(cmd,"$owner",owner);return cmd; }
    private static void Add(SqliteCommand command,string name,object value)=>command.Parameters.AddWithValue(name,value);
}

internal static class KejiSmartQueryValidation
{
    internal static bool IsIdentifier(string? value,int max)=>value is { Length:>0 }&&value.Length<=max&&
        char.IsAsciiLetterOrDigit(value[0])&&value.All(static c=>char.IsAsciiLetterOrDigit(c)||c is '_' or '-');
    internal static bool IsSafeSource(KejiSmartQueryDataSource? s,string id,string user,bool requireEnabled=true)
    {
        if(s is null||requireEnabled&&!s.Enabled||s.Id!=id||s.OwnerUserId!=user||
           !IsIdentifier(s.Id,128)||!IsIdentifier(s.OwnerUserId,128)||!Enum.IsDefined(s.Dialect)||s.Dialect==0||
           !Enum.IsDefined(s.TlsMode)||s.TlsMode==0||!Enum.IsDefined(s.Visibility)||s.Visibility==0||
           s.Host.Length is <1 or >253||s.Host.Any(static c=>!(char.IsAsciiLetterOrDigit(c)||c is '.' or '-' or ':'))||
           s.Port is <1 or >65535||!IsIdentifier(s.Database,128)||!IsIdentifier(s.Username,128)||
           s.Tables.IsDefaultOrEmpty||s.Tables.Length>128||s.ForeignKeys.IsDefault||s.ForeignKeys.Length>1024||
           s.Tables.Sum(static t=>t.Columns.Length)>4096||s.AllowedSchemas.IsDefaultOrEmpty||
           s.AllowedSchemas.Distinct(StringComparer.Ordinal).Count()!=s.AllowedSchemas.Length) return false;
        var tables=s.Tables.ToDictionary(static t=>t.Name,StringComparer.Ordinal);
        foreach(var t in s.Tables)
            if(!IsIdentifier(t.Name,128)||!IsIdentifier(t.SchemaName,128)||!s.AllowedSchemas.Contains(t.SchemaName)||
               t.Description.Length>4096||t.BusinessContext.Length>4096||t.Columns.IsDefaultOrEmpty||t.Columns.Length>256||
               t.Columns.Select(static c=>c.Name).Distinct(StringComparer.Ordinal).Count()!=t.Columns.Length||
               t.Columns.Any(c=>!IsIdentifier(c.Name,128)||c.Type==0||!Enum.IsDefined(c.Type)||c.Description.Length>4096)) return false;
        foreach(var f in s.ForeignKeys)
            if(!IsIdentifier(f.Name,128)||!tables.TryGetValue(f.PrincipalTable,out var p)||
               !tables.TryGetValue(f.DependentTable,out var d)||p.SchemaName!=f.PrincipalSchema||d.SchemaName!=f.DependentSchema||
               !p.Columns.Any(c=>c.Name==f.PrincipalColumn)||!d.Columns.Any(c=>c.Name==f.DependentColumn)) return false;
        return true;
    }
}
