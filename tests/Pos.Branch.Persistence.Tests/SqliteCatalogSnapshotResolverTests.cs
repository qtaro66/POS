using Microsoft.Data.Sqlite;
using Pos.Application.MasterData;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Infrastructure.Sqlite.Tests;

public sealed class SqliteCatalogSnapshotResolverTests
{
    [Fact]
    public async Task Resolves_price_and_structure_from_session_version_but_current_sellability_from_active_version()
    {
        await using var database = new CatalogDatabase();
        await database.InitializeAndSeedAsync();

        var result = await database.ResolveAsync(Request());

        Assert.True(result.Accepted);
        Assert.Equal("MASTER-OLD", result.Selection!.MasterDataVersion);
        Assert.Equal("ชื่อเดิม", result.Selection.DisplayName);
        Assert.Equal(100m, result.Selection.UnitPrice);
        Assert.Equal("KITCHEN-OLD", result.Selection.ProductionStationId);
        var option = Assert.Single(result.Selection.Options);
        Assert.Equal("ไก่เดิม", option.DisplayName);
        Assert.Equal(10m, option.UnitPrice);
    }

    [Fact]
    public async Task Rejects_selected_option_when_branch_override_marks_it_sold_out()
    {
        await using var database = new CatalogDatabase();
        await database.InitializeAndSeedAsync();
        await database.ExecuteAsync("""
            INSERT INTO branch_sold_out_overrides (
                branch_id, sellable_item_id, is_sold_out,
                changed_by_employee_id, changed_by_device_id, changed_at)
            VALUES ('BRANCH-01', 'OPT-1', 1, 'EMP-1', 'DEVICE-1',
                    '2026-10-13T10:00:00.0000000+00:00');
            """);

        var result = await database.ResolveAsync(Request());

        Assert.False(result.Accepted);
        Assert.Contains(
            result.Errors,
            error => error.Code == CatalogValidationErrorCode.OptionSoldOut &&
                     error.SellableItemId == "OPT-1");
    }

    [Fact]
    public async Task Rejects_frozen_item_when_current_active_package_disables_it()
    {
        await using var database = new CatalogDatabase();
        await database.InitializeAndSeedAsync();
        await database.ExecuteAsync("""
            UPDATE master_data_sellable_items
            SET branch_catalog_enabled = 0
            WHERE branch_id = 'BRANCH-01'
              AND master_data_version = 'MASTER-NEW'
              AND sellable_item_id = 'ITEM-1';
            """);

        var result = await database.ResolveAsync(Request());

        Assert.False(result.Accepted);
        Assert.Contains(
            result.Errors,
            error => error.Code == CatalogValidationErrorCode.ItemBranchDisabled);
    }

    [Fact]
    public async Task Missing_session_snapshot_is_rejected_without_falling_back_to_active_package()
    {
        await using var database = new CatalogDatabase();
        await database.InitializeAndSeedAsync();

        var result = await database.ResolveAsync(new CatalogSelectionRequest(
            "BRANCH-01",
            "UNKNOWN-SESSION",
            "ITEM-1",
            1));

        Assert.False(result.Accepted);
        var error = Assert.Single(result.Errors);
        Assert.Equal(CatalogValidationErrorCode.CatalogSnapshotUnavailable, error.Code);
    }

    [Fact]
    public async Task Current_schema_creates_catalog_and_sold_out_tables()
    {
        await using var database = new CatalogDatabase();
        await database.InitializeAsync();

        Assert.Equal(17L, await database.ScalarAsync<long>("PRAGMA user_version;"));
        Assert.Equal(1L, await database.ScalarAsync<long>("""
            SELECT COUNT(*) FROM sqlite_master
            WHERE type = 'table' AND name = 'master_data_sellable_items';
            """));
        Assert.Equal(1L, await database.ScalarAsync<long>("""
            SELECT COUNT(*) FROM sqlite_master
            WHERE type = 'table' AND name = 'master_data_option_groups';
            """));
        Assert.Equal(1L, await database.ScalarAsync<long>("""
            SELECT COUNT(*) FROM sqlite_master
            WHERE type = 'table' AND name = 'branch_sold_out_overrides';
            """));
        Assert.Equal(1L, await database.ScalarAsync<long>("""
            SELECT COUNT(*) FROM sqlite_master
            WHERE type = 'table' AND name = 'branch_sold_out_audit';
            """));
        Assert.Equal(1L, await database.ScalarAsync<long>("""
            SELECT COUNT(*) FROM sqlite_master
            WHERE type = 'table' AND name = 'production_order_snapshots';
            """));
    }

    private static CatalogSelectionRequest Request() =>
        new(
            "BRANCH-01",
            "TS-1",
            "ITEM-1",
            1,
            [new CatalogOptionSelection("GROUP-1", "OPT-1", 1)]);

    private sealed class CatalogDatabase : IAsyncDisposable
    {
        private readonly string _directory = Path.Combine(
            Path.GetTempPath(),
            "pos-catalog-tests",
            Guid.NewGuid().ToString("N"));

        private string DatabasePath => Path.Combine(_directory, "branch.db");

