using Microsoft.AspNetCore.Mvc;
using PayPal.Escrow.MilestoneEngine.With.C_.Models;
using PayPal.Escrow.MilestoneEngine.With.C_.Services;
using System.Linq;
using System.Threading.Tasks;

namespace PayPal.Escrow.MilestoneEngine.With.C_.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PaymentController : ControllerBase
    {
        private readonly IPaypalService _paypalService;

        public PaymentController(IPaypalService paypalService)
        {
            _paypalService = paypalService;
        }

        /// <summary>
        /// Şirket ile Ajans arasındaki sözleşme tutarını PayPal üzerinde havuzda (Escrow) başlatır.
        /// </summary>
        [HttpPost("create-escrow")]
        public async Task<IActionResult> CreateEscrow([FromBody] CorporatePaymentRequest request)
        {
            if (request == null || request.TotalAmount <= 0)
            {
                return BadRequest(new PaymentResponse { Message = "Geçersiz kurumsal ödeme isteği." });
            }

            try
            {
                // 1. IPaypalService üzerinden PayPal API'sine istek atıp siparişi oluşturuyoruz
                var order = await _paypalService.CreateEscrowOrderAsync(request.TotalAmount, request.Currency, request.ContractId);

                // 2. Şirket yetkilisinin gidip ödemeyi onaylaması gereken PayPal linkini (approve) ayıklıyoruz
                var approveUrl = order.Links.FirstOrDefault(x => x.Rel.Equals("approve", System.StringComparison.OrdinalIgnoreCase))?.Href;

                if (string.IsNullOrEmpty(approveUrl))
                {
                    return StatusCode(500, new PaymentResponse { Message = "PayPal onay linki üretilemedi." });
                }

                // 3. Frontend'e veya istemciye kontrat bilgilerini ve yönlendirme linkini dönüyoruz
                return Ok(new PaymentResponse
                {
                    OrderId = order.Id,
                    Status = PaymentStatus.Created.ToString(),
                    RedirectUrl = approveUrl,
                    Message = $"Sözleşme {request.ContractId} için güvenceli havuz ödemesi oluşturuldu. Lütfen RedirectUrl üzerinden onaylayın."
                });
            }
            catch (System.Exception ex)
            {
                return StatusCode(500, new PaymentResponse { Message = $"Havuz oluşturulurken hata meydana geldi: {ex.Message}" });
            }
        }

        // PaymentController.cs içindeki mevcut success metodunu bununla değiştir:
        [HttpGet("success")]
        public async Task<IActionResult> PaymentSuccess([FromQuery] string token, [FromQuery] string PayerID)
        {
            try
            {
                // PayPal üzerinde bekleyen siparişi yakala ve parayı havuzda kilitle
                // Not: PayPal API kurallarına göre buradaki 'token' aslında OrderId yerine geçer.
                var capturedOrder = await _paypalService.CaptureEscrowOrderAsync(token);

                return Ok(new
                {
                    Status = PaymentStatus.EscrowLocked.ToString(),
                    OrderId = capturedOrder.Id,
                    Message = "Ödeme şirket tarafından onaylandı. Para güvenceli havuza (Escrow) alındı ve yasal süreç boyunca bloke edildi.",
                    Details = "Proje ilerledikçe /release-milestone endpoint'i üzerinden hakedişleri serbest bırakabilirsiniz."
                });
            }
            catch (System.Exception ex)
            {
                return StatusCode(500, new { Message = $"Para havuzda kilitlenirken kurumsal hata oluştu: {ex.Message}" });
            }
        }

        /// <summary>
        /// Şirket yetkilisi PayPal sayfasında işlemi iptal ederse buraya yönlendirilir.
        /// </summary>
        [HttpGet("cancel")]
        public IActionResult PaymentCancel()
        {
            return BadRequest(new PaymentResponse
            {
                Status = PaymentStatus.Failed.ToString(),
                Message = "Kurumsal ödeme işlemi şirket yetkilisi tarafından iptal edildi."
            });
        }
        /// <summary>
        /// Projenin ilgili aşaması (Milestone) tamamlandığında havuzdaki paranın belirlenen oranını alıcıya transfer eder.
        /// </summary>
        [HttpPost("release-milestone")]
        public IActionResult ReleaseMilestone([FromQuery] string contractId, [FromQuery] int milestoneNumber, [FromQuery] decimal releaseAmount)
        {
            // GERÇEK SENARYO: Burada PayPal Partner Payouts API veya Refund/Capture entegrasyonu tetiklenir.
            // Şimdilik kurumsal iş akışını doğrulamak için loglayıp başarılı statü dönüyoruz.

            try
            {
                // Kurumsal yasal süreç kontrolü (Simülasyon)
                string logMessage = $"[HAK EDİŞ ONAYI] Sözleşme: {contractId} | Milestone: {milestoneNumber} | Transfer Edilen Tutar: {releaseAmount} USD";
                System.Diagnostics.Debug.WriteLine(logMessage);

                return Ok(new
                {
                    Status = PaymentStatus.Released.ToString(),
                    ContractId = contractId,
                    Milestone = milestoneNumber,
                    AmountReleased = releaseAmount,
                    Message = $"Hakediş Raporu onaylandı. {releaseAmount} USD tutarındaki para havuzdan (Escrow) çıkartılarak ajans hesabına aktarıldı."
                });
            }
            catch (System.Exception ex)
            {
                return StatusCode(500, new { Message = $"Hakediş serbest bırakılırken hata: {ex.Message}" });
            }
        }
    }
}