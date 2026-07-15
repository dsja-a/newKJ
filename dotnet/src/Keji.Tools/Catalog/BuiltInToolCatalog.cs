using System.Collections.Immutable;
using Keji.Security.Authorization;
using Keji.Tools.Definitions;
using Keji.Tools.Definitions.Parameters;
using Keji.Tools.Names;
using Keji.Tools.Registry;

namespace Keji.Tools.Catalog;

public static class BuiltInToolCatalog
{
    public static ImmutableArray<KejiToolDefinition> All => _all.Value;
    public static KejiToolRegistryBuilder CreateBuilder()
    {
        var builder = new KejiToolRegistryBuilder();
        foreach (var def in All)
            builder.Register(def);
        return builder;
    }

    private static readonly Lazy<ImmutableArray<KejiToolDefinition>> _all = new(() => new List<KejiToolDefinition>
    {
        ReadFile(), ListDir(), Glob(), Grep(),
        BrowseFiles(), SearchFiles(), ListAllowedDirectories(), VerifyOutput(), ReadDocument(),
        WriteFile(), EditFile(), CreateFolder(), DeleteFile(), RenameFiles(), OrganizeFiles(), DeduplicateFiles(),
        AnalyzeData(), KnowledgeStats(),
        FormatData(), CleanData(), ConvertData(), EtlPipeline(),
        QueryKnowledge(), IndexKnowledge(), RemoveFromKnowledge(),
        CreateDocument(), CreateTable(), CreatePresentation(),
        BrowseArchive(), ExtractArchive(), CreateArchive(),
        ParseEmail(), BatchParseEmails(), ExtractEmailAttachments(),
        OcrImage(), OcrPdf(), OcrBatch(),
        DbConnect(), DbListTables(), DbDescribeTable(), DbTestConnection(), DbDisconnect(),
        GetTime(), Calculator(), SelfCheckRun(),
        WebSearch(), WebFetch(),
    }.ToImmutableArray());

    private static KejiToolParameterDefinition P(string name, KejiToolParameterType type, bool required, string description, bool sensitive = false, int? maxLength = null, int? minLength = null, int? minimum = null, int? maximum = null, int? maxItems = null, object? defaultValue = null, IReadOnlySet<string>? allowedValues = null) =>
        new(name, type, required, description, defaultValue: defaultValue, minimum: minimum, maximum: maximum, minLength: minLength, maxLength: maxLength, maxItems: maxItems, sensitive: sensitive, allowedValues: allowedValues);

    private static KejiToolDefinition ReadFile() => new(
        KejiToolName.Create("read_file"), 1, "Read the contents of a file.",
        KejiToolCategory.FileRead, KejiToolRiskLevel.ReadOnly, KejiToolExecutionTarget.Host,
        KejiPermission.FileRead,
        inputSchema: new KejiToolInputSchema(new List<KejiToolParameterDefinition>
        {
            P("path", KejiToolParameterType.String, true, "Path to the file.", maxLength: 1024),
            P("offset", KejiToolParameterType.Integer, false, "Line offset.", minimum: 0),
            P("limit", KejiToolParameterType.Integer, false, "Max lines.", minimum: 1, maximum: 5000),
        }));

    private static KejiToolDefinition WriteFile() => new(
        KejiToolName.Create("write_file"), 1, "Write content to a file.",
        KejiToolCategory.FileWrite, KejiToolRiskLevel.Mutating, KejiToolExecutionTarget.Host,
        KejiPermission.FileWrite,
        inputSchema: new KejiToolInputSchema(new List<KejiToolParameterDefinition>
        {
            P("path", KejiToolParameterType.String, true, "Path to the file.", maxLength: 1024),
            P("content", KejiToolParameterType.String, true, "Content to write.", maxLength: 100000),
        }));

    private static KejiToolDefinition EditFile() => new(
        KejiToolName.Create("edit_file"), 1, "Edit a file by replacing text.",
        KejiToolCategory.FileWrite, KejiToolRiskLevel.Mutating, KejiToolExecutionTarget.Host,
        KejiPermission.FileWrite,
        inputSchema: new KejiToolInputSchema(new List<KejiToolParameterDefinition>
        {
            P("path", KejiToolParameterType.String, true, "Path to the file.", maxLength: 1024),
            P("old_text", KejiToolParameterType.String, true, "Text to replace.", maxLength: 100000),
            P("new_text", KejiToolParameterType.String, true, "Replacement text.", maxLength: 100000),
        }));

