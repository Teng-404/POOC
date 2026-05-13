using System.Data;
using Microsoft.EntityFrameworkCore;

namespace POOC.Data
{
    public static class DatabaseInitializer
    {
        private static readonly HashSet<string> AllowedColumnDefinitions = new(StringComparer.OrdinalIgnoreCase)
        {
            "INTEGER NOT NULL DEFAULT 0",
            "TEXT NULL",
            "TEXT NOT NULL DEFAULT 'Active'"
        };

        public static void EnsureColumn(ApplicationDbContext context, string tableName, string columnName, string columnDefinition)
        {
            var safeTableName = QuoteIdentifier(tableName);
            var safeColumnName = QuoteIdentifier(columnName);
            var safeColumnDefinition = ValidateColumnDefinition(columnDefinition);

            if (ColumnExists(context, safeTableName, columnName))
            {
                return;
            }

            var sql = string.Concat(
                "ALTER TABLE ",
                safeTableName,
                " ADD COLUMN ",
                safeColumnName,
                " ",
                safeColumnDefinition);

            context.Database.ExecuteSqlRaw(sql);
        }

        private static bool ColumnExists(ApplicationDbContext context, string safeTableName, string columnName)
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
                command.CommandText = $"PRAGMA table_info({safeTableName})";

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

        private static string QuoteIdentifier(string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier) || identifier.Any(c => !char.IsLetterOrDigit(c) && c != '_'))
            {
                throw new ArgumentException("Invalid SQL identifier.", nameof(identifier));
            }

            return $"\"{identifier}\"";
        }

        private static string ValidateColumnDefinition(string columnDefinition)
        {
            if (!AllowedColumnDefinitions.Contains(columnDefinition))
            {
                throw new ArgumentException("Invalid SQL column definition.", nameof(columnDefinition));
            }

            return columnDefinition;
        }
    }
}