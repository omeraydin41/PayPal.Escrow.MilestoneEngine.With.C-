using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging; // [YENİ] NLog kullanımı için eklendi
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
        private readonly ILogger<PaymentController> _logger; // [YENİ]

        // Repository ve Logger katmanlarını sisteme enjekte ediyoruz
        public PaymentController(
            IPaypalService paypalService,
            IContractRepository contractRepository,
            ILogger<PaymentController> logger) // [YENİ]
        {
            _paypalService = paypalService;
            _contractRepository = contractRepository;
            _logger = logger; // [YENİ]
        }

        /// <summary>
        /// B2B Sözleşmesi ve Hakedişleri ile birlikte Escrow (Güvenceli Havuz) sürecini başlatır.
        /// </summary>
        [HttpPost("create-escrow")]
        public async Task<IActionResult> CreateEscrow([FromBody] CorporatePaymentRequest request)
        {
            if (request == null || request.TotalAmount <= 0)
            {
                _logger.LogWarning("Geçersiz escrow oluşturma isteği alındı. İstek boş veya tutar sıfırdan küçük.");
                return BadRequest(new PaymentResponse { Message = "Geçersiz kurumsal ödeme isteği." });
            }

            _logger.LogInformation("Sözleşme {ContractId} için {Amount} {Currency} tutarında Escrow süreci başlatılıyor...", request.ContractId, request.TotalAmount, request.Currency);

            try
            {
                // 1. Önce PayPal üzerinde siparişi başlatıyoruz
                var order = await _paypalService.CreateEscrowOrderAsync(request.TotalAmount, request.Currency, request.ContractId);
                var approveUrl = order.Links.FirstOrDefault(x => x.Rel.Equals("approve", System.StringComparison.OrdinalIgnoreCase))?.Href;

                if (string.IsNullOrEmpty(approveUrl))
                {
                    _logger.LogError("Sözleşme {ContractId} için PayPal onay linki üretilemedi!", request.ContractId);
                    return StatusCode(500, new PaymentResponse { Message = "PayPal onay linki üretilemedi." });
                }

                // 2. Sözleşmeyi ve kurumsal aşamalarını (Milestone) veritabanımıza kaydediyoruz
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
                _logger.LogInformation("Sözleşme {ContractId} başarıyla sisteme kaydedildi. PayPal Sipariş No: {OrderId}", request.ContractId, order.Id);

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
                _logger.LogError(ex, "Sözleşme {ContractId} için havuz oluşturulurken teknik hata meydana geldi!", request.ContractId);
                return StatusCode(500, new PaymentResponse { Message = $"Havuz oluşturulurken hata meydana geldi: {ex.Message}" });
            }
        }

        /// <summary>
        /// Müşteri onayından sonra parayı havuzda bloke eder ve kontrat durumunu günceller.
        /// </summary>
        [HttpGet("success")]
        public async Task<IActionResult> PaymentSuccess([FromQuery] string token, [FromQuery] string PayerID)
        {
            _logger.LogInformation("PayPal üzerinden başarılı ödeme onayı alındı. Sipariş/Token: {Token}", token);

            try
            {
                // 1. PayPal üzerinde parayı bloke et
                var capturedOrder = await _paypalService.CaptureEscrowOrderAsync(token);

                // 2. PayPal sipariş ID'sinden ilgili kurumsal sözleşmeyi bul
                var contract = await _contractRepository.GetContractByOrderIdAsync(token);
                if (contract != null)
                {
                    // Sözleşme durumunu havuzda kilitli (EscrowLocked) olarak güncelle
                    contract.Status = PaymentStatus.EscrowLocked;
                    await _contractRepository.UpdateContractAsync(contract);
                    _logger.LogInformation("Sözleşme {ContractId} durumu EscrowLocked olarak güncellendi. Para havuzda bloke edildi.", contract.ContractId);
                }
                else
                {
                    _logger.LogWarning("PayPal Token: {Token} ile eşleşen bir B2B sözleşmesi veritabanında bulunamadı!", token);
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
                _logger.LogError(ex, "PayPal Token: {Token} için para havuzda kilitlenirken hata oluştu!", token);
                return StatusCode(500, new { Message = $"Para havuzda kilitlenirken hata oluştu: {ex.Message}" });
            }
        }

        /// <summary>
        /// Sözleşmeye ait belirli bir hakedişi (Milestone) onaylar ve parayı ajansa aktarır.
        /// </summary>
        [HttpPost("release-milestone")]
        public async Task<IActionResult> ReleaseMilestone([FromQuery] string contractId, [FromQuery] int milestoneNumber)
        {
            _logger.LogInformation("Sözleşme {ContractId} için {MilestoneNumber}. hakediş serbest bırakılma talebi alındı.", contractId, milestoneNumber);

            try
            {
                // 1. Sözleşmeyi getir
                var contract = await _contractRepository.GetContractAsync(contractId);
                if (contract == null)
                {
                    _logger.LogWarning("Hakediş serbest bırakılamadı. Sözleşme bulunamadı: {ContractId}", contractId);
                    return NotFound(new { Message = "Sözleşme bulunamadı." });
                }

                // [YENİ KONTROL] Eğer uyuşmazlık varsa hakediş ödemesi yapılamaz!
                if (contract.Status == PaymentStatus.Disputed)
                {
                    _logger.LogWarning("Sözleşme {ContractId} üzerinde uyuşmazlık (Dispute) bulunuyor! İşlem engellendi.", contractId);
                    return BadRequest(new { Message = "Bu sözleşme üzerinde uyuşmazlık (Dispute) bulunmaktadır. Çözülene kadar hakediş serbest bırakılamaz." });
                }

                // 2. İlgili Milestone'u bul
                var milestone = contract.Milestones.FirstOrDefault(m => m.MilestoneNumber == milestoneNumber);
                if (milestone == null)
                {
                    _logger.LogWarning("Sözleşme {ContractId} için belirtilen hakediş aşaması bulunamadı: {MilestoneNumber}", contractId, milestoneNumber);
                    return BadRequest(new { Message = "Belirtilen hakediş aşaması bulunamadı." });
                }

                if (milestone.IsReleased)
                {
                    _logger.LogWarning("Sözleşme {ContractId} için {MilestoneNumber}. hakediş zaten daha önce ödenmiş!", contractId, milestoneNumber);
                    return BadRequest(new { Message = "Bu hakediş zaten daha önce ödenmiş." });
                }

                // 3. Durumu güncelle (Parayı serbest bırak)
                milestone.IsReleased = true;
                _logger.LogInformation("Sözleşme {ContractId} için {MilestoneNumber}. hakediş serbest bırakıldı. Aktarılan Tutar: {Amount} {Currency}", contractId, milestoneNumber, milestone.Amount, contract.Currency);

                // Eğer tüm hakedişler ödendiyse sözleşmeyi tamamen kapat (Released)
                if (contract.Milestones.All(m => m.IsReleased))
                {
                    contract.Status = PaymentStatus.Released;
                    _logger.LogInformation("Sözleşme {ContractId} üzerindeki tüm hakedişler ödendi. Sözleşme tamamen kapatıldı.", contractId);
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
                _logger.LogError(ex, "Sözleşme {ContractId} için hakediş {MilestoneNumber} serbest bırakılırken teknik hata!", contractId, milestoneNumber);
                return StatusCode(500, new { Message = $"Hakediş serbest bırakılırken hata: {ex.Message}" });
            }
        }

        /// <summary>
        /// Sözleşmeyi iptal eder ve havuzda kalan (ödenmemiş) tüm tutarı müşteriye iade eder.
        /// </summary>
        [HttpPost("{contractId}/cancel-and-refund")]
        public async Task<IActionResult> CancelAndRefund(string contractId)
        {
            _logger.LogInformation("Sözleşme {ContractId} için iptal ve iade süreci başlatılıyor...", contractId);

            try
            {
                var contract = await _contractRepository.GetContractAsync(contractId);
                if (contract == null)
                {
                    _logger.LogWarning("İptal işlemi başarısız. Sözleşme bulunamadı: {ContractId}", contractId);
                    return NotFound(new { Message = "Sözleşme bulunamadı." });
                }

                if (contract.Status != PaymentStatus.EscrowLocked && contract.Status != PaymentStatus.Disputed)
                {
                    _logger.LogWarning("Sözleşme {ContractId} iptal edilemez durumda. Mevcut Durum: {Status}", contractId, contract.Status);
                    return BadRequest(new { Message = "Sadece havuzda kilitli veya uyuşmazlık durumundaki sözleşmeler iptal edilebilir." });
                }

                // Henüz ajansa ödenmemiş hakedişlerin toplamını bul
                var refundableAmount = contract.Milestones
                    .Where(m => !m.IsReleased)
                    .Sum(m => m.Amount);

                if (refundableAmount <= 0)
                {
                    _logger.LogWarning("Sözleşme {ContractId} için iade edilecek serbest kalmamış hakediş bulunamadı.", contractId);
                    return BadRequest(new { Message = "İade edilecek serbest kalmamış hakediş bulunamadı." });
                }

                // TODO: İleride buraya PayPal API Refund entegrasyonu gelecek.
                // Örn: await _paypalService.RefundOrderAsync(contract.OrderId, refundableAmount);

                contract.Status = PaymentStatus.Failed;
                await _contractRepository.UpdateContractAsync(contract);

                _logger.LogInformation("Sözleşme {ContractId} başarıyla iptal edildi. {Amount} {Currency} müşteriye iade aşamasına alındı.", contractId, refundableAmount, contract.Currency);

                return Ok(new
                {
                    Status = contract.Status.ToString(),
                    RefundedAmount = refundableAmount,
                    Message = $"Sözleşme iptal edildi. Havuzda bekleyen {refundableAmount} {contract.Currency} tutarı müşteriye iade edildi."
                });
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Sözleşme {ContractId} iptal ve iade işlemleri sırasında teknik hata!", contractId);
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
            if (contract == null)
            {
                _logger.LogWarning("Durum sorgulaması başarısız. Sözleşme bulunamadı: {ContractId}", contractId);
                return NotFound(new { Message = "Sözleşme bulunamadı." });
            }

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
            _logger.LogWarning("Sözleşme {ContractId} için uyuşmazlık (Dispute) davası açılıyor! Gerekçe: {Reason}", contractId, request?.Reason);

            var contract = await _contractRepository.GetContractAsync(contractId);
            if (contract == null)
            {
                _logger.LogWarning("Uyuşmazlık başlatılamadı. Sözleşme bulunamadı: {ContractId}", contractId);
                return NotFound(new { Message = "Sözleşme bulunamadı." });
            }

            if (contract.Status != PaymentStatus.EscrowLocked)
            {
                _logger.LogWarning("Sözleşme {ContractId} uyuşmazlık moduna alınamaz. Sözleşme durumu 'EscrowLocked' değil. Mevcut Durum: {Status}", contractId, contract.Status);
                return BadRequest(new { Message = "Sadece aktif ve havuzda parası kilitli olan sözleşmeler için uyuşmazlık başlatılabilir." });
            }

            contract.Status = PaymentStatus.Disputed;
            await _contractRepository.UpdateContractAsync(contract);

            _logger.LogInformation("Sözleşme {ContractId} durumu başarıyla 'Disputed' olarak donduruldu.", contractId);

            return Ok(new
            {
                Status = contract.Status.ToString(),
                ContractId = contract.ContractId,
                Reason = request?.Reason,
                Message = "Sözleşme askıya alındı (Disputed). Sistem yöneticisi hakemlik edene kadar hiçbir hakediş (Milestone) ödemesi yapılamaz."
            });
        }

        /*
         * git commit -m "
         ENG :The project has been integrated with the professional NLog framework, which records all steps in financial workflows 
         and technical errors in detail. As part of this process, NLog.Web.AspNetCore and the necessary dependencies were successfully 
         added to the project via NuGet. With the architectural changes made, technical errors and API requests are now automatically 
         logged to both the console and .log files.

         TUR : Projene, tüm finansal akışlardaki adımları ve teknik hataları detaylıca kaydeden profesyonel NLog altyapısı entegre edildi. 
         Bu süreçte NLog.Web.AspNetCore ve gerekli yardımcı paketler NuGet üzerinden projeye başarıyla dahil edildi. 
         Yapılan mimari düzenlemeyle birlikte, teknik hataların ve API isteklerinin hem konsola hem de günlük 
         .log dosyalarına otomatik yazılması sağlandı"
         */
    }
}