    private static KejiToolDefinition ListDir() => new(
        KejiToolName.Create("list_dir"), 1, "List files and directories.",
        KejiToolCategory.FileRead, KejiToolRiskLevel.ReadOnly, KejiToolExecutionTarget.Host,
        KejiPermission.FileRead,
        inputSchema: new KejiToolInputSchema(new List<KejiToolParameterDefinition>
        {
            P("path", KejiToolParameterType.String, true, "Directory path.", maxLength: 1024),
            P("recursive", KejiToolParameterType.Boolean, false, "List recursively."),
        }));

    private static KejiToolDefinition Glob() => new(
        KejiToolName.Create("glob"), 1, "Search files by glob pattern.",
        KejiToolCategory.FileRead, KejiToolRiskLevel.ReadOnly, KejiToolExecutionTarget.Host,
        KejiPermission.FileRead,
        inputSchema: new KejiToolInputSchema(new List<KejiToolParameterDefinition>
        {
            P("pattern", KejiToolParameterType.String, true, "Glob pattern.", maxLength: 256),
            P("path", KejiToolParameterType.String, false, "Root directory.", maxLength: 1024),
        }));

    private static KejiToolDefinition Grep() => new(
        KejiToolName.Create("grep"), 1, "Search file contents by regex.",
        KejiToolCategory.FileRead, KejiToolRiskLevel.ReadOnly, KejiToolExecutionTarget.Host,
        KejiPermission.FileRead,
        inputSchema: new KejiToolInputSchema(new List<KejiToolParameterDefinition>
        {
            P("pattern", KejiToolParameterType.String, true, "Regex pattern.", maxLength: 2048),
            P("path", KejiToolParameterType.String, false, "Directory to search.", maxLength: 1024),
            P("glob", KejiToolParameterType.String, false, "File glob filter.", maxLength: 256),
        }));

    private static KejiToolDefinition BrowseFiles() => new(
        KejiToolName.Create("browse_files"), 1, "Browse directory contents.",
        KejiToolCategory.FileRead, KejiToolRiskLevel.ReadOnly, KejiToolExecutionTarget.Host,
        KejiPermission.FileRead,
        inputSchema: new KejiToolInputSchema(new List<KejiToolParameterDefinition>
        {
            P("path", KejiToolParameterType.String, true, "Directory path.", maxLength: 1024),
        }));

    private static KejiToolDefinition SearchFiles() => new(
        KejiToolName.Create("search_files"), 1, "Search files by pattern.",
        KejiToolCategory.FileRead, KejiToolRiskLevel.ReadOnly, KejiToolExecutionTarget.Host,
        KejiPermission.FileRead,
        inputSchema: new KejiToolInputSchema(new List<KejiToolParameterDefinition>
        {
            P("pattern", KejiToolParameterType.String, true, "Search pattern.", maxLength: 256),
            P("folder", KejiToolParameterType.String, false, "Folder to search.", maxLength: 1024),
        }));

    private static KejiToolDefinition ListAllowedDirectories() => new(
        KejiToolName.Create("list_allowed_directories"), 1, "List allowed directories.",
        KejiToolCategory.FileRead, KejiToolRiskLevel.ReadOnly, KejiToolExecutionTarget.Host,
        KejiPermission.FileRead);

    private static KejiToolDefinition VerifyOutput() => new(
        KejiToolName.Create("verify_output"), 1, "Verify output file exists.",
        KejiToolCategory.FileRead, KejiToolRiskLevel.ReadOnly, KejiToolExecutionTarget.Host,
        KejiPermission.FileRead,
        inputSchema: new KejiToolInputSchema(new List<KejiToolParameterDefinition>
        {
            P("path", KejiToolParameterType.String, true, "Path to verify.", maxLength: 1024),
        }));

    private static KejiToolDefinition ReadDocument() => new(
        KejiToolName.Create("read_document"), 1, "Read a document file.",
        KejiToolCategory.Document, KejiToolRiskLevel.ReadOnly, KejiToolExecutionTarget.PythonWorker,
        KejiPermission.FileRead,
        inputSchema: new KejiToolInputSchema(new List<KejiToolParameterDefinition>
        {
            P("path", KejiToolParameterType.String, true, "Document path.", maxLength: 1024),
        }));

