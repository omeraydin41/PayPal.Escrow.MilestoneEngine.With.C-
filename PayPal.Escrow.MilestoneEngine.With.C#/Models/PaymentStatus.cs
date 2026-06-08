namespace PayPal.Escrow.MilestoneEngine.With.C_.Models
{
    public enum PaymentStatus
    {
        Created,        // Ödeme emri PayPal'da oluşturuldu, onay bekliyor
        EscrowLocked,   // Müşteri onayladı, para havuzda bloke edildi
        Released,       // Hak ediş (Milestone) onaylandı, para alıcıya aktarıldı
        Failed          // İşlem başarısız
    }
}
