using Microsoft.AspNetCore.Mvc;
using PayPal.Escrow.MilestoneEngine.With.C_.Models;
using PayPal.Escrow.MilestoneEngine.With.C_.Services;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PayPal.Escrow.MilestoneEngine.With.C_.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PaymentController : ControllerBase
    {
        private readonly IPaypalService _paypalService;
        private readonly IContractRepository _contractRepository;

        // Repository katmanını sisteme enjekte ediyoruz
        public PaymentController(IPaypalService paypalService, IContractRepository contractRepository)
        {
            _paypalService = paypalService;
            _contractRepository = contractRepository;
        }

        /// <summary>
        /// B2B Sözleşmesi ve Hakedişleri ile birlikte Escrow (Güvenceli Havuz) sürecini başlatır.
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
                // 1. Önce PayPal üzerinde siparişi başlatıyoruz
                var order = await _paypalService.CreateEscrowOrderAsync(request.TotalAmount, request.Currency, request.ContractId);
                var approveUrl = order.Links.FirstOrDefault(x => x.Rel.Equals("approve", System.StringComparison.OrdinalIgnoreCase))?.Href;

                if (string.IsNullOrEmpty(approveUrl))
                {
                    return StatusCode(500, new PaymentResponse { Message = "PayPal onay linki üretilemedi." });
                }

                // 2. [YENİ] Sözleşmeyi ve kurumsal aşamalarını (Milestone) veritabanımıza kaydediyoruz
                var contract = new B2BContract
                {
                    ContractId = request.ContractId,
                    TotalAmount = request.TotalAmount,
                    Currency = request.Currency,
                    OrderId = order.Id, // PayPal eşleşmesi için kritik
                    Status = PaymentStatus.Created,
                    Milestones = new List<Milestone>
                    {
                        // Örnek olarak kurumsal senaryodaki gibi tutarı %50 - %50 iki hakedişe bölüyoruz
                        new Milestone { MilestoneNumber = 1, Amount = request.TotalAmount * 0.5m, IsReleased = false, Description = "Milestone 1: %50 İlk Teslim Raporu" },
                        new Milestone { MilestoneNumber = 2, Amount = request.TotalAmount * 0.5m, IsReleased = false, Description = "Milestone 2: %50 Proje Kapanışı ve Canlıya Alım" }
                    }
                };

                await _contractRepository.SaveContractAsync(contract);

                return Ok(new PaymentResponse
                {
                    OrderId = order.Id,
                    Status = contract.Status.ToString(),
                    RedirectUrl = approveUrl,
                    Message = $"Sözleşme {request.ContractId} sisteme kaydedildi ve %50 oranında 2 adet hakediş (Milestone) tanımlandı. Lütfen RedirectUrl üzerinden ödemeyi onaylayın."
                });
            }
            catch (System.Exception ex)
            {
                return StatusCode(500, new PaymentResponse { Message = $"Havuz oluşturulurken hata meydana geldi: {ex.Message}" });
            }
        }

        /// <summary>
        /// Müşteri onayından sonra parayı havuzda bloke eder ve kontrat durumunu günceller.
        /// </summary>
        [HttpGet("success")]
        public async Task<IActionResult> PaymentSuccess([FromQuery] string token, [FromQuery] string PayerID)
        {
            try
            {
                // 1. PayPal üzerinde parayı bloke et
                var capturedOrder = await _paypalService.CaptureEscrowOrderAsync(token);

                // 2. [YENİ] PayPal sipariş ID'sinden ilgili kurumsal sözleşmeyi bul
                var contract = await _contractRepository.GetContractByOrderIdAsync(token);
                if (contract != null)
                {
                    // Sözleşme durumunu havuzda kilitli (EscrowLocked) olarak güncelle
                    contract.Status = PaymentStatus.EscrowLocked;
                    await _contractRepository.UpdateContractAsync(contract);
                }

                return Ok(new
                {
                    Status = PaymentStatus.EscrowLocked.ToString(),
                    ContractId = contract?.ContractId,
                    Message = "Müşteri şirket ödemeyi onayladı. Para güvenli havuza (Escrow) alındı ve veritabanında güncellendi."
                });
            }
            catch (System.Exception ex)
            {
                return StatusCode(500, new { Message = $"Para havuzda kilitlenirken hata oluştu: {ex.Message}" });
            }
        }

        /// <summary>
        /// Sözleşmeye ait belirli bir hakedişi (Milestone) onaylar ve parayı ajansa aktarır.
        /// </summary>
        [HttpPost("release-milestone")]
        public async Task<IActionResult> ReleaseMilestone([FromQuery] string contractId, [FromQuery] int milestoneNumber)
        {
            try
            {
                // 1. Sözleşmeyi getir
                var contract = await _contractRepository.GetContractAsync(contractId);
                if (contract == null) return NotFound(new { Message = "Sözleşme bulunamadı." });

                // 2. İlgili Milestone'u bul
                var milestone = contract.Milestones.FirstOrDefault(m => m.MilestoneNumber == milestoneNumber);
                if (milestone == null) return BadRequest(new { Message = "Belirtilen hakediş aşaması bulunamadı." });
                if (milestone.IsReleased) return BadRequest(new { Message = "Bu hakediş zaten daha önce ödenmiş." });
               
                // [YENİ KONTROL] Eğer uyuşmazlık varsa hakediş ödemesi yapılamaz!
                if (contract.Status == PaymentStatus.Disputed)
                {
                    return BadRequest(new { Message = "Bu sözleşme üzerinde uyuşmazlık (Dispute) bulunmaktadır. Çözülene kadar hakediş serbest bırakılamaz." });
                }
                // 3. Durumu güncelle (Parayı serbest bırak)
                milestone.IsReleased = true;

                // Eğer tüm hakedişler ödendiyse sözleşmeyi tamamen kapat (Released)
                if (contract.Milestones.All(m => m.IsReleased))
                {
                    contract.Status = PaymentStatus.Released;
                }

                await _contractRepository.UpdateContractAsync(contract);

                return Ok(new
                {
                    Status = contract.Status.ToString(),
                    ContractId = contract.ContractId,
                    ReleasedMilestone = milestone.MilestoneNumber,
                    TransferAmount = milestone.Amount,
                    Message = $"{milestone.Description} başarıyla onaylandı. {milestone.Amount} {contract.Currency} ajans hesabına aktarıldı."
                });
            }
            catch (System.Exception ex)
            {
                return StatusCode(500, new { Message = $"Hakediş serbest bırakılırken hata: {ex.Message}" });
            }

        }
        /// <summary>
        /// Sözleşmeyi iptal eder ve havuzda kalan (ödenmemiş) tüm tutarı müşteriye iade eder.
        /// </summary>
        [HttpPost("{contractId}/cancel-and-refund")]
        public async Task<IActionResult> CancelAndRefund(string contractId)
        {
            try
            {
                var contract = await _contractRepository.GetContractAsync(contractId);
                if (contract == null) return NotFound(new { Message = "Sözleşme bulunamadı." });

                if (contract.Status != PaymentStatus.EscrowLocked && contract.Status != PaymentStatus.Disputed)
                {
                    return BadRequest(new { Message = "Sadece havuzda kilitli veya uyuşmazlık durumundaki sözleşmeler iptal edilebilir." });
                }

                // Henüz ajansa ödenmemiş hakedişlerin toplamını bul
                var refundableAmount = contract.Milestones
                    .Where(m => !m.IsReleased)
                    .Sum(m => m.Amount);

                if (refundableAmount <= 0)
                {
                    return BadRequest(new { Message = "İade edilecek serbest kalmamış hakediş bulunamadı." });
                }

                // TODO: İleride buraya PayPal API Refund entegrasyonu gelecek.
                // Örn: await _paypalService.RefundOrderAsync(contract.OrderId, refundableAmount);

                contract.Status = PaymentStatus.Failed;
                await _contractRepository.UpdateContractAsync(contract);

                return Ok(new
                {
                    Status = contract.Status.ToString(),
                    RefundedAmount = refundableAmount,
                    Message = $"Sözleşme iptal edildi. Havuzda bekleyen {refundableAmount} {contract.Currency} tutarı müşteriye iade edildi."
                });
            }
            catch (System.Exception ex)
            {
                return StatusCode(500, new { Message = $"İade işlemi sırasında hata: {ex.Message}" });
            }
        }

        /// <summary>
        /// Sözleşmenin ilerleme durumunu ve finansal havuz özetini (Dashboard için) getirir.
        /// </summary>
        [HttpGet("{contractId}/status")]
        public async Task<IActionResult> GetContractStatus(string contractId)
        {
            var contract = await _contractRepository.GetContractAsync(contractId);
            if (contract == null) return NotFound(new { Message = "Sözleşme bulunamadı." });

            var totalPaid = contract.Milestones.Where(m => m.IsReleased).Sum(m => m.Amount);
            var totalLocked = contract.Milestones.Where(m => !m.IsReleased).Sum(m => m.Amount);
            var completedMilestonesCount = contract.Milestones.Count(m => m.IsReleased);

            return Ok(new
            {
                ContractId = contract.ContractId,
                CurrentStatus = contract.Status.ToString(),
                Currency = contract.Currency,
                FinancialSummary = new
                {
                    TotalContractAmount = contract.TotalAmount,
                    AmountPaidToAgency = totalPaid,       // Ajansa giden
                    AmountLockedInEscrow = totalLocked,   // Havuzda kalan
                    ProgressPercentage = contract.TotalAmount > 0 ? (totalPaid / contract.TotalAmount) * 100 : 0
                },
                MilestonesSummary = new
                {
                    TotalMilestones = contract.Milestones.Count,
                    CompletedMilestones = completedMilestonesCount,
                    PendingMilestones = contract.Milestones.Count - completedMilestonesCount
                },
                Milestones = contract.Milestones
            });
        }

        /// <summary>
        /// Taraflar arasında anlaşmazlık çıktığında havuzdaki parayı bloke eder ve işlemleri dondurur.
        /// </summary>
        [HttpPost("{contractId}/raise-dispute")]
        public async Task<IActionResult> RaiseDispute(string contractId, [FromBody] DisputeRequest request)
        {
            var contract = await _contractRepository.GetContractAsync(contractId);
            if (contract == null) return NotFound(new { Message = "Sözleşme bulunamadı." });

            if (contract.Status != PaymentStatus.EscrowLocked)
            {
                return BadRequest(new { Message = "Sadece aktif ve havuzda parası kilitli olan sözleşmeler için uyuşmazlık başlatılabilir." });
            }

            contract.Status = PaymentStatus.Disputed;
            await _contractRepository.UpdateContractAsync(contract);

            return Ok(new
            {
                Status = contract.Status.ToString(),
                ContractId = contract.ContractId,
                Reason = request.Reason,
                Message = "Sözleşme askıya alındı (Disputed). Sistem yöneticisi hakemlik edene kadar hiçbir hakediş (Milestone) ödemesi yapılamaz."
            });
        }

        /*
         * git commit -m "
        ENG : Three new functional endpoints—which block funds in the escrow account in the event of a dispute,
        provide a financial status summary for the Dashboard, and refund the remaining amount to the customer 
        in the event of a cancellation—have been successfully designed and added to the controllers and models.
        TUR : Uyuşmazlık (Dispute) durumunda havuzdaki parayı bloke eden, Dashboard için finansal durum özetini getiren ve 
        iptal durumunda kalan tutarı müşteriye iade eden 3 yeni işlevsel endpoint başarıyla tasarlanıp controller ve modellere eklendi."
         */

    }
}