    private static KejiToolDefinition CreateFolder() => new(
        KejiToolName.Create("create_folder"), 1, "Create a directory.",
        KejiToolCategory.FileWrite, KejiToolRiskLevel.Mutating, KejiToolExecutionTarget.Host,
        KejiPermission.FileWrite,
        inputSchema: new KejiToolInputSchema(new List<KejiToolParameterDefinition>
        {
            P("path", KejiToolParameterType.String, true, "Directory path.", maxLength: 1024),
        }));

    private static KejiToolDefinition DeleteFile() => new(
        KejiToolName.Create("delete_file"), 1, "Delete a file or directory.",
        KejiToolCategory.FileWrite, KejiToolRiskLevel.Mutating, KejiToolExecutionTarget.Host,
        KejiPermission.FileWrite,
        inputSchema: new KejiToolInputSchema(new List<KejiToolParameterDefinition>
        {
            P("path", KejiToolParameterType.String, true, "Path to delete.", maxLength: 1024),
            P("confirm", KejiToolParameterType.Boolean, true, "Confirmation."),
        }));

    private static KejiToolDefinition RenameFiles() => new(
        KejiToolName.Create("rename_files"), 1, "Rename files matching a pattern.",
        KejiToolCategory.FileWrite, KejiToolRiskLevel.Mutating, KejiToolExecutionTarget.PythonWorker,
        KejiPermission.FileWrite,
        inputSchema: new KejiToolInputSchema(new List<KejiToolParameterDefinition>
        {
            P("directory", KejiToolParameterType.String, true, "Directory.", maxLength: 1024),
            P("pattern", KejiToolParameterType.String, true, "Rename pattern.", maxLength: 256),
            P("value", KejiToolParameterType.String, false, "Replacement value.", maxLength: 256),
        }));

    private static KejiToolDefinition OrganizeFiles() => new(
        KejiToolName.Create("organize_files"), 1, "Organize files in a directory.",
        KejiToolCategory.FileWrite, KejiToolRiskLevel.Mutating, KejiToolExecutionTarget.PythonWorker,
        KejiPermission.FileWrite,
        inputSchema: new KejiToolInputSchema(new List<KejiToolParameterDefinition>
        {
            P("source_dir", KejiToolParameterType.String, true, "Source directory.", maxLength: 1024),
            P("mode", KejiToolParameterType.String, false, "Organization mode.", maxLength: 128),
        }));

    private static KejiToolDefinition DeduplicateFiles() => new(
        KejiToolName.Create("deduplicate_files"), 1, "Deduplicate files.",
        KejiToolCategory.FileWrite, KejiToolRiskLevel.Mutating, KejiToolExecutionTarget.PythonWorker,
        KejiPermission.FileWrite,
        inputSchema: new KejiToolInputSchema(new List<KejiToolParameterDefinition>
        {
            P("directory", KejiToolParameterType.String, true, "Directory.", maxLength: 1024),
        }));

    private static KejiToolDefinition AnalyzeData() => new(
        KejiToolName.Create("analyze_data"), 1, "Analyze data from a source.",
        KejiToolCategory.DataRead, KejiToolRiskLevel.ReadOnly, KejiToolExecutionTarget.PythonWorker,
        KejiPermission.FileRead,
        inputSchema: new KejiToolInputSchema(new List<KejiToolParameterDefinition>
        {
            P("data_source", KejiToolParameterType.String, true, "Data source path.", maxLength: 1024),
        }));

    private static KejiToolDefinition KnowledgeStats() => new(
        KejiToolName.Create("knowledge_stats"), 1, "Get knowledge base statistics.",
        KejiToolCategory.DataRead, KejiToolRiskLevel.ReadOnly, KejiToolExecutionTarget.Host,
        KejiPermission.KnowledgeRead);

