using Serilog.Core;
using Serilog.Events;

namespace Maxkeys.Api.Logging;

/// <summary>
/// Reduces sensitive types to their id/status fields before Serilog serializes
/// them, so a key code (or full order detail carrying decrypted keys) can never
/// end up in a log sink (design sections 8/12). Matches by type name rather than
/// a hard reference to <c>AttachKeyRequest</c>/<c>OrderDetail</c> so the policy
/// already covers those PR10 types once they exist, without another edit here.
/// </summary>
public sealed class SensitiveDataPolicy : IDestructuringPolicy
{
    private static readonly HashSet<string> MaskedTypeNames = new(StringComparer.Ordinal)
    {
        "Key",
        "AttachKeyRequest",
        "OrderDetail",
        "MyOrderDetail",
        "AdminOrderSummary",
    };

    public bool TryDestructure(object value, ILogEventPropertyValueFactory propertyValueFactory, out LogEventPropertyValue result)
    {
        var typeName = value.GetType().Name;
        if (!MaskedTypeNames.Contains(typeName))
        {
            result = null!;
            return false;
        }

        result = new StructureValue(new[]
        {
            new LogEventProperty("Type", new ScalarValue(typeName)),
            new LogEventProperty("Redacted", new ScalarValue(true)),
        });
        return true;
    }
}
