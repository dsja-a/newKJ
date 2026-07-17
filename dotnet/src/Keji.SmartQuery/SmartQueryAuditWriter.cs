using System.Text;
using Keji.Auditing.Abstractions;
using Keji.Auditing.Models;

namespace Keji.SmartQuery;

internal sealed class KejiSmartQueryAuditWriter(IKejiAuditService audit)
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    internal async Task<bool> WriteAsync(
        string action, KejiAuditOutcome outcome, string sourceId,
        IReadOnlyDictionary<string, string> metadata)
    {
        if (metadata.Count > 24) return false;
        var bounded = new Dictionary<string, string>(StringComparer.Ordinal);
        var bytes = 0;
        foreach (var pair in metadata)
        {
            if (pair.Key.Length is < 1 or > 64 || pair.Value.Length > 256) return false;
            try { bytes += Utf8.GetByteCount(pair.Key) + Utf8.GetByteCount(pair.Value); }
            catch (EncoderFallbackException) { return false; }
            if (bytes > 8192) return false;
            bounded[pair.Key] = pair.Value;
        }
        try
        {
            var result = await audit.WriteAsync(
                KejiAuditCategory.DataAccess, action, outcome,
                outcome == KejiAuditOutcome.Success ? KejiAuditSeverity.Information : KejiAuditSeverity.Warning,
                "smart_query_data_source", SafeId(sourceId), bounded, CancellationToken.None).ConfigureAwait(false);
            return result == KejiAuditResult.Written;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return false;
        }
    }
    private static string SafeId(string value) =>
        KejiSmartQueryValidation.IsIdentifier(value, 128) ? value : "";
}
