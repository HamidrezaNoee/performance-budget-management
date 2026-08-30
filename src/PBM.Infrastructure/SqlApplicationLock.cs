using System.Data;
using Microsoft.EntityFrameworkCore;

namespace PBM.Infrastructure;

public sealed class SqlApplicationLock(PbmDbContext db)
{
    public async Task AcquireAsync(string resource, CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = "DECLARE @r int; EXEC @r = sp_getapplock @Resource=@resource, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000; SELECT @r;";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@resource";
        parameter.Value = resource;
        command.Parameters.Add(parameter);
        var result = Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken),
            System.Globalization.CultureInfo.InvariantCulture);
        if (result < 0)
            throw new TimeoutException("Could not acquire the SQL Server application lock in time.");
    }
}
