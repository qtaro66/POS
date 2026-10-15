using Microsoft.Data.Sqlite;
using Pos.Printing.Abstractions;

namespace Pos.Branch.Infrastructure.Sqlite;

public sealed class SqlitePrinterConfigurationProvider : IPrinterConfigurationProvider
{
    private readonly string _databasePath;

    public SqlitePrinterConfigurationProvider(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException("The database path cannot be empty.", nameof(databasePath));
        }

        _databasePath = Path.GetFullPath(databasePath);
    }

    public async Task<PrinterProfile?> FindActivePrinterAsync(
        string branchId,
        string productionStationId,
        CancellationToken cancellationToken)
    {
        branchId = Require(branchId, nameof(branchId));
        productionStationId = Require(productionStationId, nameof(productionStationId));
        await using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = _databasePath,
                Mode = SqliteOpenMode.ReadOnly,
                ForeignKeys = true,
                Pooling = false
            }.ToString());
        await connection.OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT printer_id, display_name, branch_id, production_station_id,
                   host, port, protocol, compatibility_profile_id,
                   manufacturer, model, product_code, firmware_version,
                   paper_width_millimeters, dpi, printable_width_dots,
                   render_mode, thai_encoding_mode, status_mode, supports_cash_drawer,
                   cutter_command_hex, drawer_pulse_command_hex, status_command_profile,
                   connect_timeout_ms, send_timeout_ms,
                   maximum_automatic_retries_before_send, automatic_retry_backoff_ms
            FROM printer_profiles
            WHERE branch_id = $branchId
              AND production_station_id = $productionStationId
              AND is_active = 1;
            """;
        command.Parameters.AddWithValue("$branchId", branchId);
        command.Parameters.AddWithValue("$productionStationId", productionStationId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var profile = new PrinterProfile(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetInt32(5),
            ParseEnum<PrinterProtocol>(reader.GetString(6)),
            reader.GetString(7),
            reader.GetString(8),
            reader.GetString(9),
            reader.IsDBNull(10) ? null : reader.GetString(10),
            reader.IsDBNull(11) ? null : reader.GetString(11),
            reader.GetInt32(12),
            reader.GetInt32(13),
            reader.GetInt32(14),
            ParseEnum<PrinterRenderMode>(reader.GetString(15)),
            ParseEnum<PrinterThaiEncodingMode>(reader.GetString(16)),
            ParseEnum<PrinterStatusMode>(reader.GetString(17)),
            reader.GetInt32(18) == 1,
            reader.IsDBNull(19) ? null : reader.GetString(19),
            reader.IsDBNull(20) ? null : reader.GetString(20),
            reader.IsDBNull(21) ? null : reader.GetString(21),
            TimeSpan.FromMilliseconds(reader.GetInt32(22)),
            TimeSpan.FromMilliseconds(reader.GetInt32(23)),
            reader.GetInt32(24),
            TimeSpan.FromMilliseconds(reader.GetInt32(25)));
        if (await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException(
                "More than one active printer is configured for the production station.");
        }

        return profile;
    }

    private static TEnum ParseEnum<TEnum>(string value)
        where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(value, ignoreCase: false, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new InvalidOperationException(
                $"The database value '{value}' is not a valid {typeof(TEnum).Name}.");

    private static string Require(string value, string name) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("The value cannot be empty.", name)
            : value.Trim();
}
