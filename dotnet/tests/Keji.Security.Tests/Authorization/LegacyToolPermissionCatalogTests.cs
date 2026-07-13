using Keji.Security.Authorization;

namespace Keji.Security.Tests.Authorization;

public sealed class LegacyToolPermissionCatalogTests
{
    public static TheoryData<string> ExplicitWriteTools => CreateTheoryData(
    [
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
    ]);

    public static TheoryData<string> ExplicitReadTools => CreateTheoryData(
    [
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
    ]);

    public static TheoryData<string> WritePrefixExamples => CreateTheoryData(
    [
        "mcp_quack_export_csv",
        "mcp_engineer-your-data_create_chart",
        "mcp_engineer-your-data_clean_data",
        "mcp_engineer-your-data_export_visualization",
    ]);

    public static TheoryData<string> McpFilesystemReadTools => CreateTheoryData(
    [
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
    ]);

    [Theory]
    [MemberData(nameof(ExplicitWriteTools))]
    public void EveryPythonExplicitWriteTool_ResolvesAsWrite(string toolName)
    {
        AssertResolved(toolName, KejiToolAccessLevel.Write);
        Assert.True(KejiLegacyToolPermissionCatalog.IsWriteTool(toolName));
    }

    [Theory]
    [MemberData(nameof(ExplicitReadTools))]
    public void EveryPythonExplicitReadonlyAllowedTool_ResolvesAsRead(string toolName)
    {
        AssertResolved(toolName, KejiToolAccessLevel.Read);
        Assert.False(KejiLegacyToolPermissionCatalog.IsWriteTool(toolName));
    }

    [Theory]
    [MemberData(nameof(WritePrefixExamples))]
    public void EveryPythonWritePrefix_ResolvesMatchingNameAsWrite(string toolName)
    {
        AssertResolved(toolName, KejiToolAccessLevel.Write);
        Assert.True(KejiLegacyToolPermissionCatalog.IsWriteTool(toolName));
    }

    [Theory]
    [MemberData(nameof(McpFilesystemReadTools))]
    public void AllTenMcpFilesystemReadWhitelistNames_ResolveAsRead(string toolName)
    {
        AssertResolved(toolName, KejiToolAccessLevel.Read);
        Assert.False(KejiLegacyToolPermissionCatalog.IsWriteTool(toolName));
    }

    [Theory]
    [InlineData("mcp_filesystem_new_operation")]
    [InlineData("mcp_filesystem_delete_directory")]
    [InlineData("mcp_filesystem_READ_FILE")]
    public void NonWhitelistedMcpFilesystemName_ClassifiesFailSafeAsWrite(string toolName)
    {
        AssertResolved(toolName, KejiToolAccessLevel.Write);
        Assert.True(KejiLegacyToolPermissionCatalog.IsWriteTool(toolName));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("totally_unknown_tool")]
    [InlineData("Read_File")]
    [InlineData("mcp_quack_query_csv")]
    public void UnknownOrdinaryOrInvalidName_DoesNotResolve(
        string? toolName)
    {
        var resolved = KejiLegacyToolPermissionCatalog.TryResolve(toolName!, out var descriptor);

        Assert.False(resolved);
        Assert.Null(descriptor);
    }

    private static void AssertResolved(string toolName, KejiToolAccessLevel expectedAccess)
    {
        var resolved = KejiLegacyToolPermissionCatalog.TryResolve(toolName, out var descriptor);

        Assert.True(resolved);
        var nonNullDescriptor = Assert.IsType<KejiToolPermissionDescriptor>(descriptor);
        Assert.Equal(toolName, nonNullDescriptor.Name);
        Assert.Equal(expectedAccess, nonNullDescriptor.AccessLevel);
    }

    private static TheoryData<string> CreateTheoryData(IEnumerable<string> names)
    {
        var data = new TheoryData<string>();
        foreach (var name in names)
            data.Add(name);
        return data;
    }
}