        public async Task InitializeAsync()
        {
            var store = new SqliteTableSessionCommandStore(DatabasePath);
            await store.InitializeAsync();
        }

        public async Task InitializeAndSeedAsync()
        {
            await InitializeAsync();
            await ExecuteAsync($$"""
                INSERT INTO master_data_packages (
                    branch_id, master_data_version, package_schema_version, checksum_sha256,
                    published_at, activated_at, lifecycle_status)
                VALUES
                    ('BRANCH-01', 'MASTER-OLD', 1, '{{new string('A', 64)}}',
                     '2026-10-12T08:00:00.0000000+00:00',
                     '2026-10-12T09:00:00.0000000+00:00', 'Previous'),
                    ('BRANCH-01', 'MASTER-NEW', 1, '{{new string('B', 64)}}',
                     '2026-10-13T08:00:00.0000000+00:00',
                     '2026-10-13T09:00:00.0000000+00:00', 'Active');

                INSERT INTO master_data_billing_policy (
                    branch_id, master_data_version, calculation_policy_version,
                    vat_rate, service_charge_rate, currency_scale, midpoint_rounding)
                VALUES
                    ('BRANCH-01', 'MASTER-OLD', 'CALC-OLD', '7', '0', 2, 'AwayFromZero'),
                    ('BRANCH-01', 'MASTER-NEW', 'CALC-NEW', '7', '0', 2, 'AwayFromZero');

                INSERT INTO table_sessions (
                    branch_id, table_session_id, table_id, status, version)
                VALUES ('BRANCH-01', 'TS-1', 'A04', 'Ordering', 1);

                INSERT INTO table_session_billing_policy (
                    branch_id, table_session_id, master_data_version,
                    calculation_policy_version, vat_rate, service_charge_rate,
                    currency_scale, midpoint_rounding, created_at)
                VALUES ('BRANCH-01', 'TS-1', 'MASTER-OLD', 'CALC-OLD', '7', '0',
                        2, 'AwayFromZero', '2026-10-12T10:00:00.0000000+00:00');

                INSERT INTO master_data_sellable_items (
                    branch_id, master_data_version, sellable_item_id, item_kind,
                    display_name, resolved_price, central_active, branch_catalog_enabled,
                    production_station_id)
                VALUES
                    ('BRANCH-01', 'MASTER-OLD', 'ITEM-1', 'Item', 'ชื่อเดิม', '100', 1, 1, 'KITCHEN-OLD'),
                    ('BRANCH-01', 'MASTER-OLD', 'OPT-1', 'SubItem', 'ไก่เดิม', '10', 1, 1, NULL),
                    ('BRANCH-01', 'MASTER-NEW', 'ITEM-1', 'Item', 'ชื่อใหม่', '150', 1, 1, 'KITCHEN-NEW'),
                    ('BRANCH-01', 'MASTER-NEW', 'OPT-1', 'SubItem', 'ไก่ใหม่', '20', 1, 1, NULL);

                INSERT INTO master_data_option_groups (
                    branch_id, master_data_version, parent_sellable_item_id,
                    option_group_id, display_name, option_type, min_selections,
                    max_selections, allow_repeated_option, max_quantity_per_option,
                    display_order)
                VALUES ('BRANCH-01', 'MASTER-OLD', 'ITEM-1', 'GROUP-1',
                        'เลือกวัตถุดิบหลัก', 'SubItem', 1, 1, 0, 1, 1);

                INSERT INTO master_data_option_group_items (
                    branch_id, master_data_version, parent_sellable_item_id,
                    option_group_id, option_sellable_item_id, display_order, is_default)
                VALUES ('BRANCH-01', 'MASTER-OLD', 'ITEM-1', 'GROUP-1', 'OPT-1', 1, 0);
                """);
        }

        public async Task<CatalogResolutionResult> ResolveAsync(CatalogSelectionRequest request)
        {
            await using var connection = Connection(SqliteOpenMode.ReadWrite);
            await connection.OpenAsync();
            await using var transaction = connection.BeginTransaction(deferred: false);
            var result = await new SqliteCatalogSnapshotResolver().ResolveAsync(
                connection,
                transaction,
                request,
                CancellationToken.None);
            await transaction.CommitAsync();
            return result;
        }

        public async Task ExecuteAsync(string sql)
        {
            Directory.CreateDirectory(_directory);
            await using var connection = Connection(SqliteOpenMode.ReadWriteCreate);
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
        }

        public async Task<T> ScalarAsync<T>(string sql)
        {
            await using var connection = Connection(SqliteOpenMode.ReadOnly);
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            var result = await command.ExecuteScalarAsync();
            return (T)Convert.ChangeType(
                result!,
                typeof(T),
                System.Globalization.CultureInfo.InvariantCulture);
        }

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }

            return ValueTask.CompletedTask;
        }

        private SqliteConnection Connection(SqliteOpenMode mode) =>
            new(new SqliteConnectionStringBuilder
            {
                DataSource = DatabasePath,
                Mode = mode,
                ForeignKeys = true,
                Pooling = false
            }.ToString());
    }
}