    private static KejiToolDefinition FormatData() => new(
        KejiToolName.Create("format_data"), 1, "Format or transform data.",
        KejiToolCategory.DataWrite, KejiToolRiskLevel.Mutating, KejiToolExecutionTarget.PythonWorker,
        KejiPermission.FileWrite,
        inputSchema: new KejiToolInputSchema(new List<KejiToolParameterDefinition>
        {
            P("data", KejiToolParameterType.String, true, "Data to format.", maxLength: 100000),
            P("operation", KejiToolParameterType.String, true, "Format operation.", maxLength: 256),
        }));

    private static KejiToolDefinition CleanData() => new(
        KejiToolName.Create("clean_data"), 1, "Clean and normalize data.",
        KejiToolCategory.DataWrite, KejiToolRiskLevel.Mutating, KejiToolExecutionTarget.PythonWorker,
        KejiPermission.FileWrite,
        inputSchema: new KejiToolInputSchema(new List<KejiToolParameterDefinition>
        {
            P("source", KejiToolParameterType.String, true, "Data source.", maxLength: 1024),
            P("operations", KejiToolParameterType.String, false, "Cleaning operations.", maxLength: 2048),
        }));

    private static KejiToolDefinition ConvertData() => new(
        KejiToolName.Create("convert_data"), 1, "Convert data between formats.",
        KejiToolCategory.DataWrite, KejiToolRiskLevel.Mutating, KejiToolExecutionTarget.PythonWorker,
        KejiPermission.FileWrite,
        inputSchema: new KejiToolInputSchema(new List<KejiToolParameterDefinition>
        {
            P("source", KejiToolParameterType.String, true, "Source path.", maxLength: 1024),
            P("target_format", KejiToolParameterType.String, true, "Target format.", maxLength: 128),
        }));

    private static KejiToolDefinition EtlPipeline() => new(
        KejiToolName.Create("etl_pipeline"), 1, "Run an ETL pipeline.",
        KejiToolCategory.DataWrite, KejiToolRiskLevel.Mutating, KejiToolExecutionTarget.PythonWorker,
        KejiPermission.FileWrite,
        inputSchema: new KejiToolInputSchema(new List<KejiToolParameterDefinition>
        {
            P("source", KejiToolParameterType.String, true, "Source.", maxLength: 1024),
            P("steps", KejiToolParameterType.String, true, "ETL steps.", maxLength: 100000),
        }));

    private static KejiToolDefinition QueryKnowledge() => new(
        KejiToolName.Create("query_knowledge"), 1, "Query the knowledge base.",
        KejiToolCategory.Knowledge, KejiToolRiskLevel.ReadOnly, KejiToolExecutionTarget.Host,
        KejiPermission.KnowledgeRead,
        inputSchema: new KejiToolInputSchema(new List<KejiToolParameterDefinition>
        {
            P("query", KejiToolParameterType.String, true, "Search query.", maxLength: 4000),
            P("n_results", KejiToolParameterType.Integer, false, "Max results.", minimum: 1, maximum: 50),
        }));

    private static KejiToolDefinition IndexKnowledge() => new(
        KejiToolName.Create("index_knowledge"), 1, "Index a directory into knowledge base.",
        KejiToolCategory.Knowledge, KejiToolRiskLevel.Mutating, KejiToolExecutionTarget.Host,
        KejiPermission.KnowledgeWrite,
        inputSchema: new KejiToolInputSchema(new List<KejiToolParameterDefinition>
        {
            P("path", KejiToolParameterType.String, true, "Directory to index.", maxLength: 1024),
            P("recursive", KejiToolParameterType.Boolean, false, "Index recursively."),
        }));

    private static KejiToolDefinition RemoveFromKnowledge() => new(
        KejiToolName.Create("remove_from_knowledge"), 1, "Remove an entry from knowledge base.",
        KejiToolCategory.Knowledge, KejiToolRiskLevel.Mutating, KejiToolExecutionTarget.Host,
        KejiPermission.KnowledgeWrite,
        inputSchema: new KejiToolInputSchema(new List<KejiToolParameterDefinition>
        {
            P("name", KejiToolParameterType.String, true, "Entry name to remove.", maxLength: 256),
        }));

    private static KejiToolDefinition CreateDocument() => new(
        KejiToolName.Create("create_document"), 1, "Create a document file.",
        KejiToolCategory.Office, KejiToolRiskLevel.ExternalSideEffect, KejiToolExecutionTarget.PythonWorker,
        KejiPermission.FileWrite,
        inputSchema: new KejiToolInputSchema(new List<KejiToolParameterDefinition>
        {
            P("title", KejiToolParameterType.String, true, "Document title.", maxLength: 256),
            P("save_path", KejiToolParameterType.String, true, "Save path.", maxLength: 1024),
        }));

