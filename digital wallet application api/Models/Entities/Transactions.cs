using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace digital_wallet_application_api.Models.Entities
{
    public class Transaction
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public Guid TransactionId { get; set; }

        [Required]
        public Guid UserId { get; set; }

        [MaxLength(100)]
        public string CounterPartyFirstName { get; set; }

        [MaxLength(100)]
        public string CounterPartyLastName { get; set; }

        public DateTime TransactionDate { get; set; }

        [Required]
        [Column(TypeName = "decimal(18, 2)")]
        public decimal Amount { get; set; }

        [Required]
        [MaxLength(50)]
        public string Type { get; set; }

        [ForeignKey("UserId")]
        public User User { get; set; }
    }
}
