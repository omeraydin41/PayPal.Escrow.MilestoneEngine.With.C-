using PayPal.Escrow.MilestoneEngine.With.C_.Models;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace PayPal.Escrow.MilestoneEngine.With.C_.Services
{
    public class ContractRepository : IContractRepository
    {
        // Thread-safe bir in-memory koleksiyon kullanarak DB simülasyonu yapıyoruz
        private static readonly ConcurrentDictionary<string, B2BContract> _db = new ConcurrentDictionary<string, B2BContract>();

        public Task SaveContractAsync(B2BContract contract)
        {
            _db[contract.ContractId] = contract;
            return Task.CompletedTask;
        }

        public Task<B2BContract?> GetContractAsync(string contractId)
        {
            _db.TryGetValue(contractId, out var contract);
            return Task.FromResult(contract);
        }

        public Task<B2BContract?> GetContractByOrderIdAsync(string orderId)
        {
            foreach (var contract in _db.Values)
            {
                if (contract.OrderId == orderId) return Task.FromResult<B2BContract?>(contract);
            }
            return Task.FromResult<B2BContract?>(null);
        }

        public Task UpdateContractAsync(B2BContract contract)
        {
            _db[contract.ContractId] = contract;
            return Task.CompletedTask;
        }
    }
}