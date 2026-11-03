using Microsoft.Data.Sqlite;
using Pos.Branch.Infrastructure.Sqlite;

namespace Pos.Branch.Api.Tests;

public sealed class SqliteBranchSoldOutQueryTests
{
    [Fact]
    public async Task Reads_current_branch_overrides_and_searches_the_active_catalog_without_writing()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pos-sold-out-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "branch.db");
        try
        {
            await new SqliteTableSessionCommandStore(path).InitializeAsync();
            await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
            {
                await connection.OpenAsync();
                using var command = connection.CreateCommand();
                command.CommandText = """
                    INSERT INTO master_data_packages
                    VALUES ('B1','V1',1,printf('%064d',0),'2026-11-01','2026-11-01','Active');
                    INSERT INTO master_data_sellable_items
                      (branch_id,master_data_version,sellable_item_id,item_kind,display_name,central_active,branch_catalog_enabled,production_station_id)
                    VALUES ('B1','V1','I1','Item','ข้าวผัด',1,1,'KITCHEN');
                    INSERT INTO branch_sold_out_overrides VALUES
                      ('B1','I1',1,'EMP1','DEVICE1','2026-11-02T00:00:00Z'),
                      ('B1','I2',1,'EMP2','DEVICE1','2026-11-01T00:00:00Z'),
                      ('B1','I3',0,'EMP1','DEVICE1','2026-11-03T00:00:00Z'),
                      ('B2','I1',1,'EMP3','DEVICE2','2026-11-03T00:00:00Z');
                    """;
                await command.ExecuteNonQueryAsync();
            }
            var before = await File.ReadAllBytesAsync(path);
            var query = new SqliteBranchSoldOutQuery(path);
            var (total, rows) = await query.ReadAsync("B1", "", 0, CancellationToken.None);
            Assert.Equal(2, total);
            Assert.Collection(rows, row =>
            {
                Assert.Equal("ข้าวผัด", row.DisplayName);
                Assert.Equal("KITCHEN", row.StationId);
                Assert.Equal("EMP1", row.ChangedByEmployeeId);
            }, row =>
            {
                Assert.Equal("I2", row.SellableItemId);
                Assert.Null(row.DisplayName);
            });
            Assert.Single((await query.ReadAsync("B1", "ข้าว", 0, CancellationToken.None)).Items);
            Assert.Empty((await query.ReadAsync("B1", "%", 0, CancellationToken.None)).Items);
            Assert.Equal("I2", Assert.Single((await query.ReadAsync("B1", "", 1, CancellationToken.None)).Items).SellableItemId);
            Assert.Equal(before, await File.ReadAllBytesAsync(path));
        }
        finally
        {
            foreach (var file in new[] { path, path + "-wal", path + "-shm" }) { File.Delete(file); }
            if (Directory.Exists(directory)) { Directory.Delete(directory); }
        }
    }
}
