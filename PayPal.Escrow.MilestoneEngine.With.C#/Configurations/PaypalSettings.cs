namespace PayPal.Escrow.MilestoneEngine.With.C_.Configurations
{
    public class PaypalSettings
    {
        public string ClientId { get; set; } = string.Empty;
        public string SecretKey { get; set; } = string.Empty;
        public string Environment { get; set; } = "Sandbox"; // Geliştirme aşaması için varsayılan Sandbox
    }
}
