namespace POOC.Models
{
    public class AccountingIndexViewModel
    {
        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }
        public AccountingSummary Summary { get; set; } = new();
        public List<AccountingLedgerRow> LedgerRows { get; set; } = new();
        public List<AccountingDocumentRow> Documents { get; set; } = new();
    }

    public class AccountingSummary
    {
        public decimal SavingsDeposits { get; set; }
        public decimal SavingsWithdrawals { get; set; }
        public decimal SavingsInterestPaid { get; set; }
        public decimal LoanDisbursed { get; set; }
        public decimal LoanPrincipalCollected { get; set; }
        public decimal LoanInterestCollected { get; set; }
        public decimal OutstandingLoanPrincipal { get; set; }
        public decimal CashInflow => SavingsDeposits + LoanPrincipalCollected + LoanInterestCollected;
        public decimal CashOutflow => SavingsWithdrawals + SavingsInterestPaid + LoanDisbursed;
        public decimal NetCashMovement => CashInflow - CashOutflow;
    }

    public class AccountingLedgerRow
    {
        public DateTime Date { get; set; }
        public string DocumentNo { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string MemberName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
    }

    public class AccountingDocumentRow
    {
        public DateTime Date { get; set; }
        public string DocumentNo { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string MemberName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string DownloadUrl { get; set; } = string.Empty;
    }
}
