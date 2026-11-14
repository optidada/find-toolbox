using Microsoft.Data.SqlClient;

namespace AlloyCms12;

public class Program
{
    public static void Main(string[] args)
    {
        var host = CreateHostBuilder(args).Build();
        EnsureLocalDatabase(host.Services.GetRequiredService<IConfiguration>(), host.Services.GetRequiredService<IWebHostEnvironment>());
        host.Run();
    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureCmsDefaults()
            .ConfigureWebHostDefaults(webBuilder => webBuilder.UseStartup<Startup>());

    /// <summary>
    /// Development only: creates the (empty) LocalDB database on first start.
    /// The CMS then creates its schema and imports App_Data/DefaultSiteContent.episerverdata.
    /// </summary>
    private static void EnsureLocalDatabase(IConfiguration configuration, IWebHostEnvironment environment)
    {
        if (!environment.IsDevelopment())
        {
            return;
        }

        var builder = new SqlConnectionStringBuilder(configuration.GetConnectionString("EPiServerDB"));
        var database = builder.InitialCatalog;
        if (string.IsNullOrEmpty(database) || !string.IsNullOrEmpty(builder.AttachDBFilename))
        {
            return;
        }

        builder.InitialCatalog = "master";
        using var connection = new SqlConnection(builder.ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "IF DB_ID(@name) IS NULL EXEC('CREATE DATABASE [' + @name + ']')";
        command.Parameters.AddWithValue("@name", database);
        command.ExecuteNonQuery();
    }
}
