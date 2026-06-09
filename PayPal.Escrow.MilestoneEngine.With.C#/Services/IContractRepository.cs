using PayPal.Escrow.MilestoneEngine.With.C_.Models;
using System.Threading.Tasks;

namespace PayPal.Escrow.MilestoneEngine.With.C_.Services
{
    public interface IContractRepository
    {
        Task SaveContractAsync(B2BContract contract);
        Task<B2BContract?> GetContractAsync(string contractId);
        Task<B2BContract?> GetContractByOrderIdAsync(string orderId);
        Task UpdateContractAsync(B2BContract contract);
    }
}