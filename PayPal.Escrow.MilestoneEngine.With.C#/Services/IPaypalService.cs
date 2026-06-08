using PayPalCheckoutSdk.Orders;

namespace PayPal.Escrow.MilestoneEngine.With.C_.Services
{
    using System.Threading.Tasks;

    namespace PayPal.Escrow.MilestoneEngine.With.C_.Services
    {
        public interface IPaypalService
        {
            // Dönüş tipini tam adıyla (PayPalCheckoutSdk.Orders.Order) tanımladık
            Task<PayPalCheckoutSdk.Orders.Order> CreateEscrowOrderAsync(decimal amount, string currency, string contractId);
        }
    }
}