    private static KejiToolDefinition CreateTable() => new(
        KejiToolName.Create("create_table"), 1, "Create a table/spreadsheet.",
        KejiToolCategory.Office, KejiToolRiskLevel.ExternalSideEffect, KejiToolExecutionTarget.PythonWorker,
        KejiPermission.FileWrite,
        inputSchema: new KejiToolInputSchema(new List<KejiToolParameterDefinition>
        {
            P("headers", KejiToolParameterType.String, true, "Column headers.", maxLength: 2048),
            P("save_path", KejiToolParameterType.String, true, "Save path.", maxLength: 1024),
        }));

    private static KejiToolDefinition CreatePresentation() => new(
        KejiToolName.Create("create_presentation"), 1, "Create a presentation.",
        KejiToolCategory.Office, KejiToolRiskLevel.ExternalSideEffect, KejiToolExecutionTarget.PythonWorker,
        KejiPermission.FileWrite,
        inputSchema: new KejiToolInputSchema(new List<KejiToolParameterDefinition>
        {
            P("title", KejiToolParameterType.String, true, "Presentation title.", maxLength: 256),
            P("save_path", KejiToolParameterType.String, true, "Save path.", maxLength: 1024),
        }));

    private static KejiToolDefinition BrowseArchive() => new(
        KejiToolName.Create("browse_archive"), 1, "Browse archive contents.",
        KejiToolCategory.Archive, KejiToolRiskLevel.ReadOnly, KejiToolExecutionTarget.PythonWorker,
        KejiPermission.FileRead,
        inputSchema: new KejiToolInputSchema(new List<KejiToolParameterDefinition>
        {
            P("path", KejiToolParameterType.String, true, "Archive path.", maxLength: 1024),
        }));

    private static KejiToolDefinition ExtractArchive() => new(
        KejiToolName.Create("extract_archive"), 1, "Extract an archive.",
        KejiToolCategory.Archive, KejiToolRiskLevel.Mutating, KejiToolExecutionTarget.PythonWorker,
        KejiPermission.FileWrite,
        inputSchema: new KejiToolInputSchema(new List<KejiToolParameterDefinition>
        {
            P("path", KejiToolParameterType.String, true, "Archive path.", maxLength: 1024),
            P("output_dir", KejiToolParameterType.String, false, "Output directory.", maxLength: 1024),
        }));

    private static KejiToolDefinition CreateArchive() => new(
        KejiToolName.Create("create_archive"), 1, "Create an archive.",
        KejiToolCategory.Archive, KejiToolRiskLevel.Mutating, KejiToolExecutionTarget.PythonWorker,
        KejiPermission.FileWrite,
        inputSchema: new KejiToolInputSchema(new List<KejiToolParameterDefinition>
        {
            P("sources", KejiToolParameterType.String, true, "Source paths.", maxLength: 100000),
            P("output_path", KejiToolParameterType.String, true, "Output path.", maxLength: 1024),
        }));

    private static KejiToolDefinition ParseEmail() => new(
        KejiToolName.Create("parse_email"), 1, "Parse an email file.",
        KejiToolCategory.Email, KejiToolRiskLevel.ReadOnly, KejiToolExecutionTarget.PythonWorker,
        KejiPermission.FileRead,
        inputSchema: new KejiToolInputSchema(new List<KejiToolParameterDefinition>
        {
            P("path", KejiToolParameterType.String, true, "Email file path.", maxLength: 1024),
        }));

    private static KejiToolDefinition BatchParseEmails() => new(
        KejiToolName.Create("batch_parse_emails"), 1, "Parse multiple email files.",
        KejiToolCategory.Email, KejiToolRiskLevel.ReadOnly, KejiToolExecutionTarget.PythonWorker,
        KejiPermission.FileRead,
        inputSchema: new KejiToolInputSchema(new List<KejiToolParameterDefinition>
        {
            P("directory", KejiToolParameterType.String, true, "Directory.", maxLength: 1024),
        }));

