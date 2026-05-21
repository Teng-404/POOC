namespace POOC.Services
{
    public class SqliteBackupService : BackgroundService
    {
        private readonly ILogger<SqliteBackupService> _logger;
        private readonly string _dbPath;
        private readonly string _backupDir;
        private static readonly TimeSpan Interval = TimeSpan.FromHours(6);
        private const int KeepDays = 7; // เก็บ backup ย้อนหลัง 7 วัน

        public SqliteBackupService(ILogger<SqliteBackupService> logger, IConfiguration config)
        {
            _logger = logger;
            _dbPath = "loan_data.db";
            _backupDir = Path.Combine(AppContext.BaseDirectory, "backups");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            Directory.CreateDirectory(_backupDir);

            // backup ทันทีตอนเริ่ม app
            DoBackup();

            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(Interval, stoppingToken);
                DoBackup();
            }
        }

        private void DoBackup()
        {
            try
            {
                if (!File.Exists(_dbPath)) return;

                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var dest = Path.Combine(_backupDir, $"loan_data_{timestamp}.db");
                File.Copy(_dbPath, dest, overwrite: true);
                _logger.LogInformation("SQLite backup สำเร็จ: {File}", dest);

                // ลบไฟล์ที่เก่าเกิน KeepDays
                var cutoff = DateTime.Now.AddDays(-KeepDays);
                foreach (var old in Directory.GetFiles(_backupDir, "loan_data_*.db")
                    .Select(f => new FileInfo(f))
                    .Where(f => f.CreationTime < cutoff))
                {
                    old.Delete();
                    _logger.LogInformation("ลบ backup เก่า: {File}", old.Name);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Backup ล้มเหลว");
            }
        }
    }
}