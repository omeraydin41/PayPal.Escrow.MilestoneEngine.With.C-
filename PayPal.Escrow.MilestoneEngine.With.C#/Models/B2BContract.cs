using System;
using System.Collections.Generic;

namespace PayPal.Escrow.MilestoneEngine.With.C_.Models
{
    public class B2BContract
    {
        public string ContractId { get; set; } = string.Empty;
        public decimal TotalAmount { get; set; }
        public string Currency { get; set; } = "USD";
        public string OrderId { get; set; } = string.Empty; // PayPal Sipariş ID
        public PaymentStatus Status { get; set; } = PaymentStatus.Created;
        public List<Milestone> Milestones { get; set; } = new List<Milestone>();
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class Milestone
    {
        public int MilestoneNumber { get; set; } // Örn: 1, 2
        public decimal Amount { get; set; }      // Bu aşamada ödenecek tutar
        public bool IsReleased { get; set; }     // Para ajansa aktarıldı mı?
        public string Description { get; set; } = string.Empty;
    }
}