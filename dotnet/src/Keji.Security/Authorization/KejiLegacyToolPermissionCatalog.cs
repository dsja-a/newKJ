using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;

namespace Keji.Security.Authorization;

public static class KejiLegacyToolPermissionCatalog
{
    private static readonly FrozenSet<string> WriteToolNames = new[]
    {
        "write_file",
        "edit_file",
        "create_folder",
        "delete_file",
        "create_document",
        "create_table",
        "create_presentation",
        "index_knowledge",
        "remove_from_knowledge",
        "run_code",
        "db_execute_query",
        "organize_files",
        "format_data",
        "clean_data",
        "convert_data",
        "mcp_filesystem_write_file",
        "mcp_filesystem_edit_file",
        "mcp_filesystem_move_file",
        "mcp_filesystem_create_directory",
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly string[] WriteToolPrefixes =
    {
        "mcp_quack_export_",
        "mcp_engineer-your-data_create_",
        "mcp_engineer-your-data_clean_",
        "mcp_engineer-your-data_export_",
    };

    private static readonly FrozenSet<string> ReadToolNames = new[]
    {
        "selfcheck_run",
        "verify_output",
        "read_document",
        "query_knowledge",
        "knowledge_stats",
        "analyze_data",
        "web_search",
        "browse_files",
        "search_files",
        "read_file",
        "list_allowed_directories",
        "db_connect",
        "get_time",
        "calculator",
        "ocr_image",
        "ocr_pdf",
        "parse_email",
        "__tool__",
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> McpFilesystemReadToolNames = new[]
    {
        "mcp_filesystem_list_allowed_directories",
        "mcp_filesystem_list_directory",
        "mcp_filesystem_list_directory_with_sizes",
        "mcp_filesystem_directory_tree",
        "mcp_filesystem_get_file_info",
        "mcp_filesystem_read_text_file",
        "mcp_filesystem_read_file",
        "mcp_filesystem_read_media_file",
        "mcp_filesystem_read_multiple_files",
        "mcp_filesystem_search_files",
    }.ToFrozenSet(StringComparer.Ordinal);

    public static bool IsWriteTool(string? toolName)
    {
        if (!IsValidToolName(toolName))
            return false;

        // Unknown filesystem operations are conservatively classified as writes.
        if (WriteToolNames.Contains(toolName))
            return true;

        if (toolName.StartsWith("mcp_filesystem_", StringComparison.Ordinal))
            return !McpFilesystemReadToolNames.Contains(toolName);

        foreach (var prefix in WriteToolPrefixes)
        {
            if (toolName.StartsWith(prefix, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    public static bool TryResolve(string? toolName, out KejiToolPermissionDescriptor? descriptor)
    {
        descriptor = null;

        if (!IsValidToolName(toolName))
            return false;

        if (toolName.StartsWith("mcp_filesystem_", StringComparison.Ordinal))
        {
            var accessLevel = McpFilesystemReadToolNames.Contains(toolName)
                ? KejiToolAccessLevel.Read
                : KejiToolAccessLevel.Write;
            descriptor = new KejiToolPermissionDescriptor(toolName, accessLevel);
            return true;
        }

        if (IsWriteTool(toolName))
        {
            descriptor = new KejiToolPermissionDescriptor(toolName, KejiToolAccessLevel.Write);
            return true;
        }

        if (ReadToolNames.Contains(toolName))
        {
            descriptor = new KejiToolPermissionDescriptor(toolName, KejiToolAccessLevel.Read);
            return true;
        }

        return false;
    }

    private static bool IsValidToolName([NotNullWhen(true)] string? toolName) =>
        toolName is not null &&
        toolName.Length is > 0 and <= 256 &&
        !string.IsNullOrWhiteSpace(toolName) &&
        toolName.AsSpan().Trim().Length == toolName.Length;
}
