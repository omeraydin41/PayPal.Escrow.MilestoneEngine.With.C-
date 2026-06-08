namespace PayPal.Escrow.MilestoneEngine.With.C_.Models
{
    public class PaymentResponse
    {
        public string OrderId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string RedirectUrl { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
}
