
using Microsoft.Extensions.Options;
using PayPal.Escrow.MilestoneEngine.With.C_.Configurations;

using PayPalCheckoutSdk.Core;
using PayPalCheckoutSdk.Orders;

namespace PayPal.Escrow.MilestoneEngine.With.C_.Services
{
    public class PaypalService : IPaypalService
    {
        private readonly PayPalHttpClient _client;

        public PaypalService(IOptions<PaypalSettings> paypalSettings)
        {
            var settings = paypalSettings.Value;

            // Kurumsal Sandbox ortamı API anahtarlarıyla kuruluyor
            PayPalEnvironment environment = new SandboxEnvironment(settings.ClientId, settings.SecretKey);
            _client = new PayPalHttpClient(environment);
        }

        public async Task<PayPalCheckoutSdk.Orders.Order> CreateEscrowOrderAsync(decimal amount, string currency, string contractId)
        {
            var orderRequest = new OrderRequest()
            {
                CheckoutPaymentIntent = "CAPTURE", // Parayı güvenli havuz sürecine dahil etmek için tetikleyici bağlam
                PurchaseUnits = new List<PurchaseUnitRequest>
            {
                new PurchaseUnitRequest
                {
                    ReferenceId = contractId, // Sözleşme mühür numarası
                    AmountWithBreakdown = new AmountWithBreakdown
                    {
                        CurrencyCode = currency,
                        Value = amount.ToString("F2") // Örn: 5000.00
                    },
                    Description = $"Sözleşme No: {contractId} - Güvenceli Havuz Ödemesi"
                }
            },
                ApplicationContext = new ApplicationContext
                {
                    ReturnUrl = "https://localhost:5001/api/payment/success",
                    CancelUrl = "https://localhost:5001/api/payment/cancel"
                }
            };

            var request = new OrdersCreateRequest();
            request.Prefer("return=representation");
            request.RequestBody(orderRequest);

            // PayPal sunucularına güvenli istek atılıyor
            var response = await _client.Execute(request);
            return response.Result<Order>();
        }

        // PaypalService.cs içerisine eklenecek metod:
        public async Task<PayPalCheckoutSdk.Orders.Order> CaptureEscrowOrderAsync(string orderId)
        {
            var request = new OrdersCaptureRequest(orderId);
            request.RequestBody(new OrderActionRequest());

            // PayPal havuzunda parayı bloke ediyoruz
            var response = await _client.Execute(request);
            return response.Result<Order>();
        }
    }
}
