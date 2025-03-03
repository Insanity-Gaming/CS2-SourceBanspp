using CounterStrikeSharp.API;
using Dapper;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using Serilog.Core;

namespace SourceBansppBridge;

public class Connection
{
    public delegate void QueryCallbackHandler<T>(IEnumerable<T> data);
    private readonly string connectionString;
    private readonly ILogger _logger;

    public Connection(string address, int port, string database, string user, string password, ILogger logger)
    {
        _logger = logger;
        //"Server=localhost;Database=YourDatabase;User Id=YourUsername;Password=YourPassword;";
        var builder = new MySqlConnectionStringBuilder()
        {
            Server = address,
            Database = database,
            UserID = user,
            Password = password,
            Port = (uint)port,
            Pooling = true,
            MinimumPoolSize = 0,
            MaximumPoolSize = 640,
        };

        connectionString = builder.ConnectionString;

        using var dbConnection = new MySqlConnection(connectionString);
        dbConnection.Open();
        dbConnection.QueryFirstOrDefault<int>("SELECT 1");
        dbConnection.Close();
    }

    public async Task Query<T>(string sql, QueryCallbackHandler<T> callback, object? parameters = null)
    {
        await using var dbConnection = new MySqlConnection(connectionString);
        await dbConnection.OpenAsync();
        try{
            var data=  await dbConnection.QueryAsync<T>(sql, parameters);
            Server.NextFrame(()=>{callback.Invoke(data);});
        } catch (Exception e)
        {
            _logger.LogError(e.Message);
            _logger.LogError(e.StackTrace);
        }
        
        await dbConnection.CloseAsync();
    }

    public async Task Execute(string sql, object? parameters = null)
    {
        await using var dbConnection = new MySqlConnection(connectionString);
        await dbConnection.OpenAsync();
        await dbConnection.ExecuteAsync(sql, parameters);
        await dbConnection.CloseAsync();
    }
}