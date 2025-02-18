using GrandChessTree.Shared.Api;
using Npgsql;

namespace GrandChessTree.Toolkit
{
    public static class AccountSeeder
    {
        public static async Task Seed()
        {
            Console.WriteLine("Enter PostgreSQL connection string...");
            var connectionString = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                Console.WriteLine("Error: Connection string cannot be empty.");
                return;
            }

            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            Console.Write("Enter account name: ");
            var name = Console.ReadLine()?.Trim() ?? string.Empty;

            Console.Write("Enter email: ");
            var email = Console.ReadLine()?.Trim() ?? string.Empty;

            Console.Write("Enter role (User/Admin): ");
            var roleInput = Console.ReadLine()?.Trim() ?? "User";
            if (!Enum.TryParse(roleInput, true, out AccountRole role))
            {
                Console.WriteLine("Invalid role. Defaulting to User.");
                role = AccountRole.User;
            }

            const string insertQuery = @"
            INSERT INTO accounts (name, email, role) 
            VALUES (@name, @email, @role) 
            RETURNING id;";

            await using var cmd = new NpgsqlCommand(insertQuery, conn);
            cmd.Parameters.AddWithValue("name", name);
            cmd.Parameters.AddWithValue("email", email);
            cmd.Parameters.AddWithValue("role", role.ToString());

            var insertedId = await cmd.ExecuteScalarAsync();
            Console.WriteLine($"Account created successfully with ID: {insertedId}");
        }
    }
}
