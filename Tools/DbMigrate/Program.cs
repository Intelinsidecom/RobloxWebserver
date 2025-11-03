using System.Reflection;
using DbUp;
using Microsoft.Extensions.Configuration;
using Npgsql;

internal class Program
{
    private static int Main(string[] args)
    {
        // Args: --connection <connString> [--env Development|Production]
        string? connArg = null;
        string environment = "Development";
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--connection" && i + 1 < args.Length)
            {
                connArg = args[++i];
            }
            else if (args[i] == "--env" && i + 1 < args.Length)
            {
                environment = args[++i];
            }
        }

        // Resolve solution root (two levels up from Tools/DbMigrate/bin/...)
        var exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var projectDir = Path.GetFullPath(Path.Combine(exeDir, "..", "..", ".."));
        var repoRoot = Path.GetFullPath(Path.Combine(projectDir, "..", ".."));

        // Load connection from this tool's appsettings.{env}.json if not provided
        string? connectionString = connArg;
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            var toolDir = projectDir; // Tools/DbMigrate
            var config = new ConfigurationBuilder()
                .SetBasePath(toolDir)
                .AddJsonFile("appsettings.json", optional: true)
                .AddJsonFile($"appsettings.{environment}.json", optional: true)
                .AddEnvironmentVariables()
                .Build();
            connectionString = config.GetConnectionString("Default");
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.Error.WriteLine("Connection string not found. Provide --connection or configure Tools/DbMigrate/appsettings*.json (ConnectionStrings:Default).");
            return 2;
        }

        // Scripts folder: <repoRoot>/Database/Sql
        var scriptsPath = Path.Combine(repoRoot, "Database", "Sql");
        if (!Directory.Exists(scriptsPath))
        {
            Console.Error.WriteLine($"Scripts folder not found: {scriptsPath}");
            return 3;
        }

        Console.WriteLine("Select an action:");
        Console.WriteLine("1) Migrate/Update database");
        Console.WriteLine("2) List tables");
        Console.WriteLine("3) WIPE database (DROP ALL TABLES) - DANGEROUS");
        Console.Write("Enter choice (1-3): ");
        var key = Console.ReadKey(intercept: true).KeyChar;
        Console.WriteLine();

        if (key == '1')
        {
            // Ensure DB exists and run migrations (journal table: schemaversions)
            try
            {
                EnsureDatabase.For.PostgresqlDatabase(connectionString);

                var upgrader = DeployChanges.To
                    .PostgresqlDatabase(connectionString)
                    .WithScriptsFromFileSystem(scriptsPath)
                    .LogToConsole()
                    .Build();

                var result = upgrader.PerformUpgrade();
                if (!result.Successful)
                {
                    Console.Error.WriteLine(result.Error);
                    return -1;
                }

                Console.WriteLine("Success! Database is up to date.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return -1;
            }
        }
        else if (key == '2')
        {
            try
            {
                using var conn = new NpgsqlConnection(connectionString);
                conn.Open();
                const string sql = @"select table_schema, table_name
                                      from information_schema.tables
                                      where table_type='BASE TABLE'
                                        and table_schema not in ('pg_catalog','information_schema')
                                      order by table_schema, table_name";
                using var cmd = new NpgsqlCommand(sql, conn);
                using var reader = cmd.ExecuteReader();
                int count = 0;
                Console.WriteLine("Tables:");
                while (reader.Read())
                {
                    count++;
                    var schema = reader.GetString(0);
                    var name = reader.GetString(1);
                    Console.WriteLine($"- {schema}.{name}");
                }
                if (count == 0)
                {
                    Console.WriteLine("(no tables)");
                }
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return -1;
            }
        }
        else if (key == '3')
        {
            // Destructive wipe: drop and recreate public schema
            Console.WriteLine("\nWARNING: You are about to DROP ALL OBJECTS in this database (schema 'public').");
            Console.WriteLine("This operation is IRREVERSIBLE and will DELETE ALL TABLES, VIEWS, FUNCTIONS, DATA.");
            Console.WriteLine($"Connection: {connectionString}");
            Console.WriteLine();
            Console.Write("Type 'WIPE' to continue (or anything else to cancel): ");
            var confirm1 = Console.ReadLine();
            if (!string.Equals(confirm1, "WIPE", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Cancelled.");
                return 1;
            }
            Console.Write("Type the target database name to confirm: ");
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            var expectedDb = builder.Database;
            var confirmDb = Console.ReadLine();
            if (!string.Equals(confirmDb, expectedDb, StringComparison.Ordinal))
            {
                Console.WriteLine("Database name did not match. Cancelled.");
                return 1;
            }
            Console.Write("FINAL CONFIRMATION: Type 'DROP' to proceed: ");
            var confirm2 = Console.ReadLine();
            if (!string.Equals(confirm2, "DROP", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Cancelled.");
                return 1;
            }

            try
            {
                using var conn = new NpgsqlConnection(connectionString);
                conn.Open();
                using var tx = conn.BeginTransaction();
                using (var cmd = new NpgsqlCommand("DROP SCHEMA IF EXISTS public CASCADE;", conn, tx))
                    cmd.ExecuteNonQuery();
                using (var cmd = new NpgsqlCommand("CREATE SCHEMA public;", conn, tx))
                    cmd.ExecuteNonQuery();
                // Restore common privileges
                using (var cmd = new NpgsqlCommand("GRANT ALL ON SCHEMA public TO postgres;", conn, tx))
                    cmd.ExecuteNonQuery();
                using (var cmd = new NpgsqlCommand("GRANT ALL ON SCHEMA public TO public;", conn, tx))
                    cmd.ExecuteNonQuery();
                tx.Commit();
                Console.WriteLine("Database schema 'public' has been wiped and recreated.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return -1;
            }
        }
        else
        {
            Console.WriteLine("Invalid choice.");
            return 1;
        }
    }
}
