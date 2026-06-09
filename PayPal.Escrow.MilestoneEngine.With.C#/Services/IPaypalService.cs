using System.Threading.Tasks;
using PayPalCheckoutSdk.Orders;

namespace PayPal.Escrow.MilestoneEngine.With.C_.Services
{
    public interface IPaypalService
    {
        Task<Order> CreateEscrowOrderAsync(decimal amount, string currency, string contractId);
        // IPaypalService.cs içerisine eklenecek:
        Task<PayPalCheckoutSdk.Orders.Order> CaptureEscrowOrderAsync(string orderId);
    }
}