    private static KejiToolDefinition ExtractEmailAttachments() => new(
        KejiToolName.Create("extract_email_attachments"), 1, "Extract attachments from emails.",
        KejiToolCategory.Email, KejiToolRiskLevel.Mutating, KejiToolExecutionTarget.PythonWorker,
        KejiPermission.FileWrite,
        inputSchema: new KejiToolInputSchema(new List<KejiToolParameterDefinition>
        {
            P("path", KejiToolParameterType.String, true, "Email file path.", maxLength: 1024),
            P("output_dir", KejiToolParameterType.String, false, "Output directory.", maxLength: 1024),
        }));

    private static KejiToolDefinition OcrImage() => new(
        KejiToolName.Create("ocr_image"), 1, "Perform OCR on an image.",
        KejiToolCategory.Utility, KejiToolRiskLevel.ReadOnly, KejiToolExecutionTarget.PythonWorker,
        KejiPermission.FileRead,
        inputSchema: new KejiToolInputSchema(new List<KejiToolParameterDefinition>
        {
            P("path", KejiToolParameterType.String, true, "Image path.", maxLength: 1024),
            P("lang", KejiToolParameterType.String, false, "Language code.", maxLength: 32),
        }));

    private static KejiToolDefinition OcrPdf() => new(
        KejiToolName.Create("ocr_pdf"), 1, "Perform OCR on a PDF.",
        KejiToolCategory.Utility, KejiToolRiskLevel.ReadOnly, KejiToolExecutionTarget.PythonWorker,
        KejiPermission.FileRead,
        inputSchema: new KejiToolInputSchema(new List<KejiToolParameterDefinition>
        {
            P("path", KejiToolParameterType.String, true, "PDF path.", maxLength: 1024),
            P("lang", KejiToolParameterType.String, false, "Language code.", maxLength: 32),
        }));

    private static KejiToolDefinition OcrBatch() => new(
        KejiToolName.Create("ocr_batch"), 1, "Batch OCR files in a directory.",
        KejiToolCategory.Utility, KejiToolRiskLevel.ReadOnly, KejiToolExecutionTarget.PythonWorker,
        KejiPermission.FileRead,
        inputSchema: new KejiToolInputSchema(new List<KejiToolParameterDefinition>
        {
            P("directory", KejiToolParameterType.String, true, "Directory.", maxLength: 1024),
            P("recursive", KejiToolParameterType.Boolean, false, "Process recursively."),
        }));

    private static KejiToolDefinition DbConnect() => new(
        KejiToolName.Create("db_connect"), 1, "Connect to a database.",
        KejiToolCategory.Database, KejiToolRiskLevel.ExternalSideEffect, KejiToolExecutionTarget.PythonWorker,
        KejiPermission.DatabaseManage,
        inputSchema: new KejiToolInputSchema(new List<KejiToolParameterDefinition>
        {
            P("db_type", KejiToolParameterType.String, true, "Database type.", maxLength: 64),
            P("host", KejiToolParameterType.String, true, "Host.", maxLength: 256),
            P("database", KejiToolParameterType.String, true, "Database name.", maxLength: 256),
            P("username", KejiToolParameterType.String, true, "Username.", maxLength: 256),
            P("password", KejiToolParameterType.String, true, "Password.", sensitive: true, maxLength: 512),
        }));

    private static KejiToolDefinition DbListTables() => new(
        KejiToolName.Create("db_list_tables"), 1, "List database tables.",
        KejiToolCategory.Database, KejiToolRiskLevel.ReadOnly, KejiToolExecutionTarget.PythonWorker,
        KejiPermission.DatabaseRead,
        inputSchema: new KejiToolInputSchema(new List<KejiToolParameterDefinition>
        {
            P("connection_id", KejiToolParameterType.String, true, "Connection ID.", maxLength: 128),
        }));

    private static KejiToolDefinition DbDescribeTable() => new(
        KejiToolName.Create("db_describe_table"), 1, "Describe a database table.",
        KejiToolCategory.Database, KejiToolRiskLevel.ReadOnly, KejiToolExecutionTarget.PythonWorker,
        KejiPermission.DatabaseRead,
        inputSchema: new KejiToolInputSchema(new List<KejiToolParameterDefinition>
        {
            P("connection_id", KejiToolParameterType.String, true, "Connection ID.", maxLength: 128),
            P("table_name", KejiToolParameterType.String, true, "Table name.", maxLength: 256),
        }));

