namespace PayPal.Escrow.MilestoneEngine.With.C_.Models
{
    public class CorporatePaymentRequest
    {
        public decimal TotalAmount { get; set; }
        public string Currency { get; set; } = "USD";
        public string ContractId { get; set; } = string.Empty;
    }
}
