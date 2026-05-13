using System.Data;
using Microsoft.EntityFrameworkCore;

namespace POOC.Data
{
    public static class DatabaseInitializer
    {
        public static void EnsureColumn(ApplicationDbContext context, string tableName, string columnName, string columnDefinition)
        {
            if (ColumnExists(context, tableName, columnName))
            {
                return;
            }

            context.Database.ExecuteSqlRaw($"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition}");
        }

        private static bool ColumnExists(ApplicationDbContext context, string tableName, string columnName)
        {
            var connection = context.Database.GetDbConnection();
            var shouldClose = connection.State == ConnectionState.Closed;

            if (shouldClose)
            {
                connection.Open();
            }

            try
            {
                using var command = connection.CreateCommand();
                command.CommandText = $"PRAGMA table_info({tableName})";

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    if (string.Equals(reader["name"]?.ToString(), columnName, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                return false;
            }
            finally
            {
                if (shouldClose)
                {
                    connection.Close();
                }
            }
        }
    }
}