    private static KejiToolDefinition DbTestConnection() => new(
        KejiToolName.Create("db_test_connection"), 1, "Test a database connection.",
        KejiToolCategory.Database, KejiToolRiskLevel.ReadOnly, KejiToolExecutionTarget.PythonWorker,
        KejiPermission.DatabaseRead,
        inputSchema: new KejiToolInputSchema(new List<KejiToolParameterDefinition>
        {
            P("db_type", KejiToolParameterType.String, true, "Database type.", maxLength: 64),
            P("host", KejiToolParameterType.String, true, "Host.", maxLength: 256),
            P("database", KejiToolParameterType.String, true, "Database name.", maxLength: 256),
            P("username", KejiToolParameterType.String, true, "Username.", maxLength: 256),
            P("password", KejiToolParameterType.String, true, "Password.", sensitive: true, maxLength: 512),
        }));

    private static KejiToolDefinition DbDisconnect() => new(
        KejiToolName.Create("db_disconnect"), 1, "Disconnect from a database.",
        KejiToolCategory.Database, KejiToolRiskLevel.ReadOnly, KejiToolExecutionTarget.PythonWorker,
        KejiPermission.DatabaseRead,
        inputSchema: new KejiToolInputSchema(new List<KejiToolParameterDefinition>
        {
            P("connection_id", KejiToolParameterType.String, true, "Connection ID.", maxLength: 128),
        }));

    private static KejiToolDefinition GetTime() => new(
        KejiToolName.Create("get_time"), 1, "Get the current time.",
        KejiToolCategory.Utility, KejiToolRiskLevel.ReadOnly, KejiToolExecutionTarget.ToolWorker,
        KejiPermission.ToolCatalogRead,
        availability: KejiToolAvailability.Executable);

    private static KejiToolDefinition Calculator() => new(
        KejiToolName.Create("calculator"), 1, "Evaluate a mathematical expression.",
        KejiToolCategory.Utility, KejiToolRiskLevel.ReadOnly, KejiToolExecutionTarget.ToolWorker,
        KejiPermission.ToolCatalogRead,
        availability: KejiToolAvailability.Executable,
        inputSchema: new KejiToolInputSchema(new List<KejiToolParameterDefinition>
        {
            P("expr", KejiToolParameterType.String, true, "Mathematical expression.", maxLength: 2000),
        }));

    private static KejiToolDefinition WebSearch() => new(
        KejiToolName.Create("web_search"), 1, "Search the web for information.",
        KejiToolCategory.Network, KejiToolRiskLevel.ExternalSideEffect, KejiToolExecutionTarget.Host,
        KejiPermission.ToolExecuteRead,
        inputSchema: new KejiToolInputSchema(new List<KejiToolParameterDefinition>
        {
            P("query", KejiToolParameterType.String, true, "Search query.", maxLength: 4000),
            P("max_results", KejiToolParameterType.Integer, false, "Max results.", minimum: 1, maximum: 20),
        }));

    private static KejiToolDefinition WebFetch() => new(
        KejiToolName.Create("web_fetch"), 1, "Fetch content from a URL.",
        KejiToolCategory.Network, KejiToolRiskLevel.ExternalSideEffect, KejiToolExecutionTarget.Host,
        KejiPermission.ToolExecuteRead,
        inputSchema: new KejiToolInputSchema(new List<KejiToolParameterDefinition>
        {
            P("url", KejiToolParameterType.String, true, "URL to fetch.", maxLength: 4096),
        }));

    private static KejiToolDefinition SelfCheckRun() => new(
        KejiToolName.Create("selfcheck_run"), 1, "Run system self-check.",
        KejiToolCategory.System, KejiToolRiskLevel.ReadOnly, KejiToolExecutionTarget.Host,
        KejiPermission.SystemRead,
        inputSchema: new KejiToolInputSchema(new List<KejiToolParameterDefinition>
        {
            P("scope", KejiToolParameterType.String, false, "Check scope.", maxLength: 128,
                allowedValues: new HashSet<string>(StringComparer.Ordinal) { "full", "tools", "mcp", "database" }),
        }));
}
